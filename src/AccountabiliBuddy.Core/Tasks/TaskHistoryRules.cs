using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// History (ADR 76), pure: a task that is finished, archived or deleted is kept with its snapshot (90 days stored, the last
/// <see cref="Listed"/> listed). <see cref="Remove"/> files a task there, <see cref="DoAgain"/> makes a new Queued copy of a finished
/// one, <see cref="BringBack"/> revives an archived or deleted one under its own id and number. A "deleted" entry is also what
/// "Recently deleted" meant for tasks (ADR 50): the PC's own deletes through its old lists are filed here too
/// (<see cref="WithDeletions"/>), so one list serves Undo, Restore and History.
/// </summary>
public static class TaskHistoryRules
{
    /// <summary>How far back History is listed (it is kept longer, <see cref="TaskHistoryEntry.Kept"/>).</summary>
    public static readonly TimeSpan Listed = TimeSpan.FromDays(30);

    /// <summary>The live entries of the last <see cref="Listed"/>, newest first.</summary>
    public static TaskHistoryList List(TaskLists lists, DateTimeOffset now)
    {
        List<TaskHistoryEntry> entries = lists.Log.Select(item => item.Entry)
            .Where(entry => entry.IsLive && now - entry.When < Listed)
            .OrderByDescending(entry => entry.When).ToList();
        return new TaskHistoryList(
            entries.Where(entry => entry.Kind == TaskHistoryKind.Finished).ToList(),
            entries.Where(entry => entry.Kind != TaskHistoryKind.Finished).ToList());
    }

    /// <summary>Files live tasks in History as <paramref name="kind"/> (archived or deleted) with everything they had, and takes them out of the list.</summary>
    public static TaskActionResult Remove(TaskLists lists, IReadOnlyList<Guid> ids, TaskHistoryKind kind, DateTimeOffset now, Guid self)
    {
        List<SyncTaskItem> tasks = lists.Tasks.ToList();
        List<SyncTaskLogItem> log = lists.Log.ToList();
        var removed = new List<TaskItem>();
        foreach (Guid id in ids.Distinct())
        {
            int index = tasks.FindIndex(item => item.Id == id && item.Task.IsLive);
            if (index < 0)
            {
                continue;
            }

            SyncTaskItem item = tasks[index];
            removed.Add(item.Task);
            tasks[index] = item.Deleted(SharedListMerge.NextStamp(now, item));
            PutEntry(log, new TaskHistoryEntry(id, kind, now, item.Task, now), now);
        }

        if (removed.Count == 0)
        {
            return TaskActionResult.None(lists);
        }

        string toast = kind == TaskHistoryKind.Archived ? TaskToasts.Archived(removed) : TaskToasts.Deleted(removed);
        return new TaskActionResult(lists with { Tasks = tasks, Log = log }, toast, removed.Select(task => task.Id).ToList());
    }

    /// <summary>
    /// "Do again": a finished task's entry makes a new task in Queued, at the end of it, with a new id (and a number of its own
    /// once given), its label, tags, time limit, note and steps (open again, new ids), and the origin "Done again from 142". Reminders,
    /// due date and highlight are not copied. The entry stays, so it can be done again, and its number stays taken.
    /// </summary>
    public static TaskActionResult DoAgain(TaskLists lists, Guid entryId, DateTimeOffset now, Guid self)
    {
        if (lists.Log.FirstOrDefault(item => item.Id == entryId) is not { Entry: { IsLive: true, Kind: TaskHistoryKind.Finished } } found)
        {
            return TaskActionResult.None(lists);
        }

        TaskItem old = found.Entry.Task;
        List<TaskStep>? steps = old.Steps?.Select(step => new TaskStep(Guid.NewGuid(), step.Title, EstimateMinutes: step.EstimateMinutes)).ToList();
        var copy = new TaskItem(
            Guid.NewGuid(), old.Title, TaskBin.Queued, TaskListRules.EndOf(lists, TaskBin.Queued), now, now, now,
            LabelId: old.LabelId,
            Tags: old.Tags,
            Steps: steps is { Count: > 0 } ? steps : null,
            EstimateMinutes: old.EstimateMinutes,
            Effort: old.Effort,
            Note: old.Note,
            Origin: TaskOrigins.DoneAgain(old.Number),
            Devices: old.Devices,
            Calendar: old.Calendar);
        List<SyncTaskItem> tasks = [.. lists.Tasks, new SyncTaskItem(copy, TaskFieldStamps.AllAt(new TaskFieldStamp(now, self)))];
        return new TaskActionResult(lists with { Tasks = tasks }, TaskToasts.DoneAgain(copy), [copy.Id]);
    }

