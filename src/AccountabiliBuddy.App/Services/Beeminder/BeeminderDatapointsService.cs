using System.Net;
using System.Text.Json;
using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Beeminder;

/// <summary>
/// Beeminder's datapoints as goal entries (ADR 84). After every goals read (<see cref="BeeminderService.GoalsRead"/>: about every 30
/// minutes and when Today or Goals opens) each goal with a Beeminder source in direction Read gets that Beeminder goal's newest
/// datapoints through <see cref="CalendarNetwork"/>, only while a token is set. A day becomes one <see cref="GoalEntrySource.Beeminder"/>
/// entry (the goal's own <c>aggday</c> rule, <see cref="BeeminderDayValue"/>), stored in <see cref="GoalStore"/> like other readings
/// (sealed at rest, this PC only: nothing here reaches a guard, the goal unlock, focusDay, the focus totals or the sync file).
/// Unlinking, a direction other than Read, a deleted goal or a removed token stops the calls and removes the entries. A failure is
/// quiet and keeps the last values; nothing here logs a token, a value or a comment. Use from the UI thread only.
/// </summary>
public sealed class BeeminderDatapointsService
{
    private const int FirstReadCount = 300;
    private const int LaterReadCount = 100;

    // Back-filled or edited older days are caught by always re-reading this many days back (not only since the newest stored day).
    private const int TrailingDays = 14;

    // Beeminder's "count" cuts by datapoints, not days: when the newest page is full and does not reach back far enough, older pages
    // (page=2.. with per=count) are read, at most this many pages in all (the first is the count page) per goal and round.
    private const int MaxPagesPerGoal = 5;

    // A newly linked goal is kicked (read now) at most this often; a kick that finds a read under way is tried again shortly.
    private static readonly TimeSpan KickMinimum = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan KickRetry = TimeSpan.FromSeconds(10);
    private const int MaxBusyRetries = 6;
    private static readonly TimeSpan RateLimitPause = TimeSpan.FromMinutes(30);

    private readonly CalendarNetwork _network;
    private readonly GoalStore _store;
    private readonly BeeminderService _beeminder;
    private readonly TimeProvider _time;
    private DateTimeOffset _notBefore = DateTimeOffset.MinValue;
    private bool _reading;
    private DateTimeOffset _lastKickAt = DateTimeOffset.MinValue;
    private bool _kickRunning;
    private HashSet<BeeminderLink> _readLinks = [];

    public BeeminderDatapointsService(CalendarNetwork network, GoalStore store, BeeminderService beeminder, TimeProvider time)
    {
        _network = network;
        _store = store;
        _beeminder = beeminder;
        _time = time;
        _beeminder.GoalsRead = ReadAsync;
        _beeminder.Changed += (_, _) => _ = ReconcileAsync();
        _store.Changed += (_, _) => StoreChanged();
    }

    /// <summary>When the datapoints were last read for every linked goal; null before that.</summary>
    public DateTimeOffset? LastReadAt { get; private set; }

    /// <summary>The last read failed (network, token, rate limit); the last values stay.</summary>
    public bool Failed { get; private set; }

    public event EventHandler? Changed;

    /// <summary>
    /// Removes this PC's Beeminder entries no live Read link owns (ADR 84: with no token that is all of this PC's own; another PC's stay, <see cref="BeeminderEntries.Prune"/>).
    /// </summary>
    public Task ReconcileAsync()
    {
        return _store.ChangeAsync(this, (lists, at) => BeeminderEntries.Prune(lists, ActiveLinks(lists), _store.SelfId(), at));
    }

    /// <summary>Reads the datapoints of every linked goal (called by <see cref="BeeminderService"/> after it read the goals).</summary>
    public async Task ReadAsync(string token, IReadOnlyList<BeeminderGoal> goals)
    {
        if (_reading || !_store.IsReady)
        {
            return;
        }

        _reading = true;
        try
        {
            await ReconcileAsync();
            IReadOnlyList<BeeminderLink> links = ActiveLinks(_store.Lists);
            _readLinks = [.. links];
            bool failed = false;
            if (_time.GetUtcNow() < _notBefore)
            {
                failed = links.Count > 0;
                links = [];
            }

            foreach (BeeminderLink link in links)
            {
                (BeeminderGoal? goal, bool ambiguous) = FindGoal(goals, link.Key);
                string slug = goal is { Slug.Length: > 0 } ? goal.Slug : link.Key;
                if (ambiguous || goal is null || !BeeminderEntries.IsValidSlug(slug))
                {
                    continue; // Not in Beeminder's list (any more), a title that fits two goals, or not a slug: nothing to ask, the last values stay.
                }

                ReadOutcome outcome = await ReadOneAsync(token, link, slug, goal.AggDay);
                if (outcome != ReadOutcome.Done)
                {
                    failed = true;
                    if (outcome == ReadOutcome.StopRound)
                    {
                        break; // Network trouble, 429 or a 5xx: one quiet failure is enough for this round (no timeout for every goal).
                    }
                }
            }

            Failed = failed;
            if (!failed)
            {
                LastReadAt = _time.GetUtcNow();
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _reading = false;
        }
    }

    /// <summary>
    /// The Beeminder goal a link points at: by slug first (a link made before ADR 84 stored the title, which is its key then); by title
    /// only when exactly one Beeminder goal has that title. A title that fits several goals is ambiguous: no guess, the link is skipped.
    /// </summary>
    public static (BeeminderGoal? Goal, bool Ambiguous) FindGoal(IReadOnlyList<BeeminderGoal> goals, string key)
    {
        BeeminderGoal? bySlug = goals.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        if (bySlug is not null)
        {
            return (bySlug, false);
        }

        List<BeeminderGoal> byTitle = goals.Where(item => string.Equals(item.Title, key, StringComparison.Ordinal)).ToList();
        return byTitle.Count == 1 ? (byTitle[0], false) : (null, byTitle.Count > 1);
    }

    private enum ReadOutcome
    {
        Done,

        /// <summary>This goal's read failed (a 4xx, an unreadable answer): the others are still read.</summary>
        SkipLink,

        /// <summary>The network, a 429 or a 5xx failed: the other goals would fail too, so the round stops.</summary>
        StopRound,
    }

    private async Task<ReadOutcome> ReadOneAsync(string token, BeeminderLink link, string slug, string aggDay)
    {
        DateTimeOffset now = _time.GetUtcNow();
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _time.LocalTimeZone).DateTime);
        DateOnly? newest = BeeminderEntries.NewestDay(_store.Lists, link);
        DateOnly floor = today.AddDays(-(int)BeeminderEntries.FirstRead.TotalDays);
        DateOnly from = newest is null ? floor : DateOnly.FromDayNumber(Math.Min(newest.Value.DayNumber, today.AddDays(-TrailingDays).DayNumber));
        if (from < floor)
        {
            from = floor;
        }

