using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Undo for task actions (ADR 76), pure: <see cref="Capture"/> keeps the tasks and History entries an action is about to touch,
/// <see cref="Restore"/> puts them back exactly (ids, numbers, bins, positions, steps, every field), stamped now so every device
/// takes the restored state over what the action did. Only the touched ids go back: what other tasks changed since stays.
/// </summary>
public static class TaskUndoRules
{
    public static TaskUndoEntry Capture(TaskLists before, IEnumerable<Guid> ids, TaskActionKind kind, string text)
    {
        var tasks = new Dictionary<Guid, SyncTaskItem?>();
        var log = new Dictionary<Guid, SyncTaskLogItem?>();
        foreach (Guid id in ids.Distinct())
        {
            tasks[id] = before.Tasks.FirstOrDefault(item => item.Id == id);
            log[id] = before.Log.FirstOrDefault(item => item.Id == id);
        }

        return new TaskUndoEntry(kind, text, tasks, log);
    }

    public static TaskLists Restore(TaskLists lists, TaskUndoEntry entry, Guid self, DateTimeOffset now)
    {
        List<SyncTaskItem> tasks = lists.Tasks.ToList();
        foreach ((Guid id, SyncTaskItem? snapshot) in entry.Tasks)
        {
            int index = tasks.FindIndex(item => item.Id == id);
            SyncTaskItem? current = index >= 0 ? tasks[index] : null;
            SyncTaskItem? restored = snapshot is { Task.IsLive: true } ? Reinstated(current, snapshot, now, self) : Gone(current, now);
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

    private static SyncTaskItem Reinstated(SyncTaskItem? current, SyncTaskItem snapshot, DateTimeOffset now, Guid self)
    {
        if (current is null || !current.Task.IsLive)
        {
            // Gone (deleted, or a move's departure): it comes back whole, newer than the tombstone, so every device takes it back.
            DateTimeOffset at = current is null ? now : SharedListMerge.NextStamp(now, current);
            return new SyncTaskItem(snapshot.Task with { EditedAt = at, DeletedAt = null }, TaskFieldStamps.AllAt(new TaskFieldStamp(at, self)));
        }

        SyncTaskItem item = current;
        foreach (TaskField field in TaskFieldValues.All)
        {
            item = TaskEdits.Edit(item, field, snapshot.Task, now, self);
        }

        return item with { Task = item.Task with { FormerNumbers = snapshot.Task.FormerNumbers, Origin = snapshot.Task.Origin } };
    }
}