    /// <summary>
    /// "Bring back": an archived or deleted task comes back under its own id and number, in its last bin (Queued when that was This
    /// session or Finished today, as a place in a session is the cap's to give), at its old place, touched now. The entry goes.
    /// </summary>
    public static TaskActionResult BringBack(TaskLists lists, Guid entryId, DateTimeOffset now, Guid self)
    {
        if (lists.Log.FirstOrDefault(item => item.Id == entryId) is not { Entry: { IsLive: true } } found || found.Entry.Kind == TaskHistoryKind.Finished)
        {
            return TaskActionResult.None(lists);
        }

        TaskItem old = found.Entry.Task;
        if (lists.Tasks.Any(item => item.Id == old.Id && item.Task.IsLive))
        {
            return TaskActionResult.None(lists);
        }

        TaskBin bin = old.Bin is TaskBin.Session or TaskBin.Done ? TaskBin.Queued : old.Bin;
        List<SyncTaskItem> tasks = lists.Tasks.ToList();
        int index = tasks.FindIndex(item => item.Id == old.Id);
        DateTimeOffset at = index >= 0 ? SharedListMerge.NextStamp(now, tasks[index]) : now;
        TaskItem back = old with
        {
            Bin = bin,
            Position = old.Bin == bin ? old.Position : TaskListRules.EndOf(lists, bin),
            Steps = old.Steps is null ? null : old.Steps.Select(step => step with { Focus = false }).ToList(),
            DoneAt = null,
            DeletedAt = null,
            EditedAt = at,
            TouchedAt = at,
        };
        var revived = new SyncTaskItem(back, TaskFieldStamps.AllAt(new TaskFieldStamp(at, self)));
        if (index >= 0)
        {
            tasks[index] = revived;
        }
        else
        {
            tasks.Add(revived);
        }

        List<SyncTaskLogItem> log = lists.Log.ToList();
        PutEntry(log, found.Entry.Deleted(SharedListMerge.NextStamp(now, found)), now);
        return new TaskActionResult(lists with { Tasks = tasks, Log = log }, TaskToasts.BroughtBack(back), [old.Id]);
    }

    /// <summary>
    /// The deletes the PC's own old lists made (<paramref name="deleted"/>: ids they deleted) filed as "deleted" entries, from the
    /// tasks as they were in <paramref name="before"/>. Only a task that was live and is gone now and has no live entry already
    /// (a finished Shelf task settles into History by itself); the caller leaves out what is not a delete (a running session's tasks).
    /// </summary>
    public static TaskLists WithDeletions(TaskLists before, TaskLists after, IReadOnlySet<Guid> deleted, DateTimeOffset now)
    {
        List<SyncTaskLogItem>? log = null;
        foreach (Guid id in deleted)
        {
            SyncTaskItem? was = before.Tasks.FirstOrDefault(item => item.Id == id);
            SyncTaskItem? afterItem = after.Tasks.FirstOrDefault(item => item.Id == id);
            if (was is not { Task.IsLive: true } || afterItem is null || afterItem.Task.IsLive
                || (log ?? after.Log.ToList()).Any(item => item.Id == id && item.Entry.IsLive))
            {
                continue;
            }

            log ??= after.Log.ToList();
            PutEntry(log, new TaskHistoryEntry(id, TaskHistoryKind.Deleted, now, was.Task, now), now);
        }

        return log is null ? after : after with { Log = log };
    }

    /// <summary>
    /// An archived or deleted entry whose task is live again (restored by Undo, Restore or a device that edited it) goes: History
    /// never lists a task that is back.
    /// </summary>
    public static TaskLists Reconciled(TaskLists lists, DateTimeOffset now)
    {
        HashSet<Guid> live = lists.Tasks.Where(item => item.Task.IsLive).Select(item => item.Id).ToHashSet();
        if (!lists.Log.Any(item => item.Entry.IsLive && item.Entry.Kind != TaskHistoryKind.Finished && live.Contains(item.Id)))
        {
            return lists;
        }

        List<SyncTaskLogItem> log = lists.Log
            .Select(item => item.Entry.IsLive && item.Entry.Kind != TaskHistoryKind.Finished && live.Contains(item.Id)
                ? new SyncTaskLogItem(item.Entry.Deleted(SharedListMerge.NextStamp(now, item)))
                : item)
            .ToList();
        return lists with { Log = log };
    }

    // The entry replaces the one with its id (stamped after it, so it wins) or is added.
    private static void PutEntry(List<SyncTaskLogItem> log, TaskHistoryEntry entry, DateTimeOffset now)
    {
        int index = log.FindIndex(item => item.Id == entry.Id);
        if (index < 0)
        {
            log.Add(new SyncTaskLogItem(entry with { EditedAt = entry.EditedAt < now ? now : entry.EditedAt }));
            return;
        }

        DateTimeOffset at = SharedListMerge.NextStamp(now, log[index]);
        log[index] = new SyncTaskLogItem(entry with { EditedAt = entry.EditedAt > at ? entry.EditedAt : at });
    }
}
