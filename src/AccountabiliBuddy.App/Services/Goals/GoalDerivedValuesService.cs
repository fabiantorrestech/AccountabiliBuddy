using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// The derived day values this PC writes for the other devices (ADR 79, GS3b), through Core's <see cref="GoalDerivedWriter"/>: one entry per
/// goal, source, this PC's id and day, written and removed only by this PC, updated in place only when the value changed (so a run that
/// finds nothing new writes nothing). Two sources are measured here:
/// <list type="bullet">
/// <item><b>#tag extras</b> (<see cref="GoalEntrySource.FocusLocal"/>): the minutes this PC's local-only #tag attribution adds for its OWN
/// sessions beyond the synced-shape (label) attribution every device makes (<see cref="GoalDerivedMinutes.FocusLocalExtra"/>).</item>
/// <item><b>Calendar</b> (<see cref="GoalEntrySource.Calendar"/>): the minutes this PC's calendar windows add beyond every focus window the
/// PC knows (<see cref="GoalDerivedMinutes.CalendarExtra"/>). Goal counts only: no focus total, Stats focus, goal unlock or guard reads an
/// entry (ADR 58). Not written while this PC has no calendar window at all (the calendar may not be loaded yet; no zeros are written over
/// earlier values on that guess).</item>
/// </list>
/// Beeminder's entries are written by the Beeminder reader through the same device marking (the PC's own id).
/// Screen time is not here (ST2). Nothing is written while this PC is not in a group (no id to mark them with) or the goals and this PC's
/// sessions are not loaded. The last 90 days; older own entries stay as they are. Logs nothing. Use from the UI thread only.
/// </summary>
public sealed class GoalDerivedValuesService
{
    /// <summary>How many days back (today included) a run measures.</summary>
    public const int Days = 90;

    /// <summary>A run asked for by <see cref="Request"/> happens at most this often.</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromMinutes(5);

    private readonly GoalStore _store;
    private readonly GoalSourceDataService _data;
    private readonly TimeProvider _time;
    private bool _running;
    private DateTimeOffset _lastRun = DateTimeOffset.MinValue;

    public GoalDerivedValuesService(GoalStore store, GoalSourceDataService data, TimeProvider time)
    {
        _store = store;
        _data = data;
        _time = time;
    }

    /// <summary>A run when the last one was long enough ago (the caller asks on every recompute; a timer asks too, so nothing waits long).</summary>
    public void Request()
    {
        if (!_running && _time.GetUtcNow() - _lastRun >= MinGap)
        {
            _ = RunAsync();
        }
    }

    /// <returns>How many entries were put (new, changed, brought back or removed); 0 when nothing changed or nothing could be measured.</returns>
    public async Task<int> RunAsync()
    {
        Guid me = _store.SelfId();
        if (_running || me == Guid.Empty || !_store.IsReady || !_data.IsLoaded)
        {
            return 0;
        }

        _running = true;
        try
        {
            _lastRun = _time.GetUtcNow();
            (List<GoalDerivedReading> readings, List<GoalDerivedCover> covers) = Measure(me);
            if (covers.Count == 0)
            {
                return 0;
            }

            int count = 0;
            await _store.ChangeAsync(this, (lists, at) =>
            {
                IReadOnlyList<GoalEntry> changes = GoalDerivedWriter.Changes(lists.Entries, me, readings, covers, at);
                count = changes.Count;
                return count == 0 ? lists : lists with { Entries = GoalDerivedWriter.Applied(lists.Entries, changes) };
            });
            return count;
        }
        finally
        {
            _running = false;
        }
    }

