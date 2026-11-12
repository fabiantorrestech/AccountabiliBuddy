using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// One goal's numbers for today, computed once per refresh by <see cref="GoalFactsBuilder"/> from the Core goals maths and read by its
/// list row, its detail pane, the This week card and Today's goals. Nothing is stored: every value comes from the goal's sources.
/// </summary>
/// <param name="Goal">The goal as shown (Deep work through <see cref="GoalsService.DeepWork"/>: its targets are views).</param>
/// <param name="IsPaused">Paused today: it leaves the This week counts and shows only in All.</param>
/// <param name="PausedSince">The day its current pause began, while paused.</param>
/// <param name="TodayState">Today's day state.</param>
/// <param name="PeriodGoal">The goal the This period tile reads (Deep work with a weekly target: that target).</param>
/// <param name="Period">How <paramref name="PeriodGoal"/> does this period.</param>
/// <param name="Status">On track / Behind / Not due yet / Paused for this period (brief §5.5).</param>
/// <param name="Strip">The 14-day strip, oldest first (<see cref="GoalStrip14"/>).</param>
/// <param name="PeriodCells">This period's days as strip cells (a week or two only; empty for a month).</param>
/// <param name="Recent">The last 30 days' states, oldest first (sparkline, 7-day bars).</param>
/// <param name="Target">The Target tile (a long-term amount), or null.</param>
/// <param name="WhatItTakes">The Target tile's pop-up (brief §5.6), or null.</param>
/// <param name="Threshold">A habit's "ticks itself at" minutes, or null.</param>
public sealed record GoalFacts(
    Goal Goal,
    DateOnly Today,
    bool IsPaused,
    DateOnly? PausedSince,
    GoalDayState TodayState,
    GoalPeriodSpan PeriodSpan,
    Goal PeriodGoal,
    GoalPeriodResult Period,
    GoalPeriodStatus Status,
    IReadOnlyList<GoalStripCell> Strip,
    IReadOnlyList<GoalStripCell> PeriodCells,
    IReadOnlyList<GoalDayState> Recent,
    GoalTargetProgress? Target,
    GoalWhatItTakes? WhatItTakes,
    int Streak,
    GoalAge Age,
    double? Threshold)
{
    /// <summary>True when the goal asks for something each day or period (not Deep work's "Track time only").</summary>
    public bool HasPeriodTarget => Goal.Kind == GoalKind.Habit || Goal.Target.Amount is not null || Goal.Target.WeeklyDays is not null;
}
