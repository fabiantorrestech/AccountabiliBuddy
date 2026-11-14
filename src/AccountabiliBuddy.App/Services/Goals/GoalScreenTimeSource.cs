using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Goals;
using AccountabiliBuddy.Core.Ports;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// This PC's screen time for goals (screen-time source v2, ST2; ADR 8, 17, 79). Counted from this PC's own activity history only: one
/// <see cref="IActivityLog.GetSummaryAsync"/> per local day, the rows matched by <see cref="GoalAppMatching"/> through
/// <see cref="GoalScreenTimeRows"/> (process, shown name or website; a row counts once). Time the user deleted from History
/// (<see cref="UsageDeletion"/>) counts nowhere.
/// <list type="bullet">
/// <item>Days before yesterday are read once and kept in memory (their matched minutes too, per entries and names); today is read on every
/// refresh and yesterday once more on the first refresh after the day changed (a segment that ends at midnight may be written after it). One
/// apps-and-sites query per day (<see cref="IActivityLog.GetAppsAndSitesAsync"/>), all of a refresh's days on one thread-pool hop. A refresh
/// is asked for by <see cref="GoalSourceDataService"/>, which already coalesces the log's writes to one read every 3 seconds. Not refreshed
/// by a sleep of over a day: a day cached as today or yesterday before it is not read again until the next change of day or restart.</item>
/// <item>Time is the log's: summed per monitor, as History shows it (two monitors both showing a matching app count twice; Q1117).</item>
/// <item>Names, titles and sites stay in memory on this PC. Only the whole minutes of a day leave it, and only as a derived goal entry
/// (<see cref="GoalDerivedValuesService"/>). Nothing here logs; there is no new connection.</item>
/// <item>The goal's rule decides whether this PC counts it (<see cref="GoalAppMatching.CountsOn"/>).</item>
/// </list>
/// Use from the UI thread only; the reads run on the thread pool.
/// </summary>
public sealed class GoalScreenTimeSource
{
    /// <summary>How many days back (today included) a goal's screen time is read: the activity history's retention (ADR 8).</summary>
    public const int Days = 90;

    private readonly IActivityLog _log;
    private readonly Func<IReadOnlyList<UsageDeletion>> _deletions;
    private readonly Func<string, string> _nameFor;
    private readonly Func<string, bool> _isHidden;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<DateOnly, IReadOnlyList<AppSiteUsage>> _days = [];
    private readonly Dictionary<(string Entries, DateOnly Day), double> _minutes = [];
    private DateOnly? _rereadFor;
    private int _generation;

    /// <param name="log">This PC's activity history.</param>
    /// <param name="deletions">The time the user deleted from History.</param>
    /// <param name="nameFor">The name this PC shows for an app or site (History's renames, <see cref="DisplayNamesService.NameFor"/>).</param>
    /// <param name="time">The trusted clock (days are this PC's local days).</param>
    /// <param name="isHidden">Whether History hides an app or site (it is not offered on the pick list; it still counts).</param>
    public GoalScreenTimeSource(
        IActivityLog log, Func<IReadOnlyList<UsageDeletion>> deletions, Func<string, string> nameFor, TimeProvider time, Func<string, bool>? isHidden = null)
    {
        _log = log;
        _deletions = deletions;
        _nameFor = nameFor;
        _time = time;
        _isHidden = isHidden ?? (_ => false);
    }

    /// <summary>The first day held without a gap up to today, or null before a refresh worked (or after a read failed).</summary>
    public DateOnly? LoadedFrom { get; private set; }

    /// <summary>True once a refresh read every day it asked for.</summary>
    public bool IsLoaded => LoadedFrom is not null;

    /// <summary>The automatic screen-time rules of <paramref name="goal"/> that <paramref name="device"/> counts (never for Deep work).</summary>
    public static IReadOnlyList<GoalSourceRule> CountedRules(Goal goal, Guid device)
    {
        if (goal.IsDeepWork)
        {
            return [];
        }

        return GoalSources.CountedFor(goal)
            .Where(rule => rule.Automatic && rule.Type == GoalSourceType.ScreenTime && rule.Apps is { Count: > 0 } && GoalAppMatching.CountsOn(rule, device))
            .ToList();
    }

