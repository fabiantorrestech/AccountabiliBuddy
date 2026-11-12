namespace AccountabiliBuddy.Core.Goals;

/// <summary>What the weekly review decided for one goal.</summary>
public sealed record GoalReviewDecision(Guid GoalId, GoalReviewChoice Choice);
