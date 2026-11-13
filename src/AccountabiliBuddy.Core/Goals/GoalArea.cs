using System.Text.Json;
using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// A user-made group of goals (brief §5.1), e.g. "Work" or "Learning". A shared item of ADR 48/72 (stable id, position, edit time,
/// tombstone) that travels in the sealed sync files (ADR 79, part <c>goalAreas</c>), newest version wins (<see cref="GoalSyncMerge"/>).
/// Whether it is collapsed is per-device UI state (`goals.ui`), not part of the area.
/// </summary>
/// <param name="Name">The user's private text: sealed at rest with the device's key, never logged; a name: in a backup it travels readable like a task title.</param>
/// <param name="Icon">A Material icon name (ADR 32).</param>
/// <param name="Color">"#RRGGBB".</param>
/// <param name="Position">Its place among the areas: lower first; fractions put it between two others.</param>
/// <param name="EditedAt">Its newest change: the item stamp (newest edit wins).</param>
/// <param name="DeletedAt">Set on a tombstone (kept 30 days, no text).</param>
public sealed record GoalArea(
    Guid Id,
    string Name,
    string Icon,
    string Color,
    double Position,
    DateTimeOffset EditedAt,
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
