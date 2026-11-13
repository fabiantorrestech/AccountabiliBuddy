using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>What the weekly review decided for one goal.</summary>
public sealed record GoalReviewDecision(Guid GoalId, GoalReviewChoice Choice)
{
    /// <summary>
    /// When this goal's decision was made (JSON <c>decidedAt</c>, optional; ADR 79): the sync merge keeps each goal's newest decision
    /// by it. A decision without one counts as made at its record's edit time; the merge writes that time in, so a decision keeps its
    /// own time once a record holding it is folded with another's (the review service sets it when the user answers, GS3).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? DecidedAt { get; init; }

    /// <summary>Fields written by a newer app, kept and written back unchanged (ADR 79 forward compatibility); null when there are none.</summary>
    [JsonExtensionData]
    public GoalUnknownFields? Extra { get; init; }
}
