using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A task in History (ADR 76): finished, archived or deleted, with the task as it was, for <see cref="Kept"/>. Its id is the
/// task's, so a task has at most one entry and "Bring back" revives the same task. Kept 90 days, as long as older apps show a
/// finished task ticked (the UI may list fewer), so their ticked copies always find it. Separate from the session records of
/// ADR 72 (<c>taskHistory</c>), which stay as they are.
/// </summary>
/// <param name="When">When it was finished, archived or deleted.</param>
/// <param name="Task">The task as it was then (bin <see cref="TaskBin.Done"/> for a finished one, with its <see cref="TaskItem.PrevBin"/>).</param>
/// <param name="EditedAt">The entry's newest change; the newest version wins.</param>
/// <param name="DeletedAt">Set on a tombstone: the entry was removed (brought back, done again or aged out).</param>
public sealed record TaskHistoryEntry(
    Guid Id,
    TaskHistoryKind Kind,
    DateTimeOffset When,
    TaskItem Task,
    DateTimeOffset EditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt = null)
{
    /// <summary>How long an entry is kept after <see cref="When"/>.</summary>
    public static readonly TimeSpan Kept = TimeSpan.FromDays(90);

    [JsonIgnore]
    public bool IsLive => DeletedAt is null;

    /// <summary>The entry as a tombstone: the snapshot reduced to the task's own tombstone.</summary>
    public TaskHistoryEntry Deleted(DateTimeOffset at)
    {
        return this with { Task = Task.Deleted(at), EditedAt = at, DeletedAt = at };
    }
}
