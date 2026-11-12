namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The 14-day strip on the goal list (change note §4): the last 13 days and today, oldest first, from the same day states as
/// the streak, so a 12-day streak on a daily goal is 12 solid cells before today (paused cells skipped).
/// </summary>
public static class GoalStrip14
{
    public const int Length = 14;

    public static IReadOnlyList<GoalStripCell> For(Goal goal, GoalSourceData data, DateOnly today)
    {
        return GoalCounting.Days(goal, data, today.AddDays(-(Length - 1)), today, today)
            .Select(state => Cell(goal, state, today))
            .ToList();
    }

    private static GoalStripCell Cell(Goal goal, GoalDayState state, DateOnly today)
    {
        bool isToday = state.Day == today;
        GoalStripCellKind kind = state.Status switch
        {
            GoalDayStatus.Counted or GoalDayStatus.CountedTickedItself when state.Hit => GoalStripCellKind.Hit,
            GoalDayStatus.Paused => GoalStripCellKind.Paused,

            // A day that added to a weekly amount without being a target of its own.
            GoalDayStatus.Counted or GoalDayStatus.CountedTickedItself when !isToday => GoalStripCellKind.Partial,
            GoalDayStatus.NotCounted when !isToday => GoalStripCellKind.Partial,
            GoalDayStatus.Missed => GoalStripCellKind.Missed,
            _ when isToday && state.Status != GoalDayStatus.NotDue => GoalStripCellKind.Today,
            _ => GoalStripCellKind.NotDue,
        };
        double progress = kind == GoalStripCellKind.Today ? TodayProgress(goal, state) : 0;
        return new GoalStripCell(state.Day, kind, progress, isToday);
    }

    private static double TodayProgress(Goal goal, GoalDayState state)
    {
        double? target = goal.Kind == GoalKind.Habit
            ? goal.Sources.Where(rule => rule.Automatic && rule.CountsTime && rule.ThresholdMinutes is > 0).Select(rule => rule.ThresholdMinutes).Min()
            : goal.Target.Direction == GoalDirection.AtLeast ? goal.Target.Amount : null;
        double done = goal.Kind == GoalKind.Habit ? state.Minutes : state.Value;
        return target is double amount && amount > 0 ? Math.Clamp(done / amount, 0, 1) : 0;
    }
}
