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
    private static readonly TimeSpan RateLimitPause = TimeSpan.FromMinutes(30);

    private readonly CalendarNetwork _network;
    private readonly GoalStore _store;
    private readonly BeeminderService _beeminder;
    private readonly TimeProvider _time;
    private DateTimeOffset _notBefore = DateTimeOffset.MinValue;
    private bool _reading;
    private DateTimeOffset _lastKickAt = DateTimeOffset.MinValue;
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

    /// <summary>Removes the Beeminder entries no live Read link owns (also all of them without a token).</summary>
    public Task ReconcileAsync()
    {
        return _store.ChangeAsync(this, (lists, _) => BeeminderEntries.Prune(lists, ActiveLinks(lists)));
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
                BeeminderGoal? goal = goals.FirstOrDefault(item => string.Equals(item.Key, link.Key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Title, link.Key, StringComparison.Ordinal));
                string slug = goal is { Slug.Length: > 0 } ? goal.Slug : link.Key;
                if (goal is null || !BeeminderEntries.IsValidSlug(slug))
                {
                    continue; // Not in Beeminder's list (any more) or not a slug: nothing to ask, the last values stay.
                }

                if (!await ReadOneAsync(token, link, slug, goal.AggDay))
                {
                    failed = true;
                    break; // One quiet failure is enough for this round (no timeout for every goal).
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

    private async Task<bool> ReadOneAsync(string token, BeeminderLink link, string slug, string aggDay)
    {
        DateTimeOffset now = _time.GetUtcNow();
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _time.LocalTimeZone).DateTime);
        DateOnly? newest = BeeminderEntries.NewestDay(_store.Lists, link);
        DateOnly from = newest ?? today.AddDays(-(int)BeeminderEntries.FirstRead.TotalDays);
        IReadOnlyList<BeeminderDatapoint> points;
        try
        {
            using JsonDocument answer = await _network.GetBeeminderDatapointsAsync(
                token, slug, newest is null ? FirstReadCount : LaterReadCount, CancellationToken.None);
            points = BeeminderDatapointsParser.Parse(answer.RootElement);
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _notBefore = _time.GetUtcNow() + RateLimitPause;
            }

            return false;
        }
        catch (Exception ex) when (ex is TaskCanceledException or JsonException or ArgumentException)
        {
            return false;
        }

        var days = points
            .Where(point => point.Day >= from)
            .GroupBy(point => point.Day)
            .ToDictionary(group => group.Key, group => BeeminderDayValue.Of(aggDay, group.ToList()));
        await _store.ChangeAsync(this, (lists, at) =>
            ActiveLinks(lists).Contains(link) ? BeeminderEntries.Apply(lists, link, from, days, at) : lists);
        return true;
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
        if (!_reading && _beeminder.HasToken && _time.GetUtcNow() - _lastKickAt >= TimeSpan.FromMinutes(1)
            && linked.Any(link => !_readLinks.Contains(link)))
        {
            _lastKickAt = _time.GetUtcNow();
            _ = _beeminder.RefreshAsync();
        }
    }
}
