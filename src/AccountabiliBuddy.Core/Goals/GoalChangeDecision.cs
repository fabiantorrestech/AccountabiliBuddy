namespace AccountabiliBuddy.Core.Goals;

/// <summary>A target change's fate (<see cref="GoalLowering.Decide"/>).</summary>
/// <param name="AppliesAt">For <see cref="GoalChangeOutcome.Wait"/>: the earliest it may apply (the pending change's time); null otherwise.</param>
/// <param name="MadeInProductaBuddy">A wait decided in ProductaBuddy mode (held only by the work-hours rule, no delay).</param>
public sealed record GoalChangeDecision(GoalChangeOutcome Outcome, DateTimeOffset? AppliesAt = null, bool MadeInProductaBuddy = false)
{
    /// <summary>The pending change to keep for a wait; null for any other outcome.</summary>
    public GoalPendingChange? Pending(double? amount)
    {
        return Outcome == GoalChangeOutcome.Wait && AppliesAt is DateTimeOffset at ? new GoalPendingChange(amount, at, MadeInProductaBuddy) : null;
    }
}