    /// <summary>
    /// The first day to hold so every goal this PC counts has its days: the goal's first day, at most <see cref="Days"/> back. Null when
    /// no goal has a screen-time rule this PC counts (then nothing is read at all).
    /// </summary>
    public DateOnly? DaysNeededFrom(IEnumerable<Goal> goals, Guid device, DateOnly today)
    {
        DateOnly limit = today.AddDays(-(Days - 1));
        DateOnly? from = null;
        foreach (Goal goal in goals.Where(goal => CountedRules(goal, device).Count > 0))
        {
            DateOnly start = goal.CreatedOn(_time.LocalTimeZone);
            DateOnly day = start < limit ? limit : start;
            from = from is null || day < from ? day : from;
        }

        return from;
    }

    /// <summary>Whether a refresh is needed to hold <paramref name="from"/> (nothing held yet, or only later days).</summary>
    public bool NeedsRefresh(DateOnly from)
    {
        return LoadedFrom is not DateOnly held || from < held;
    }

    /// <summary>Forgets every held day: the user deleted or restored History time, or deleted all data.</summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        _days.Clear();
        _minutes.Clear();
        _rereadFor = null;
        LoadedFrom = null;
    }

    /// <summary>The names this PC shows changed (a History rename): matched minutes are worked out again; the rows stay.</summary>
    public void NamesChanged()
    {
        _minutes.Clear();
    }

    /// <summary>
    /// Reads the days from <paramref name="from"/> to today that are not held yet, plus today and yesterday again. Any failed read leaves
    /// <see cref="LoadedFrom"/> null, so nothing is written from a half-read history.
    /// </summary>
    public async Task RefreshAsync(DateOnly from, DateOnly today)
    {
        await _gate.WaitAsync();
        try
        {
            int generation = _generation;
            IReadOnlyList<UsageDeletion> deletions = _deletions().ToList();
            DateOnly first = LoadedFrom is DateOnly held && held < from ? held : from;
            DateOnly yesterday = today.AddDays(-1);
            bool rereadYesterday = _rereadFor != today;
            var wanted = new List<DateOnly>();
            for (DateOnly day = first; day <= today; day = day.AddDays(1))
            {
                if (!_days.ContainsKey(day) || day == today || (day == yesterday && rereadYesterday))
                {
                    wanted.Add(day);
                }
            }

            Dictionary<DateOnly, IReadOnlyList<AppSiteUsage>> read;
            try
            {
                // Off the UI thread, all the days on one hop: Microsoft.Data.Sqlite's async calls run synchronously.
                read = await Task.Run(async () =>
                {
                    var rows = new Dictionary<DateOnly, IReadOnlyList<AppSiteUsage>>();
                    foreach (DateOnly day in wanted)
                    {
                        (DateTimeOffset start, DateTimeOffset end) = DayRange(day);
                        rows[day] = await _log.GetAppsAndSitesAsync(start, end, deletions);
                    }

                    return rows;
                });
            }
            catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException)
            {
                LoadedFrom = null;
                return;
            }

            if (generation != _generation)
            {
                return; // The history changed under this read; the next refresh starts over.
            }

            foreach ((DateOnly day, IReadOnlyList<AppSiteUsage> rows) in read)
            {
                _days[day] = rows;
            }

            _rereadFor = today;
            LoadedFrom = first;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The day values of <paramref name="goal"/> that <paramref name="device"/> measures, from <paramref name="from"/> to <paramref name="today"/>:
    /// the whole minutes the goal's counted rules match. A day with no logged activity is measured only when it is today or
    /// <paramref name="hasOwnEntry"/> says this device already holds a value for it (a deletion or a changed rule then makes it a real 0);
    /// a day the log has nothing for is not guessed as 0. Empty when no rule is counted here or the days are not held.
    /// </summary>
    public IReadOnlyList<GoalDerivedReading> ReadingsFor(Goal goal, Guid device, DateOnly from, DateOnly today, Func<DateOnly, bool> hasOwnEntry)
    {
        IReadOnlyList<GoalSourceRule> rules = CountedRules(goal, device);
        if (rules.Count == 0 || LoadedFrom is not DateOnly held)
        {
            return [];
        }

        // The entries of every counted rule together, so a row that two of them match still counts once.
        List<GoalAppMatch> apps = rules.SelectMany(rule => rule.Apps ?? []).ToList();
        string entries = string.Join('\n', apps.Select(match => (int)match.Kind + ":" + match.Text).Order(StringComparer.OrdinalIgnoreCase));
        DateOnly closedBefore = today.AddDays(-1);
        var readings = new List<GoalDerivedReading>();
        for (DateOnly day = from < held ? held : from; day <= today; day = day.AddDays(1))
        {
            if (!_days.TryGetValue(day, out IReadOnlyList<AppSiteUsage>? rows))
            {
                continue;
            }

            if (rows.Count > 0 || day == today || hasOwnEntry(day))
            {
                // A closed day's minutes are worked out once per entries and names (its rows no longer change).
                if (day >= closedBefore || !_minutes.TryGetValue((entries, day), out double minutes))
                {
                    minutes = GoalScreenTimeRows.Minutes(apps, rows, _nameFor);
                    if (day < closedBefore)
                    {
                        _minutes[(entries, day)] = minutes;
                    }
                }

                readings.Add(new GoalDerivedReading(goal.Id, GoalEntrySource.ScreenTime, day, minutes));
            }
        }

        return readings;
    }

    /// <summary>
    /// <paramref name="stored"/> with this PC's own screen-time entries for every goal it counts replaced by the fresh readings (in memory
    /// only, never stored by this call): the goals count what this PC logged right now, in a group or not.
    /// </summary>
    public IReadOnlyList<GoalEntry> WithReadings(IReadOnlyList<GoalEntry> stored, IEnumerable<Goal> goals, Guid device, DateOnly today, DateTimeOffset now)
    {
        if (!IsLoaded)
        {
            return stored;
        }

        var fresh = new List<GoalEntry>();
        var replaced = new HashSet<Guid>();
        Guid? marked = device == Guid.Empty ? null : device;
        foreach (Goal goal in goals)
        {
            if (DaysNeededFrom([goal], device, today) is not DateOnly from)
            {
                continue;
            }

            HashSet<DateOnly> ownDays = stored
                .Where(entry => entry is not null && entry.GoalId == goal.Id && entry.Source == GoalEntrySource.ScreenTime && entry.IsLive && GoalDerivedWriter.IsOwn(entry, device))
                .Select(entry => entry.Day)
                .ToHashSet();
            foreach (GoalDerivedReading reading in ReadingsFor(goal, device, from, today, ownDays.Contains))
            {
                Guid id = GoalDerivedIds.For(goal.Id, GoalEntrySource.ScreenTime, device, reading.Day);
                replaced.Add(id);
                fresh.Add(new GoalEntry(id, goal.Id, reading.Day, null, reading.Value, GoalEntrySource.ScreenTime, now, 0, null, marked));
            }
        }

        if (fresh.Count == 0)
        {
            return stored;
        }

        return [.. stored.Where(entry => entry is null || !replaced.Contains(entry.Id)), .. fresh];
    }

    /// <summary>
    /// Reads this PC's last <see cref="GoalScreenTimeSeen.Days"/> days of apps and sites for the editor's pick list (one apps-and-sites query,
    /// the same deletions apply). Called when a screen-time row opens, not when the dialog does. Empty when the history can't be read.
    /// </summary>
    public GoalScreenTimeSeen ReadSeen()
    {
        DateTimeOffset now = _time.GetUtcNow();
        try
        {
            IReadOnlyList<UsageDeletion> deletions = _deletions().ToList();
            IReadOnlyList<AppSiteUsage> usage = Task.Run(() => _log.GetAppsAndSitesAsync(now.AddDays(-GoalScreenTimeSeen.Days), now, deletions)).GetAwaiter().GetResult();
            return new GoalScreenTimeSeen(usage, _nameFor, _isHidden);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException)
        {
            return GoalScreenTimeSeen.Empty;
        }
    }

    // The local day as two instants (the next midnight is the end, so a day with a clock change is its true length).
    private (DateTimeOffset Start, DateTimeOffset End) DayRange(DateOnly day)
    {
        TimeZoneInfo zone = _time.LocalTimeZone;
        return (ToInstant(day.ToDateTime(TimeOnly.MinValue), zone), ToInstant(day.AddDays(1).ToDateTime(TimeOnly.MinValue), zone));
    }

    private static DateTimeOffset ToInstant(DateTime local, TimeZoneInfo zone)
    {
        DateTime unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        TimeSpan offset = zone.IsInvalidTime(unspecified) ? zone.GetUtcOffset(unspecified.AddHours(1)) : zone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset);
    }
}
