using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Undo for task actions (ADR 76), pure: <see cref="Capture"/> keeps the tasks and History entries an action is about to touch,
/// <see cref="Restore"/> puts them back exactly (ids, numbers, bins, positions, steps, every field), stamped now so every device
/// takes the restored state over what the action did. Only the touched ids go back: what other tasks changed since stays.
/// </summary>
public static class TaskUndoRules
{
    /// <param name="after">The lists the action made; with it the entry remembers which fields the action changed, which tasks it took
    /// out of the lists and the History it left.</param>
    /// <param name="at">The action's time: with it a Shelf task the action finished counts as gone to History, as the store settles it.</param>
    public static TaskUndoEntry Capture(TaskLists before, IEnumerable<Guid> ids, TaskActionKind kind, string text, TaskLists? after = null, DateTimeOffset? at = null)
    {
        var changedFields = new HashSet<TaskField>();
        var removed = new HashSet<Guid>();
        var logAfter = new Dictionary<Guid, DateTimeOffset?>();
        var tasks = new Dictionary<Guid, SyncTaskItem?>();
        var log = new Dictionary<Guid, SyncTaskLogItem?>();
        TaskLists? settled = after is not null && at is DateTimeOffset time ? TaskListRules.Settled(after, time) : after;
        foreach (Guid id in ids.Distinct())
        {
            tasks[id] = before.Tasks.FirstOrDefault(item => item.Id == id);
            log[id] = before.Log.FirstOrDefault(item => item.Id == id);
            SyncTaskItem? now = after?.Tasks.FirstOrDefault(item => item.Id == id);
            if (tasks[id] is { Task.IsLive: true } was && now is { Task.IsLive: true })
            {
                foreach (TaskField field in TaskFieldValues.All)
                {
                    if (!string.Equals(TaskFieldValues.Canonical(was.Task, field), TaskFieldValues.Canonical(now.Task, field), StringComparison.Ordinal))
                    {
                        changedFields.Add(field);
                    }
                }
            }

            if (settled is null)
            {
                continue;
            }

            if (tasks[id] is { Task.IsLive: true } && settled.Tasks.FirstOrDefault(item => item.Id == id) is not { Task.IsLive: true })
            {
                removed.Add(id);
            }

            logAfter[id] = settled.Log.FirstOrDefault(item => item.Id == id && item.Entry.IsLive)?.EditedAt;
        }

        return settled is null
            ? new TaskUndoEntry(kind, text, tasks, log)
            : new TaskUndoEntry(kind, text, tasks, log, changedFields, removed, logAfter);
    }

