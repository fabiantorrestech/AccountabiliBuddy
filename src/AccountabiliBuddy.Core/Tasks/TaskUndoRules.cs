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
    /// <param name="after">The lists the action made; with it the entry remembers which fields the action changed.</param>
    public static TaskUndoEntry Capture(TaskLists before, IEnumerable<Guid> ids, TaskActionKind kind, string text, TaskLists? after = null)
    {
        var changedFields = new HashSet<TaskField>();
        var tasks = new Dictionary<Guid, SyncTaskItem?>();
        var log = new Dictionary<Guid, SyncTaskLogItem?>();
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
        }

        return new TaskUndoEntry(kind, text, tasks, log, after is null ? null : changedFields);
    }

    /// <summary>
    /// The entry made safe to restore (ADR 76): a task whose snapshot sat in This session goes back to Queued, unfocused, because a place in
    /// a session is the cap's to give (the returned ids are those tasks, for the caller to offer to the session's own add); a task that is
    /// in the session's checklist now (<paramref name="inChecklist"/>) is left out, so the checklist is never edited behind the session's back.
    /// </summary>
    public static (TaskUndoEntry Entry, IReadOnlyList<Guid> SessionIds) ForRestore(TaskUndoEntry entry, Func<Guid, bool> inChecklist)
    {
        var tasks = new Dictionary<Guid, SyncTaskItem?>();
        var sessionIds = new List<Guid>();
        foreach ((Guid id, SyncTaskItem? snapshot) in entry.Tasks)
        {
            if (inChecklist(id))
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

        var log = entry.Log.Where(pair => !inChecklist(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        return (entry with { Tasks = tasks, Log = log }, sessionIds);
    }

    /// <param name="onlyFields">Restore just these fields of a task that is still live (default: the entry's own, else every field).
    /// A task that is gone comes back whole.</param>
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
