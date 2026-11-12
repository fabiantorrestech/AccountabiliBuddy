using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// A user-made group of goals (brief §5.1), e.g. "Work" or "Learning". Carries the shared-item stamps of ADR 48/72 (stable id,
/// position, edit time, tombstone) so syncing it later (ADR 79, Proposed) is a merge, not a migration; nothing syncs it now.
/// Whether it is collapsed is per-device UI state (`goals.ui`), not part of the area.
/// </summary>
/// <param name="Name">The user's private text: sealed at rest, never logged.</param>
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt = null)
{
    [JsonIgnore]
    public bool IsLive => DeletedAt is null;
}
