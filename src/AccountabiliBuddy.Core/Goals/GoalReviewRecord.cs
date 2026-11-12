using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// One weekly review (brief §5.1, §5.8). Carries the shared-item stamps of ADR 48/72 for a later ADR 79 merge; nothing syncs it now.
/// </summary>
/// <param name="Week">The first day of the week reviewed (on the user's week start).</param>
/// <param name="Decisions">A decision per goal reviewed.</param>
/// <param name="SnoozedUntil">Snoozed for 1 or 2 days; null when not snoozed.</param>
/// <param name="Notes">The user's private text: sealed at rest with the device's key, never logged; in a backup only inside a password file.</param>
public sealed record GoalReviewRecord(
    Guid Id,
    DateOnly Week,
    IReadOnlyList<GoalReviewDecision> Decisions,
    double Position,
    DateTimeOffset EditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? SnoozedUntil = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Notes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt = null)
{
    [JsonIgnore]
    public bool IsLive => DeletedAt is null;
}
