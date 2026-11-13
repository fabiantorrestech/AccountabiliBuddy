using System.Text.Json;
using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// One weekly review (brief §5.1, §5.8). A shared item of ADR 48/72 that travels in the sealed sync files (ADR 79, part
/// <c>goalReviews</c>): one record per week (id <see cref="GoalReviewIds.For"/> its week; records of the same week fold into one, each
/// goal's decision from the newest, <see cref="GoalSyncMerge"/>).
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt = null) : ISharedItem
{
    /// <summary>Fields written by a newer app, kept and written back unchanged (ADR 79 forward compatibility); null when there are none.</summary>
    [JsonExtensionData]
    public GoalUnknownFields? Extra { get; init; }

    [JsonIgnore]
    public bool IsLive => DeletedAt is null;

    /// <summary>The item as one text, for the rare tie of two versions with the same stamp (<see cref="SharedListMerge"/>). Never logged or shown.</summary>
    [JsonIgnore]
    public string TieBreak => JsonSerializer.Serialize(this, SyncJson.Options);

    public ISharedItem WithStamps(DateTimeOffset editedAt, DateTimeOffset? deletedAt)
    {
        return this with { EditedAt = editedAt, DeletedAt = deletedAt };
    }
}
