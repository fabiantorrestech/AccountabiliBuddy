using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>What the weekly review decided for one goal.</summary>
public sealed record GoalReviewDecision(Guid GoalId, GoalReviewChoice Choice)
{
    /// <summary>Fields written by a newer app, kept and written back unchanged (ADR 79 forward compatibility); null when there are none.</summary>
    [JsonExtensionData]
    public GoalUnknownFields? Extra { get; init; }
}
