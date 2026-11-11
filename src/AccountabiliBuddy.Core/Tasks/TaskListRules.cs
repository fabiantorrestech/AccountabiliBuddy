using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// What the PC's task store (ADR 76) does to its <see cref="TaskLists"/>, kept pure so it can be checked without the app:
/// <list type="bullet">
/// <item><see cref="ApplyLocal"/>: a change the PC's own old lists made (<see cref="TaskFacadeLists"/>) goes in as an edit by this
/// device, through <see cref="OldTaskFieldsReader"/> (the same field rules and stamps an old file gets), keeping the places the
/// lists gave.</item>
/// <item><see cref="MergeFile"/>: another device's sync file: its <see cref="SyncFile.Tasks"/> merged field by field, or, without
/// them, its old fields read in.</item>
/// <item><see cref="Normalize"/>: finished Shelf tasks go to History (as ticked Later items and come-backs always did), numbers are
/// given, History older than its 90 days and tombstones older than 30 go.</item>
/// <item><see cref="WithParts"/>: the new parts and the old fields (rebuilt from the lists, never stored) for this PC's file.</item>
/// </list>
/// </summary>
public static class TaskListRules
{
    // Two positions closer than this are the same place (an old list's place maps back with a rounding error at most).
    private const double SamePlace = 1e-6;

    /// <summary>
    /// The PC's own old lists' changed items (<paramref name="changed"/>: only what changed, tombstones for what went) read in as
    /// this device's edits. A local item kept here comes back over its own stub (a handover taken in); a task keeps the place its
    /// list gave it; a new Auto-saved task gets its origin line; a ticked Later item or come-back deleted here leaves History.
    /// </summary>
    public static TaskLists ApplyLocal(TaskLists lists, OldTaskFields changed, Guid self, DateTimeOffset now, TimeZoneInfo zone, int sessionActions)
    {
        OldTaskFields prepared = Adopted(changed, lists.Tasks, self);
        HashSet<Guid> known = lists.Tasks.Select(item => item.Id).ToHashSet();
        OldTaskReadResult read = OldTaskFieldsReader.Read(new OldTaskReadInput(
            prepared, self, TaskNumbering.PcKind, lists.Tasks, lists.Labels, lists.Log, self, now, zone, sessionActions));

        List<SyncTaskItem> tasks = read.Tasks.ToList();
        foreach ((OldTaskList list, ISharedItem item, DateOnly? autoSavedFor) in Items(prepared).Where(entry => entry.Item.DeletedAt is null))
        {
            int index = tasks.FindIndex(candidate => candidate.Id == item.Id);
            if (index < 0 || !tasks[index].Task.IsLive)
            {
                continue;
            }

            TaskItem task = tasks[index].Task;
            if (OldTaskFieldsWriter.SameHome(TaskFacadeLists.ListOf(task, now), list))
            {
                double wanted = OldTaskFieldsWriter.NewPosition(TaskFacadeLists.ShownBin(task), item.Position);
                if (Math.Abs(wanted - task.Position) > SamePlace)
                {
                    task = task with { Position = wanted };
                }
            }

            if (!known.Contains(item.Id) && list == OldTaskList.AutoSaved && task.Origin is null && autoSavedFor is DateOnly day)
            {
                task = task with { Origin = TaskOrigins.AutoSaved(day) };
            }

            tasks[index] = tasks[index] with { Task = task };
        }

        return new TaskLists(tasks, read.Labels, lists.Views, CloseFinished(read.History, prepared));
    }

    /// <summary>
    /// Another device's file read in (one file at a time: an older app's moves pair within one file). A file with
    /// <see cref="SyncFile.Tasks"/> is the truth for its device: tasks merged field by field, labels, views and History item by item.
    /// One without has its old fields read in; a writer older than ADR 50 (no <c>later</c> part) can't clear an estimate, note or
    /// plan, so those it lacks are this device's. Null when the file carries no task list at all.
    /// </summary>
    public static TaskLists? MergeFile(TaskLists lists, SyncFile file, Guid self, DateTimeOffset now, TimeZoneInfo zone, int sessionActions)
    {
        if (file.Tasks is { } incoming)
        {
            return MergeLists(lists, new TaskLists(incoming, file.Labels ?? [], file.Views ?? [], file.TaskLog ?? []), self, now);
        }

        OldTaskFields fields = OldTaskFields.Of(file);
        if (fields.NextTasks.Count + fields.Later.Count + fields.Parked.Count + fields.Comebacks.Count + fields.Categories.Count + fields.AutoSaved.Count == 0)
        {
            return null;
        }

        if (file.Later is null)
        {
            fields = fields with { NextTasks = WithHeldDetails(fields.NextTasks, lists.Tasks) };
        }

        OldTaskReadResult read = OldTaskFieldsReader.Read(new OldTaskReadInput(
            fields, file.Device.DeviceId, file.Device.Kind ?? string.Empty, lists.Tasks, lists.Labels, lists.Log, self, now, zone, sessionActions));
        return new TaskLists(read.Tasks, read.Labels, lists.Views, CloseFinished(read.History, fields));
    }

