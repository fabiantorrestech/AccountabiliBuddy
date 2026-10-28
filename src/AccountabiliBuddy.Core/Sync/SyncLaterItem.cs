using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A task on the Later list (ADR 50), the shared list beside the next session's tasks (ADR 48): things the user keeps for
/// another day, with the steps, category, estimate and note they had and an optional due date. The sealed sync files share
/// it between the user's devices, item by item; a deleted item travels as a tombstone with no text. Only the sealed files
/// carry it; the text is never logged.
/// </summary>
/// <param name="Category">The category's name; null for none.</param>
/// <param name="CategoryColor">The category's colour, "#RRGGBB".</param>
/// <param name="Subtasks">Its steps in order; null for none.</param>
/// <param name="EstimateMinutes">The user's estimate in minutes; null for none.</param>
/// <param name="Note">The user's private note; null for none.</param>
/// <param name="DueOn">The local date it is due; null for none.</param>
/// <param name="PlannedFor">The day part it was put in the plan for; null for none. When that part starts, the task moves to the next session's list.</param>
public sealed record SyncLaterItem(
    Guid Id,
    string Title,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SessionSubtask>? Subtasks,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Category,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CategoryColor,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? EstimateMinutes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? DueOn,
    DateTimeOffset EditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt,
    double Position,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PlannedSlot? PlannedFor = null) : ISharedItem
{
    [JsonIgnore]
    public string TieBreak => string.Join(
        "\u0001",
        Title,
        string.Join("\u0002", (Subtasks ?? []).Select(step => $"{step.Id:D}\u0003{step.Title}\u0003{step.DoneAt?.UtcTicks}")),
        Category ?? string.Empty, CategoryColor ?? string.Empty,
        EstimateMinutes?.ToString() ?? string.Empty, Note ?? string.Empty,
        DueOn?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        PlannedFor?.TieText ?? string.Empty);

    public ISharedItem WithStamps(DateTimeOffset editedAt, DateTimeOffset? deletedAt)
    {
        return this with { EditedAt = editedAt, DeletedAt = deletedAt };
    }

    /// <summary>The item as a tombstone: its text is gone, and it is deleted at <paramref name="at"/>.</summary>
    public SyncLaterItem Deleted(DateTimeOffset at)
    {
        return this with
        {
            Title = string.Empty, Subtasks = null, Category = null, CategoryColor = null, EstimateMinutes = null, Note = null,
            DueOn = null, PlannedFor = null, EditedAt = at, DeletedAt = at,
        };
    }
}
