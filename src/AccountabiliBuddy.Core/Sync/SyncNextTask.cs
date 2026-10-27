using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A task on the next session's list, as the sealed sync files share it between the user's devices (ADR 48, amends ADR 24):
/// the task with its steps and its category's name and colour (the id of a category is local to a device). A deleted task
/// travels as a tombstone with no text. Only the sealed files carry it; the text is never logged.
/// </summary>
/// <param name="DoneAt">When it was ticked; null while it is open.</param>
/// <param name="Category">The category's name; null for none.</param>
/// <param name="CategoryColor">The category's colour, "#RRGGBB".</param>
/// <param name="Subtasks">Its steps in order; null for none.</param>
public sealed record SyncNextTask(
    Guid Id,
    string Title,
    DateTimeOffset? DoneAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Category,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CategoryColor,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SessionSubtask>? Subtasks,
    DateTimeOffset EditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt,
    double Position) : ISharedItem
{
    [JsonIgnore]
    public string TieBreak => string.Join(
        "\u0001",
        Title, DoneAt?.UtcTicks.ToString() ?? string.Empty, Category ?? string.Empty, CategoryColor ?? string.Empty,
        string.Join("\u0002", (Subtasks ?? []).Select(step => $"{step.Id:D}\u0003{step.Title}\u0003{step.DoneAt?.UtcTicks}")));

    public ISharedItem WithStamps(DateTimeOffset editedAt, DateTimeOffset? deletedAt)
    {
        return this with { EditedAt = editedAt, DeletedAt = deletedAt };
    }

    /// <summary>The task as a tombstone: its text is gone, and it is deleted at <paramref name="at"/>.</summary>
    public SyncNextTask Deleted(DateTimeOffset at)
    {
        return this with { Title = string.Empty, DoneAt = null, Category = null, CategoryColor = null, Subtasks = null, EditedAt = at, DeletedAt = at };
    }
}
