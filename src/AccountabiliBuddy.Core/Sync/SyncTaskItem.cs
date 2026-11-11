using System.Text.Json;
using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A task of the one shared task list (ADR 76) as the sealed sync files carry it: the task and its per-field stamps. Merged by
/// <see cref="TaskFieldMerge"/> (field by field), which leaves tombstones and positions to <see cref="SharedListMerge"/>. A
/// deleted task travels as a tombstone with no text; a task kept on one device (<see cref="TaskItem.Devices"/> naming one) is
/// written by every other device as a stub, as ADR 72's local items. Only the sealed files carry it; the text is never logged.
/// </summary>
public sealed record SyncTaskItem(TaskItem Task, TaskFieldStamps Stamps) : ISharedItem
{
    [JsonIgnore]
    public Guid Id => Task.Id;

    [JsonIgnore]
    public DateTimeOffset EditedAt => Task.EditedAt;

    [JsonIgnore]
    public DateTimeOffset? DeletedAt => Task.DeletedAt;

    [JsonIgnore]
    public double Position => Task.Position;

    [JsonIgnore]
    public Guid? LocalTo => Task.LocalTo;

    [JsonIgnore]
    public string TieBreak => JsonSerializer.Serialize(this, SyncJson.Options);

    public ISharedItem WithStamps(DateTimeOffset editedAt, DateTimeOffset? deletedAt)
    {
        return this with { Task = Task with { EditedAt = editedAt, DeletedAt = deletedAt } };
    }

    public ISharedItem AsLocalStub()
    {
        return Deleted(Task.EditedAt);
    }

    /// <summary>The task as a tombstone deleted at <paramref name="at"/> (it keeps its bin and devices, no text, no stamps).</summary>
    public SyncTaskItem Deleted(DateTimeOffset at)
    {
        return new SyncTaskItem(Task.Deleted(at), TaskFieldStamps.None);
    }
}