    /// <summary>
    /// Another device's (or a backup's) task list merged in: tasks field by field (<see cref="TaskFieldMerge"/>), labels, views and
    /// History item by item. Items a damaged file left empty are dropped.
    /// </summary>
    public static TaskLists MergeLists(TaskLists lists, TaskLists incoming, Guid self, DateTimeOffset now)
    {
        return new TaskLists(
            TaskFieldMerge.Merge(lists.Tasks, incoming.Tasks.OfType<SyncTaskItem>().Where(item => item.Task is not null), now, self),
            SharedListMerge.Merge(lists.Labels, incoming.Labels.OfType<SyncTaskLabel>().Where(item => item.Label is not null), now),
            SharedListMerge.Merge(lists.Views, incoming.Views.OfType<SyncTaskView>().Where(item => item.View is not null), now),
            SharedListMerge.Merge(lists.Log, incoming.Log.OfType<SyncTaskLogItem>().Where(item => item.Entry?.Task is not null), now));
    }

    /// <summary>Settles, numbers and prunes the lists (see the class summary); stamped by <paramref name="self"/>.</summary>
    /// <param name="numberingDevice">The device that gives final numbers (<see cref="TaskNumbering.NumberingDevice"/>); this device gives temporary ones otherwise.</param>
    public static TaskLists Normalize(TaskLists lists, Guid self, Guid? numberingDevice, DateTimeOffset now)
    {
        return Pruned(Numbered(Settled(lists, now), self, numberingDevice, now), now, self);
    }

    /// <summary>
    /// A finished Shelf task (Next, Later, To sort) goes to History, as a ticked Later item or come-back always did: an entry with
    /// its snapshot and a tombstone in the list, both stamped with the tick's own time, so two devices settling it agree.
    /// </summary>
    public static TaskLists Settled(TaskLists lists, DateTimeOffset now)
    {
        var tasks = lists.Tasks.ToList();
        var entries = new List<SyncTaskLogItem>();
        for (int index = 0; index < tasks.Count; index++)
        {
            TaskItem task = tasks[index].Task;
            if (!task.IsLive || task.Bin != TaskBin.Done || task.DoneAt is not DateTimeOffset doneAt || task.PrevBin is not (TaskBin.Sort or TaskBin.Next or TaskBin.Later))
            {
                continue;
            }

            DateTimeOffset at = task.EditedAt;
            entries.Add(new SyncTaskLogItem(new TaskHistoryEntry(task.Id, TaskHistoryKind.Finished, doneAt, task, at)));
            tasks[index] = tasks[index].Deleted(at);
        }

        return entries.Count == 0 ? lists : lists with { Tasks = tasks, Log = SharedListMerge.Merge(lists.Log, entries, now) };
    }

    /// <summary>Final numbers on the numbering device; elsewhere a temporary number for each live task without one, in creation order.</summary>
    public static TaskLists Numbered(TaskLists lists, Guid self, Guid? numberingDevice, DateTimeOffset now)
    {
        IEnumerable<TaskItem> inHistory = lists.Log.Select(item => item.Entry.Task);
        if (numberingDevice == self)
        {
            List<SyncTaskItem> final = TaskNumbering.AssignFinal(lists.Tasks, self, numberingDevice, now, inHistory);
            return final.SequenceEqual(lists.Tasks) ? lists : lists with { Tasks = final };
        }

        var tasks = lists.Tasks.ToList();
        List<int> missing = Enumerable.Range(0, tasks.Count)
            .Where(index => tasks[index].Task.IsLive && tasks[index].Task.Number is null)
            .OrderBy(index => tasks[index].Task.CreatedAt.UtcTicks).ThenBy(index => tasks[index].Id.ToString("D"), StringComparer.Ordinal)
            .ToList();
        foreach (int index in missing)
        {
            TaskNumber number = TaskNumbering.NewNumber(tasks.Select(item => item.Task).Concat(inHistory), isNumberingDevice: false);
            tasks[index] = TaskEdits.Edit(tasks[index], TaskField.Number, tasks[index].Task with { Number = number }, now, self);
        }

        return missing.Count == 0 ? lists : lists with { Tasks = tasks };
    }

    /// <summary>History past its 90 days goes (on every device alike); tombstones past their 30 days go (a local item's stub stays).</summary>
    public static TaskLists Pruned(TaskLists lists, DateTimeOffset now, Guid self)
    {
        List<SyncTaskLogItem> log = lists.Log.Where(item => !item.Entry.IsLive || now - item.Entry.When < TaskHistoryEntry.Kept).ToList();
        return new TaskLists(
            SharedListMerge.Merge(lists.Tasks, [], now, self),
            SharedListMerge.Merge(lists.Labels, [], now),
            SharedListMerge.Merge(lists.Views, [], now),
            SharedListMerge.Merge(log, [], now));
    }

