using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// Builds <see cref="GoalFacts"/> from the Core goals maths only (<see cref="GoalCounting"/>, <see cref="GoalStatus"/>,
/// <see cref="GoalStrip14"/>, <see cref="GoalStreak"/>, <see cref="GoalAge"/>, <see cref="GoalWhatItTakes"/>), so the PC's screens
/// show exactly what the rules say (and what the phone's mirror computes from the same data).
/// </summary>
public static class GoalFactsBuilder
{
    /// <summary>Days of history the row visuals read (sparkline, 7-day bars).</summary>
    public const int RecentDays = 30;

    public static GoalFacts Build(Goal goal, GoalSourceData data, DateOnly today, GoalWeekStart weekStart)
    {
        Goal periodGoal = goal.IsDeepWork && goal.SecondTarget is { Amount: not null } weekly ? goal with { Target = weekly } : goal;
        GoalPeriodSpan span = GoalStatus.ThisPeriodSpan(periodGoal, today, weekStart, data.Zone);
        GoalPeriodResult period = GoalStatus.Period(periodGoal, data, span, today);
        IReadOnlyList<GoalDayState> recent = GoalCounting.Days(goal, data, today.AddDays(-(RecentDays - 1)), today, today);
        IReadOnlyList<GoalStripCell> periodCells = span.Length <= 14
            ? GoalCounting.Days(goal, data, span.Start, span.End, today).Select(state => GoalStrip14.CellOf(goal, state, today)).ToList()
            : [];
        DateOnly? pausedSince = (goal.Pauses ?? []).Where(stretch => stretch.ResumedOn is null && stretch.From <= today).Select(stretch => (DateOnly?)stretch.From).Max();
        return new GoalFacts(
            goal,
            today,
            goal.IsPausedOn(today),
            pausedSince,
            recent[^1],
            span,
            periodGoal,
            period,
            GoalStatus.ThisPeriod(goal, data, today, weekStart),
            GoalStrip14.For(goal, data, today),
            periodCells,
            recent,
            GoalStatus.Target(goal, data, today),
            GoalWhatItTakes.For(goal, data, today),
            GoalStreak.Count(goal, data, today, weekStart),
            GoalAge.For(goal, today, data.Zone),
            GoalTexts.Threshold(goal));
    }
}
