using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>The task list after another device's old fields were read in (counts only for the log, never text).</summary>
/// <param name="FieldsApplied">Single fields taken from newer old items.</param>
/// <param name="Added">Tasks (and tombstones) this device didn't know.</param>
/// <param name="Deleted">Tasks made tombstones (or local stubs) by a newer old tombstone.</param>
/// <param name="Revived">Tasks brought back from a tombstone or History by a newer old item.</param>
/// <param name="Moved">Bin changes (a move between old lists, a tick, an untick).</param>
public sealed record OldTaskReadResult(
    IReadOnlyList<SyncTaskItem> Tasks,
    IReadOnlyList<SyncTaskLabel> Labels,
    IReadOnlyList<SyncTaskLogItem> History,
    int FieldsApplied,
    int Added,
    int Deleted,
    int Revived,
    int Moved)
{
    public bool Changed => FieldsApplied + Added + Deleted + Revived + Moved > 0;
}
