namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// The Target tile (change note §4a): "34h of 50h · Target by Sun 1/31" with an On track / Behind pill and a thin mark at where
/// the user needs to be today (<see cref="GoalStatus.Target"/>).
/// </summary>
/// <param name="Current">The total since the goal began (live days only), or a level goal's latest value.</param>
/// <param name="Target">The long-term amount or level.</param>
/// <param name="ExpectedToday">The pace line at the end of today; null without a date.</param>
/// <param name="Start">A level goal's first value (where its pace line starts); null for a total.</param>
public sealed record GoalTargetProgress(
    double Current,
    double Target,
    double? ExpectedToday,
    double? Start,
    bool Met,
    GoalPeriodStatus Status);
