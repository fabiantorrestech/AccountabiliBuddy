using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// One undoable task action (ADR 76): the tasks and History entries it touched as they were before (null: it did not exist, so
/// undoing deletes it), with the toast text. Holds the user's private text: kept in memory only, never logged.
/// </summary>
/// <param name="Fields">The fields the action changed on the tasks that stayed live; undo puts back only those, so another device's
/// edits to other fields stay. Null: every field.</param>
/// <param name="Text">The action's toast text ("Finished “Draft Q4…”").</param>
public sealed record TaskUndoEntry(
    TaskActionKind Kind,
    string Text,
    IReadOnlyDictionary<Guid, SyncTaskItem?> Tasks,
    IReadOnlyDictionary<Guid, SyncTaskLogItem?> Log,
    IReadOnlySet<TaskField>? Fields = null);
