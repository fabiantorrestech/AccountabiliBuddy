using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// What one run of the session-note conversion (ADR 94, <see cref="SessionNoteTasks.Plan"/>) does: the To sort tasks to add and the notes
/// to delete (an ordinary synced tombstone) once those tasks are saved. A note whose task already exists anywhere is only deleted.
/// </summary>
/// <param name="Add">The new tasks, each stamped with its note's own edit time.</param>
/// <param name="Tombstone">The ids of the notes to delete after <paramref name="Add"/> is saved (every live note the plan looked at).</param>
public sealed record SessionNoteTaskPlan(IReadOnlyList<SyncTaskItem> Add, IReadOnlyList<Guid> Tombstone)
{
    public static SessionNoteTaskPlan Empty { get; } = new([], []);

    public bool IsEmpty => Add.Count == 0 && Tombstone.Count == 0;
}
