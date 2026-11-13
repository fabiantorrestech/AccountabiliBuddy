using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The minutes a PC writes as its derived #tag and calendar values for a goal and day (ADR 79, owner 2026-11-14 decision 1; orchestrator
/// review item 2), and that <see cref="GoalCounting"/> reads back. Whole minutes, like every time source (<see cref="GoalCounting"/>).
/// <list type="bullet">
/// <item><b>#tags</b> (<see cref="GoalEntrySource.FocusLocal"/>): the EXTRA minutes this PC's local-only #tag attribution adds for its OWN
/// sessions beyond the label attribution every device makes from the synced sessions. A receiver counts every device's sessions by labels
/// and adds the other devices' extras, so a minute counts once per goal and PCs see each other's #tag focus.</item>
/// <item><b>Calendar</b> (<see cref="GoalEntrySource.Calendar"/>): the minutes this PC's calendar windows add beyond every focus window of the
/// goal it knows (all devices' sessions). Receivers take the highest across PCs (the same calendar is often on several). Never in focus
/// totals, Stats focus or a guard (ADR 58).</item>
/// </list>
/// </summary>
public static class GoalDerivedMinutes
{
    /// <summary>
    /// The #tag extra of <paramref name="goal"/> on <paramref name="day"/> from this PC's own sessions (<see cref="GoalFocusAttribution.FromRecords"/>):
    /// their focus counted with #tags minus the same sessions counted without. 0 when the goal has no switched-on #tag rule, and for Deep work.
    /// </summary>
    public static double FocusLocalExtra(Goal goal, IReadOnlyList<GoalFocusSession> ownSessions, DateOnly day)
    {
        IReadOnlyList<GoalSourceRule> rules = GoalSources.CountedFor(goal);
        if (goal.IsDeepWork || !rules.Any(rule => rule.Automatic && rule.Type == GoalSourceType.FocusTag))
        {
            return 0;
        }

        LocalWindow dayWindow = DayWindow(day);
        double local = GoalCounting.WholeMinutes(Clip(GoalFocusAttribution.FocusWindows(ownSessions, rules, true), dayWindow));
        double shape = GoalCounting.WholeMinutes(Clip(GoalFocusAttribution.FocusWindows(ownSessions, rules, false), dayWindow));
        return Math.Max(0, local - shape);
    }

    /// <summary>
    /// The calendar extra of <paramref name="goal"/> on <paramref name="day"/>: the goal's calendar windows on this PC
    /// (<see cref="GoalSourceData.CalendarWindows"/>) beyond every focus window of the goal it knows (<see cref="GoalSourceData.Focus"/>, with
    /// #tags where they are counted). 0 without a switched-on calendar rule or calendar windows, and for Deep work.
    /// </summary>
    public static double CalendarExtra(Goal goal, GoalSourceData data, DateOnly day)
    {
        IReadOnlyList<GoalSourceRule> rules = GoalSources.CountedFor(goal);
        if (goal.IsDeepWork)
        {
            return 0;
        }

        LocalWindow dayWindow = DayWindow(day);
        List<LocalWindow> calendar = rules
            .Where(rule => rule.Automatic && rule.Type == GoalSourceType.CalendarCategory)
            .SelectMany(rule => CalendarWindows(rule, data))
            .ToList();
        if (calendar.Count == 0)
        {
            return 0;
        }

        IReadOnlyList<LocalWindow> focus = GoalFocusAttribution.FocusWindows(data.Focus, rules.Where(rule => rule.Automatic).ToList(), data.TagsAvailable);
        return Extra(Clip(focus, dayWindow), Clip(calendar, dayWindow));
    }

    /// <summary>What <paramref name="more"/> adds beyond <paramref name="basis"/>, whole minutes (each union rounded down, as everywhere).</summary>
    internal static double Extra(IReadOnlyList<LocalWindow> basis, IReadOnlyList<LocalWindow> more)
    {
        return Math.Max(0, GoalCounting.WholeMinutes(basis.Concat(more)) - GoalCounting.WholeMinutes(basis));
    }

    internal static IReadOnlyList<LocalWindow> CalendarWindows(GoalSourceRule rule, GoalSourceData data)
    {
        return rule.CalendarCategoryId is Guid category && data.CalendarWindows is { } calendar && calendar.TryGetValue(category, out IReadOnlyList<LocalWindow>? windows)
            ? DayTimeline.Union(windows)
            : [];
    }

    private static LocalWindow DayWindow(DateOnly day)
    {
        DateTime start = day.ToDateTime(TimeOnly.MinValue);
        return new LocalWindow(start, start.AddDays(1));
    }

    private static List<LocalWindow> Clip(IEnumerable<LocalWindow> windows, LocalWindow dayWindow)
    {
        return windows.Select(window => DayTimeline.Clip(window, dayWindow)).OfType<LocalWindow>().ToList();
    }
}