        int count = newest is null ? FirstReadCount : LaterReadCount;
        var points = new List<BeeminderDatapoint>();
        DateOnly coveredFrom = from;
        try
        {
            for (int page = 1; page <= MaxPagesPerGoal; page++)
            {
                using JsonDocument answer = await _network.GetBeeminderDatapointsAsync(token, slug, count, CancellationToken.None, page == 1 ? null : page);
                IReadOnlyList<BeeminderDatapoint> got = BeeminderDatapointsParser.Parse(answer.RootElement);
                points.AddRange(got);
                if (got.Count < count || got.Min(point => point.Day) < from)
                {
                    break; // Short page: all there is. Reaches back before the window: the window is whole.
                }

                if (page == MaxPagesPerGoal)
                {
                    // Full and still inside the window: its oldest day may be cut. Drop that day; the window starts the day after it.
                    coveredFrom = got.Min(point => point.Day).AddDays(1);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _notBefore = _time.GetUtcNow() + RateLimitPause;
                return ReadOutcome.StopRound;
            }

            return ex.StatusCode is HttpStatusCode status && (int)status is >= 400 and < 500 ? ReadOutcome.SkipLink : ReadOutcome.StopRound;
        }
        catch (TaskCanceledException)
        {
            return ReadOutcome.StopRound;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return ReadOutcome.SkipLink;
        }

        // A page that shifted between two calls repeats a datapoint: one per id (a point without an id is kept as it is).
        List<BeeminderDatapoint> unique = points
            .Where(point => point.Day >= coveredFrom)
            .GroupBy(point => point.Id.Length > 0 ? point.Id : null)
            .SelectMany(group => group.Key is null ? group : group.Take(1))
            .ToList();
        var days = unique
            .GroupBy(point => point.Day)
            .ToDictionary(group => group.Key, group => BeeminderDayValue.Of(aggDay, group.ToList()));
        await _store.ChangeAsync(this, (lists, at) =>
            ActiveLinks(lists).Contains(link) ? BeeminderEntries.Apply(lists, link, coveredFrom, days, _store.SelfId(), at) : lists);
        return ReadOutcome.Done;
    }

    private IReadOnlyList<BeeminderLink> ActiveLinks(GoalLists lists) =>
        _beeminder.HasToken ? BeeminderEntries.LinksOf(lists.LiveGoals) : [];

    // A goal's sources changed: entries of what is no longer linked go; a newly linked goal is read now, not in half an hour.
    private void StoreChanged()
    {
        // Always: even while a read runs, an unlinked goal's entries must go (our own writes make this a no-op).
        _ = ReconcileAsync();
        IReadOnlyList<BeeminderLink> linked = BeeminderEntries.LinksOf(_store.Lists.LiveGoals);
        _readLinks.IntersectWith(linked); // an unlinked goal that is linked again is new again
        if (NeedsKick())
        {
            _ = KickAsync();
        }
    }

    private bool NeedsKick() =>
        _beeminder.HasToken && BeeminderEntries.LinksOf(_store.Lists.LiveGoals).Any(link => !_readLinks.Contains(link));

    // At most one kick every 5 minutes. One that comes too soon waits for the minimum; one that finds a read under way (ours, or
    // the goals read answering Busy) tries again shortly: a newly linked goal's kick is never dropped. One loop runs at a time and
    // looks at the newest state on every turn.
    private async Task KickAsync()
    {
        if (_kickRunning)
        {
            return;
        }

        _kickRunning = true;
        try
        {
            int busy = 0;
            while (NeedsKick())
            {
                TimeSpan wait = KickMinimum - (_time.GetUtcNow() - _lastKickAt);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait);
                    continue;
                }

                if (!_reading)
                {
                    _lastKickAt = _time.GetUtcNow();
                    if (await _beeminder.RefreshAsync() != BeeminderRefresh.Busy)
                    {
                        return;
                    }

                    _lastKickAt = DateTimeOffset.MinValue; // nothing was asked, so the minimum does not hold the retry back
                }

                if (++busy > MaxBusyRetries)
                {
                    return;
                }

                await Task.Delay(KickRetry);
            }
        }
        finally
        {
            _kickRunning = false;
        }
    }
}