    private (List<GoalDerivedReading> Readings, List<GoalDerivedCover> Covers) Measure(Guid me)
    {
        GoalSourceData data = _data.Current;
        DateOnly today = _data.Today;
        DateOnly from = today.AddDays(-(Days - 1));
        bool calendarKnown = data.CalendarWindows is { Count: > 0 };
        var readings = new List<GoalDerivedReading>();
        var covers = new List<GoalDerivedCover>();
        IReadOnlyList<GoalEntry> entries = _store.Lists.Entries;

        foreach (Goal goal in _store.Lists.LiveGoals.Where(goal => !goal.IsDeepWork))
        {
            IReadOnlyList<GoalSourceRule> rules = GoalSources.CountedFor(goal);
            HashSet<DateOnly> ownTagDays = OwnDays(entries, goal, me, GoalEntrySource.FocusLocal, from, today);
            if (rules.Any(rule => rule.Automatic && rule.Type == GoalSourceType.FocusTag) || ownTagDays.Count > 0)
            {
                // Days with own focus (a day without any measures 0, which is written only over an earlier value, so it is a day with an own entry).
                HashSet<DateOnly> days = [.. ownTagDays, .. DaysOf(_data.OwnFocus.SelectMany(session => session.Windows), from, today)];
                AddReadings(readings, covers, goal, GoalEntrySource.FocusLocal, from, today, days, day => GoalDerivedMinutes.FocusLocalExtra(goal, _data.OwnFocus, day));
            }

            HashSet<DateOnly> ownCalendarDays = OwnDays(entries, goal, me, GoalEntrySource.Calendar, from, today);
            if (calendarKnown && (rules.Any(rule => rule.Automatic && rule.Type == GoalSourceType.CalendarCategory) || ownCalendarDays.Count > 0))
            {
                IEnumerable<LocalWindow> windows = rules
                    .Where(rule => rule.Automatic && rule.Type == GoalSourceType.CalendarCategory)
                    .SelectMany(rule => data.CalendarWindows!.TryGetValue(rule.CalendarCategoryId ?? Guid.Empty, out IReadOnlyList<LocalWindow>? found) ? found : []);
                HashSet<DateOnly> days = [.. ownCalendarDays, .. DaysOf(windows, from, today)];
                AddReadings(readings, covers, goal, GoalEntrySource.Calendar, from, today, days, day => GoalDerivedMinutes.CalendarExtra(goal, data, day));
            }
        }

        return (readings, covers);
    }

    private static void AddReadings(
        List<GoalDerivedReading> readings, List<GoalDerivedCover> covers, Goal goal, GoalEntrySource source, DateOnly from, DateOnly to,
        HashSet<DateOnly> days, Func<DateOnly, double> measure)
    {
        covers.Add(new GoalDerivedCover(goal.Id, source, from, to));
        foreach (DateOnly day in days.OrderBy(day => day))
        {
            readings.Add(new GoalDerivedReading(goal.Id, source, day, measure(day)));
        }
    }

    // The days (inside the run's range) this PC already has a live entry for.
    private static HashSet<DateOnly> OwnDays(IReadOnlyList<GoalEntry> entries, Goal goal, Guid me, GoalEntrySource source, DateOnly from, DateOnly to)
    {
        return entries
            .Where(entry => entry is not null && entry.GoalId == goal.Id && entry.Source == source && entry.IsLive && GoalDerivedWriter.IsOwn(entry, me)
                && entry.Day >= from && entry.Day <= to)
            .Select(entry => entry.Day)
            .ToHashSet();
    }

    // The days inside the range any window touches.
    private static IEnumerable<DateOnly> DaysOf(IEnumerable<LocalWindow> windows, DateOnly from, DateOnly to)
    {
        var days = new HashSet<DateOnly>();
        foreach (LocalWindow window in windows)
        {
            DateOnly first = DateOnly.FromDateTime(window.Start);
            DateOnly last = DateOnly.FromDateTime(window.End > window.Start ? window.End.AddTicks(-1) : window.Start);
            for (DateOnly day = first < from ? from : first; day <= last && day <= to; day = day.AddDays(1))
            {
                days.Add(day);
            }
        }

        return days;
    }
}
