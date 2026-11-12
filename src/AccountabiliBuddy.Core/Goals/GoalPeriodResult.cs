namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// How a goal did in one period (<see cref="GoalStatus.Period"/>): the "This period" tile ("3 of 7 · Days this week"), streaks and
/// the weekly review. Only the goal's live days count: before it was created and paused days are left out (a partly paused
/// period asks for fewer days, or a smaller amount in proportion).
/// </summary>
/// <param name="Required">Days to hit (a goal that counts days), else the amount the period asks for.</param>
/// <param name="Progress">Days hit so far, else the amount so far (a level to reach: its latest value).</param>
/// <param name="Met">Whether it is met (an at-most period: not over its cap so far).</param>
/// <param name="Skipped">Nothing was asked of it (no live day, or no period amount): neither met nor missed, skipped by streaks.</param>
/// <param name="AliveDays">The period's days the goal was live (created, not paused), past and future.</param>
public sealed record GoalPeriodResult(
    GoalPeriodSpan Span,
    double Required,
    double Progress,
    bool Met,
    bool Skipped,
    int AliveDays);
