namespace AccountabiliBuddy.Core.Goals;

/// <summary>A target change's fate (<see cref="GoalLowering.Decide"/>).</summary>
/// <param name="AppliesAt">For <see cref="GoalChangeOutcome.Wait"/>: the earliest it may apply (the pending change's time); null otherwise.</param>
public sealed record GoalChangeDecision(GoalChangeOutcome Outcome, DateTimeOffset? AppliesAt = null)
{
    /// <summary>The pending change to keep for a wait; null for any other outcome.</summary>
    public GoalPendingChange? Pending(double? amount)
    {
        return Outcome == GoalChangeOutcome.Wait && AppliesAt is DateTimeOffset at ? new GoalPendingChange(amount, at) : null;
    }
}