    /// <summary>
    /// The entry made safe to restore against the lists as they are now (<paramref name="lists"/>; ADR 76). A touched task is left out,
    /// in <c>Skipped</c> for the "Can't undo · it changed since" toast, when the session holds it now (<paramref name="heldBySession"/>:
    /// in its checklist or among the store's held copies, so the checklist is never edited behind the session's back), when a live
    /// History entry newer than the action's own has it, or when it is gone now and this action wasn't what took it out (only a delete,
    /// archive, finish or move out of the lists comes back whole). A task whose snapshot sat in This session goes back to Queued,
    /// unfocused, because a place in a session is the cap's to give (<c>SessionIds</c>, for the caller to offer to the session's own add).
    /// </summary>
    public static (TaskUndoEntry Entry, IReadOnlyList<Guid> SessionIds, IReadOnlyList<Guid> Skipped) ForRestore(
        TaskUndoEntry entry, TaskLists lists, Func<Guid, bool> heldBySession)
    {
        var tasks = new Dictionary<Guid, SyncTaskItem?>();
        var sessionIds = new List<Guid>();
        var skipped = new List<Guid>();
        foreach (Guid id in entry.Tasks.Keys.Union(entry.Log.Keys))
        {
            if (heldBySession(id) || HasNewerHistory(entry, lists, id) || IsGoneSince(entry, lists, id))
            {
                skipped.Add(id);
            }
        }

        foreach ((Guid id, SyncTaskItem? snapshot) in entry.Tasks)
        {
            if (skipped.Contains(id))
            {
                continue;
            }

            if (snapshot is { Task: { IsLive: true, Bin: TaskBin.Session } task })
            {
                sessionIds.Add(id);
                List<TaskStep>? steps = task.Steps?.Select(step => step with { Focus = false }).ToList();
                tasks[id] = snapshot with { Task = task with { Bin = TaskBin.Queued, Steps = steps } };
            }
            else
            {
                tasks[id] = snapshot;
            }
        }

        var log = entry.Log.Where(pair => !skipped.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        return (entry with { Tasks = tasks, Log = log }, sessionIds, skipped);
    }

    // A live History entry for the task that came after the action (another device finished it, a session settled it): the action's
    // undo would delete or overwrite it.
    private static bool HasNewerHistory(TaskUndoEntry entry, TaskLists lists, Guid id)
    {
        if (entry.LogAfter is null || lists.Log.FirstOrDefault(item => item.Id == id && item.Entry.IsLive) is not { } current)
        {
            return false;
        }

        return !entry.LogAfter.TryGetValue(id, out DateTimeOffset? after) || after is not DateTimeOffset stamp || current.EditedAt > stamp;
    }

    // The task was live before the action and is gone now, but the action didn't take it out (it joined a session and finished,
    // another device deleted it): reviving it whole would make a duplicate of what happened since.
    private static bool IsGoneSince(TaskUndoEntry entry, TaskLists lists, Guid id)
    {
        if (!entry.Tasks.TryGetValue(id, out SyncTaskItem? snapshot) || snapshot is not { Task.IsLive: true }
            || lists.Tasks.Any(item => item.Id == id && item.Task.IsLive))
        {
            return false;
        }

        bool removedByAction = entry.Removed?.Contains(id) ?? entry.Kind is TaskActionKind.Delete or TaskActionKind.Finish;
        return !removedByAction;
    }

    /// <param name="onlyFields">Restore just these fields of a task that is still live (default: the entry's own, else every field).
    /// A task that is gone comes back whole: pass the entry through <see cref="ForRestore"/> first, which leaves out what changed since.</param>
    public static TaskLists Restore(TaskLists lists, TaskUndoEntry entry, Guid self, DateTimeOffset now, IReadOnlyCollection<TaskField>? onlyFields = null)
    {
        IReadOnlyCollection<TaskField> fields = onlyFields ?? (IReadOnlyCollection<TaskField>?)entry.Fields ?? TaskFieldValues.All;
        List<SyncTaskItem> tasks = lists.Tasks.ToList();
        foreach ((Guid id, SyncTaskItem? snapshot) in entry.Tasks)
        {
            int index = tasks.FindIndex(item => item.Id == id);
            SyncTaskItem? current = index >= 0 ? tasks[index] : null;
            SyncTaskItem? restored = snapshot is { Task.IsLive: true } ? Reinstated(current, snapshot, now, self, fields) : Gone(current, now);
            if (restored is null)
            {
                continue;
            }

            if (index >= 0)
            {
                tasks[index] = restored;
            }
            else
            {
                tasks.Add(restored);
            }
        }

        List<SyncTaskLogItem> log = lists.Log.ToList();
        foreach ((Guid id, SyncTaskLogItem? snapshot) in entry.Log)
        {
            int index = log.FindIndex(item => item.Id == id);
            SyncTaskLogItem? current = index >= 0 ? log[index] : null;
            DateTimeOffset at = current is null ? now : SharedListMerge.NextStamp(now, current);
            SyncTaskLogItem? restored = snapshot is { Entry.IsLive: true }
                ? new SyncTaskLogItem(snapshot.Entry with { EditedAt = at, DeletedAt = null })
                : current is { Entry.IsLive: true } ? new SyncTaskLogItem(current.Entry.Deleted(at)) : null;
            if (restored is null)
            {
                continue;
            }

            if (index >= 0)
            {
                log[index] = restored;
            }
            else
            {
                log.Add(restored);
            }
        }

        return lists with { Tasks = tasks, Log = log };
    }

    // The action made the task (or it was not there): undoing deletes it.
    private static SyncTaskItem? Gone(SyncTaskItem? current, DateTimeOffset now)
    {
        return current is { Task.IsLive: true } ? current.Deleted(SharedListMerge.NextStamp(now, current)) : null;
    }

    private static SyncTaskItem Reinstated(SyncTaskItem? current, SyncTaskItem snapshot, DateTimeOffset now, Guid self, IReadOnlyCollection<TaskField> fields)
    {
        if (current is null || !current.Task.IsLive)
        {
            // Gone (deleted, or a move's departure): it comes back whole, newer than the tombstone, so every device takes it back.
            DateTimeOffset at = current is null ? now : SharedListMerge.NextStamp(now, current);
            return new SyncTaskItem(snapshot.Task with { EditedAt = at, DeletedAt = null }, TaskFieldStamps.AllAt(new TaskFieldStamp(at, self)));
        }

        SyncTaskItem item = current;
        foreach (TaskField field in fields)
        {
            item = TaskEdits.Edit(item, field, snapshot.Task, now, self);
        }

        // The number's history and the origin line go back only with a full restore.
        return fields.Contains(TaskField.Number)
            ? item with { Task = item.Task with { FormerNumbers = snapshot.Task.FormerNumbers, Origin = snapshot.Task.Origin } }
            : item;
    }
}
