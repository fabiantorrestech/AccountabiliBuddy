using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Stamps a local change to a task (ADR 76) the way <see cref="TaskFieldMerge"/> expects: the changed field gets a new stamp
/// (now, or just past the task's newest stamp), a title or note remembers the value it was edited from, and the task's edit and
/// touch times move on. The stores use it for every change; nothing else writes stamps.
/// </summary>
public static class TaskEdits
{
    /// <returns>The task with <paramref name="field"/> taken from <paramref name="changed"/> and stamped; the same item when the value didn't change.</returns>
    public static SyncTaskItem Edit(SyncTaskItem item, TaskField field, TaskItem changed, DateTimeOffset now, Guid by)
    {
        TaskItem task = item.Task;
        if (string.Equals(TaskFieldValues.Canonical(task, field), TaskFieldValues.Canonical(changed, field), StringComparison.Ordinal))
        {
            return item;
        }

        IReadOnlyList<string>? bases = null;
        if (field is TaskField.Title or TaskField.Note)
        {
            bases = WithBase(item.Stamps.Of(field)?.Bases, TaskFieldValues.Hash(TaskFieldValues.Text(task, field)));
        }

        return Stamped(item, field, TaskFieldValues.With(task, changed, field), bases, now, by);
    }

    /// <summary>
    /// The user's pick in "Changed on both devices": <paramref name="kept"/> becomes the value, stamped as made from both versions,
    /// so the conflict closes on every device once synced.
    /// </summary>
    public static SyncTaskItem Resolve(SyncTaskItem item, TaskConflict conflict, string? kept, DateTimeOffset now, Guid by)
    {
        TaskField field = conflict.Field == TaskConflict.TitleField ? TaskField.Title : TaskField.Note;
        TaskItem task = item.Task;
        IReadOnlyList<string>? bases = WithBase(item.Stamps.Of(field)?.Bases, TaskFieldValues.Hash(TaskFieldValues.Text(task, field)));
        bases = WithBase(bases, TaskFieldValues.Hash(conflict.OtherValue));

        TaskItem changed = field == TaskField.Title ? task with { Title = kept ?? string.Empty } : task with { Note = kept };
        List<TaskConflict> open = (task.Conflicts ?? []).Where(other => other != conflict).ToList();
        changed = changed with { Conflicts = open.Count == 0 ? null : open };
        return Stamped(item, field, changed, bases, now, by);
    }

    private static SyncTaskItem Stamped(SyncTaskItem item, TaskField field, TaskItem changed, IReadOnlyList<string>? bases, DateTimeOffset now, Guid by)
    {
        DateTimeOffset at = SharedListMerge.NextStamp(now, item);
        if (item.Stamps.Newest is DateTimeOffset newest && newest >= at)
        {
            at = newest.AddTicks(1);
        }

        // A field never stamped counts from the task's creation: the edit must still be newer than that.
        DateTimeOffset floor = item.Stamps.Of(field)?.At ?? item.Task.CreatedAt;
        if (floor >= at)
        {
            at = floor.AddTicks(1);
        }

        TaskItem task = changed with { EditedAt = at, TouchedAt = at > changed.TouchedAt ? at : changed.TouchedAt };
        return new SyncTaskItem(task, item.Stamps.With(field, new TaskFieldStamp(at, by, bases)));
    }

    private static IReadOnlyList<string> WithBase(IReadOnlyList<string>? bases, string hash)
    {
        List<string> all = (bases ?? []).Where(other => other != hash).Append(hash).ToList();
        return all.Count > TaskFieldMerge.MaxBases ? all[^TaskFieldMerge.MaxBases..] : all;
    }
}
