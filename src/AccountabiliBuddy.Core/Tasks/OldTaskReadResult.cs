using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>The task list after another device's old fields were read in (counts only for the log, never text).</summary>
/// <param name="FieldsApplied">Single fields taken from newer old items.</param>
/// <param name="Added">Tasks (and tombstones) this device didn't know.</param>
/// <param name="Deleted">Tasks made tombstones (or local stubs) by a newer old tombstone.</param>
/// <param name="Revived">Tasks brought back from a tombstone or History by a newer old item.</param>
/// <param name="Moved">Bin changes (a move between old lists, a tick, an untick, a moved task leaving its old id).</param>
/// <param name="Paired">Older apps' moves (delete or tick plus a new id) carried over to the new id with everything the task had.</param>
public sealed record OldTaskReadResult(
    IReadOnlyList<SyncTaskItem> Tasks,
    IReadOnlyList<SyncTaskLabel> Labels,
    IReadOnlyList<SyncTaskLogItem> History,
    int FieldsApplied,
    int Added,
    int Deleted,
    int Revived,
    int Moved,
    int Paired = 0)
{
    public bool Changed => FieldsApplied + Added + Deleted + Revived + Moved + Paired > 0;
}