    /// <summary>
    /// <paramref name="file"/> with the task list's parts: the new ones (local items as their stubs) and the old fields rebuilt from
    /// them for older apps (<see cref="OldTaskFieldsWriter"/>; <c>later</c> always, even empty).
    /// </summary>
    public static SyncFile WithParts(SyncFile file, TaskLists lists, DateTimeOffset now, TimeZoneInfo zone)
    {
        OldTaskFields old = OldTaskFieldsWriter.Write(lists.Tasks, lists.Labels, lists.Log, now, zone);
        return file with
        {
            NextTasks = old.NextTasks,
            Later = old.Later,
            Parked = old.Parked,
            Comebacks = old.Comebacks,
            Categories = old.Categories,
            AutoSaved = old.AutoSaved,
            Tasks = SharedListMerge.ForFile(lists.Tasks),
            Labels = lists.Labels.ToList(),
            Views = lists.Views.ToList(),
            TaskLog = lists.Log.ToList(),
        };
    }

    // A local item kept here that this device holds only as a stub or tombstone (a handover taken in, or one that came back) is
    // newer than that stub, whatever times the handover carried.
    private static OldTaskFields Adopted(OldTaskFields changed, IReadOnlyList<SyncTaskItem> tasks, Guid self)
    {
        Dictionary<Guid, TaskItem> gone = tasks.Where(item => !item.Task.IsLive).GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First().Task);

        T Bumped<T>(T item)
            where T : ISharedItem
        {
            if (item.DeletedAt is not null || item.LocalTo != self || !gone.TryGetValue(item.Id, out TaskItem? held))
            {
                return item;
            }

            DateTimeOffset stamp = held.DeletedAt is DateTimeOffset deletedAt && deletedAt > held.EditedAt ? deletedAt : held.EditedAt;
            return item.EditedAt > stamp ? item : (T)item.WithStamps(stamp.AddTicks(1), null);
        }

        return changed with
        {
            NextTasks = changed.NextTasks.Select(Bumped).ToList(),
            Later = changed.Later.Select(Bumped).ToList(),
            Parked = changed.Parked.Select(Bumped).ToList(),
            AutoSaved = changed.AutoSaved.Select(Bumped).ToList(),
        };
    }

    private static IEnumerable<(OldTaskList List, ISharedItem Item, DateOnly? AutoSavedFor)> Items(OldTaskFields fields)
    {
        return fields.NextTasks.Select(item => (OldTaskList.NextTasks, (ISharedItem)item, (DateOnly?)null))
            .Concat(fields.Later.Select(item => (OldTaskList.Later, (ISharedItem)item, (DateOnly?)null)))
            .Concat(fields.Parked.Select(item => (OldTaskList.Parked, (ISharedItem)item, (DateOnly?)null)))
            .Concat(fields.AutoSaved.Select(item => (OldTaskList.AutoSaved, (ISharedItem)item, (DateOnly?)item.WrapUpOf)));
    }

    // A ticked Later item or come-back deleted after it was finished (newer than its History entry) leaves History too, as deleting
    // a ticked item always removed it; otherwise it would come back ticked with the next write.
    private static List<SyncTaskLogItem> CloseFinished(IReadOnlyList<SyncTaskLogItem> history, OldTaskFields fields)
    {
        Dictionary<Guid, DateTimeOffset> deleted = fields.Later.Cast<ISharedItem>().Concat(fields.Parked)
            .Where(item => item.DeletedAt is not null && item.LocalTo is null)
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.Max(item => item.DeletedAt!.Value));
        return history
            .Select(item => item.Entry.IsLive && item.Entry.Kind == TaskHistoryKind.Finished
                && deleted.TryGetValue(item.Id, out DateTimeOffset at) && at > item.Entry.EditedAt
                    ? new SyncTaskLogItem(item.Entry.Deleted(at))
                    : item)
            .ToList();
    }

    private static List<SyncNextTask> WithHeldDetails(IReadOnlyList<SyncNextTask> incoming, IReadOnlyList<SyncTaskItem> tasks)
    {
        Dictionary<Guid, TaskItem> held = tasks.Where(item => item.Task.IsLive).GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First().Task);
        return incoming.Select(item => item.DeletedAt is null && held.TryGetValue(item.Id, out TaskItem? own)
            ? item with
            {
                EstimateMinutes = item.EstimateMinutes ?? own.EstimateMinutes,
                Note = item.Note ?? own.Note,
                PlannedFor = item.PlannedFor ?? own.PlannedFor,
            }
            : item).ToList();
    }
}
