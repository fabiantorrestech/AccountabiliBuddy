using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// One weekly review (brief §5.1, §5.8). Carries the shared-item stamps of ADR 48/72 for a later ADR 79 merge; nothing syncs it now.
/// </summary>
/// <param name="Week">The first day of the week reviewed (on the user's week start).</param>
/// <param name="Decisions">A decision per goal reviewed. A decision of a newer app (a choice this version doesn't know) is kept as written beside them (<see cref="GoalNewerJson.NewerOf{T}"/>), never applied (ADR 79).</param>
/// <param name="SnoozedUntil">Snoozed for 1 or 2 days; null when not snoozed.</param>
/// <param name="Notes">The user's private text: sealed at rest with the device's key, never logged; in a backup only inside a password file.</param>
public sealed record GoalReviewRecord(
    Guid Id,
    DateOnly Week,
    [property: JsonConverter(typeof(GoalNewerListConverter<GoalReviewDecision>))] IReadOnlyList<GoalReviewDecision> Decisions,
    double Position,
    DateTimeOffset EditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? SnoozedUntil = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Notes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt = null)
{
    /// <summary>Fields written by a newer app, kept and written back unchanged (ADR 79 forward compatibility); null when there are none.</summary>
    [JsonExtensionData]
    public GoalUnknownFields? Extra { get; init; }

    [JsonIgnore]
    public bool IsLive => DeletedAt is null;
}
