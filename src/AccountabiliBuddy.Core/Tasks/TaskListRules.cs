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
/// <item><see cref="Normalize"/>: finished Shelf tasks go to History (as ticked Later items and come-backs always did), archived and
/// deleted entries of tasks that are live again go (<see cref="TaskHistoryRules.Reconciled"/>), numbers are given, History older than its 90 days and tombstones older than 30 go.</item>
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
        OldTaskFields prepared = Adopted(WithoutMoveDepartures(changed, lists.Tasks, now), lists.Tasks, self);
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

        return new TaskLists(tasks, read.Labels, lists.Views, CloseFinished(read.History, prepared), lists.Account);
    }

    /// <summary>
    /// Tasks put in the list as this device's edits, each where <paramref name="wanted"/> says (its bin, place and fields). A live task
    /// with the id is changed field by field (<see cref="TaskEdits"/>): a move is a bin edit, so it keeps its id, number, steps,
    /// reminders and History. A tombstone or an unknown id comes back to life: from its <paramref name="held"/> copy when there is one
    /// (a running session's task, which keeps its number and everything else), else as a new task; either way it is stamped after the
    /// tombstone, so every device takes it back. Steps leave focus outside This session.
    /// </summary>
    public static TaskLists Place(TaskLists lists, IReadOnlyList<TaskItem> wanted, IReadOnlyDictionary<Guid, SyncTaskItem>? held, Guid self, DateTimeOffset now)
    {
        var tasks = lists.Tasks.ToList();
        foreach (TaskItem want in wanted)
        {
            TaskItem target = want.Bin == TaskBin.Session ? want : want with { Steps = Unfocused(want.Steps) };
            int index = tasks.FindIndex(item => item.Id == target.Id);
            SyncTaskItem? current = index >= 0 ? tasks[index] : null;
            DateTimeOffset? gone = current is { Task.IsLive: false } ? current.Task.DeletedAt ?? current.Task.EditedAt : null;
            SyncTaskItem? source = current is { Task.IsLive: true } ? current
                : held is not null && held.TryGetValue(target.Id, out SyncTaskItem? kept) && kept.Task.IsLive ? kept
                : null;

            SyncTaskItem placed;
            if (source is null)
            {
                DateTimeOffset at = gone is DateTimeOffset deletedAt && deletedAt >= now ? deletedAt.AddTicks(1) : now;
                placed = new SyncTaskItem(
                    target with { EditedAt = at, TouchedAt = at, DeletedAt = null, CreatedAt = target.CreatedAt > at ? at : target.CreatedAt },
                    TaskFieldStamps.AllAt(new TaskFieldStamp(at, self)));
            }
            else
            {
                placed = source;
                foreach (TaskField field in TaskFieldValues.All)
                {
                    placed = TaskEdits.Edit(placed, field, target, now, self);
                }

                if (gone is DateTimeOffset deletedAt && placed.Task.EditedAt <= deletedAt)
                {
                    // Nothing changed since it was held: still newer than the tombstone, or no device would take it back.
                    DateTimeOffset at = deletedAt.AddTicks(1);
                    placed = TaskEdits.Edit(placed, TaskField.Bin, placed.Task with { Position = placed.Task.Position + 1e-7 }, at, self);
                }
            }

            if (index >= 0)
            {
                tasks[index] = placed;
            }
            else
            {
                tasks.Add(placed);
            }
        }

        return lists with { Tasks = tasks };
    }

    /// <summary>A place after every live task in <paramref name="bin"/> (1 in an empty bin).</summary>
    public static double EndOf(TaskLists lists, TaskBin bin)
    {
        List<double> positions = lists.Tasks.Select(item => item.Task).Where(task => task.IsLive && task.Bin == bin).Select(task => task.Position).ToList();
        return positions.Count == 0 ? 1 : Math.Floor(positions.Max()) + 1;
    }

    /// <summary>
    /// A running checklist's task as it goes back to the list in <paramref name="bin"/> (Queued after a swap or the pick): what its
    /// <paramref name="held"/> copy had (number, reminders, highlight, due, devices, origin, step estimates) with the checklist's
    /// changes (<see cref="TaskSessionMapper.Apply"/>), at <paramref name="position"/>; a new task when nothing was held.
    /// </summary>
    public static TaskItem FromChecklist(SessionTask task, TaskItem? held, TaskBin bin, double position, IReadOnlyList<TaskLabel> labels, DateTimeOffset now)
    {
        TaskItem target = held ?? new TaskItem(task.Id, task.Title, bin, position, now, now, now);
        TaskItem applied = TaskSessionMapper.Apply(target, task, labels);
        bool done = bin == TaskBin.Done;
        return applied with
        {
            Bin = bin,
            PrevBin = done ? TaskBin.Session : bin == TaskBin.Queued ? TaskBin.Session : applied.PrevBin,
            Position = position,
            DoneAt = done ? applied.DoneAt ?? now : null,
        };
    }

    /// <summary>
    /// Another device's file read in (one file at a time: an older app's moves pair within one file). A file with
    /// <see cref="SyncFile.Tasks"/> is the truth for its device: tasks merged field by field, labels, views and History item by item.
    /// One without has its old fields read in; a writer older than ADR 50 (no <c>later</c> part) can't clear an estimate, note or
    /// plan, so those it lacks are this device's. The account's Tasks settings merge field by field whenever the file carries them.
    /// Null when the file carries no task list and no account settings at all.
    /// </summary>
    public static TaskLists? MergeFile(TaskLists lists, SyncFile file, Guid self, DateTimeOffset now, TimeZoneInfo zone, int sessionActions)
    {
        TaskLists? merged = MergeTaskParts(lists, file, self, now, zone, sessionActions);
        if (file.TaskAccountSettings is not { } account)
        {
            return merged;
        }

        TaskLists basis = merged ?? lists;
        return basis with { Account = TaskAccountSettingsMerge.Merge(basis.Account, account, now) };
    }

    private static TaskLists? MergeTaskParts(TaskLists lists, SyncFile file, Guid self, DateTimeOffset now, TimeZoneInfo zone, int sessionActions)
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
        return new TaskLists(read.Tasks, read.Labels, lists.Views, CloseFinished(read.History, fields), lists.Account);
    }

    /// <summary>
    /// Another device's (or a backup's) task list merged in: tasks field by field (<see cref="TaskFieldMerge"/>), labels, views and
    /// History item by item, the account's Tasks settings field by field (none incoming keeps this device's). Items a damaged file left
    /// empty are dropped.
    /// </summary>
    public static TaskLists MergeLists(TaskLists lists, TaskLists incoming, Guid self, DateTimeOffset now)
    {
        return new TaskLists(
            TaskFieldMerge.Merge(lists.Tasks, incoming.Tasks.OfType<SyncTaskItem>().Where(item => item.Task is not null), now, self),
            SharedListMerge.Merge(lists.Labels, incoming.Labels.OfType<SyncTaskLabel>().Where(item => item.Label is not null), now),
            SharedListMerge.Merge(lists.Views, incoming.Views.OfType<SyncTaskView>().Where(item => item.View is not null), now),
            SharedListMerge.Merge(lists.Log, incoming.Log.OfType<SyncTaskLogItem>().Where(item => item.Entry?.Task is not null), now),
            incoming.Account is null ? lists.Account : TaskAccountSettingsMerge.Merge(lists.Account, incoming.Account, now));
    }

    /// <summary>Settles, numbers and prunes the lists (see the class summary); stamped by <paramref name="self"/>.</summary>
    /// <param name="numberingDevice">The device that gives final numbers (<see cref="TaskNumbering.NumberingDevice"/>); this device gives temporary ones otherwise.</param>
    /// <param name="zone">The device's time zone, for "its finish day is over" (see <see cref="Settled"/>); null leaves finished Queued tasks where they are.</param>
    public static TaskLists Normalize(TaskLists lists, Guid self, Guid? numberingDevice, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        return Pruned(Numbered(TaskHistoryRules.Reconciled(Settled(lists, now, zone), now), self, numberingDevice, now), now, self);
    }

    /// <summary>
    /// A finished Shelf task (Next, Later, To sort) goes to History, as a ticked Later item or come-back always did: an entry with
    /// its snapshot and a tombstone in the list, both stamped with the tick's own time, so two devices settling it agree. A finished
    /// Queued task stays in Finished today for the rest of its finish day (in <paramref name="zone"/>, as Finished today counts days)
    /// and goes to History the same way once that day is over; with no zone it stays.
    /// </summary>
    public static TaskLists Settled(TaskLists lists, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        DateOnly? today = zone is null ? null : DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var tasks = lists.Tasks.ToList();
        var entries = new List<SyncTaskLogItem>();
        for (int index = 0; index < tasks.Count; index++)
        {
            TaskItem task = tasks[index].Task;
            if (!task.IsLive || task.Bin != TaskBin.Done || task.DoneAt is not DateTimeOffset doneAt)
            {
                continue;
            }

            bool fromShelf = task.PrevBin is TaskBin.Sort or TaskBin.Next or TaskBin.Later;
            bool queuedDayOver = task.PrevBin == TaskBin.Queued && today is DateOnly day
                && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(doneAt, zone!).DateTime) < day;
            if (!fromShelf && !queuedDayOver)
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
            SharedListMerge.Merge(log, [], now),
            lists.Account);
    }

    /// <summary>
    /// <paramref name="file"/> with the task list's parts: the new ones (local items as their stubs) and the old fields rebuilt from
    /// them for older apps (<see cref="OldTaskFieldsWriter"/>; <c>later</c> always, even empty), and the account's Tasks settings once
    /// something was chosen.
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
            TaskAccountSettings = lists.Account is { IsEmpty: false } account ? account : null,
        };
    }

    /// <summary>
    /// The tasks an old list deleted that were left alone because they live elsewhere now and were moved after the delete was made
    /// (a stale delete: another device moved the task meanwhile). A move's own departure (same moment) is not one. For the toast
    /// <see cref="TaskToasts.MovedElsewhere"/>.
    /// </summary>
    public static IReadOnlyList<TaskItem> StaleDeletes(OldTaskFields changed, IReadOnlyList<SyncTaskItem> tasks, DateTimeOffset now)
    {
        Dictionary<Guid, SyncTaskItem> live = tasks.Where(item => item.Task.IsLive).GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
        var stale = new List<TaskItem>();
        foreach ((OldTaskList list, ISharedItem item, DateOnly? _) in Items(changed).Where(entry => entry.Item.DeletedAt is not null))
        {
            if (live.TryGetValue(item.Id, out SyncTaskItem? current)
                && !OldTaskFieldsWriter.SameHome(TaskFacadeLists.ListOf(current.Task, now), list)
                && current.Stamps.Of(TaskField.Bin) is { } moved && moved.At > item.DeletedAt!.Value
                && stale.All(known => known.Id != item.Id))
            {
                stale.Add(current.Task);
            }
        }

        return stale;
    }

    // A move between two of this PC's own lists (ADR 76) keeps the task's id: the list it goes to adds it with that id, which moves its
    // bin; the list it leaves then drops it (a tombstone) or ticks it (a come-back taken to Today). That second half is not a delete or
    // a tick of the task, which now lives in another list: it is left out. A list only shows a task whose home it is, so a real delete
    // or tick always comes from the task's own list.
    private static OldTaskFields WithoutMoveDepartures(OldTaskFields changed, IReadOnlyList<SyncTaskItem> tasks, DateTimeOffset now)
    {
        Dictionary<Guid, TaskItem> live = tasks.Where(item => item.Task.IsLive).GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First().Task);

        bool Departs(OldTaskList list, ISharedItem item, bool ticked) =>
            (item.DeletedAt is not null || ticked)
            && live.TryGetValue(item.Id, out TaskItem? task)
            && !OldTaskFieldsWriter.SameHome(TaskFacadeLists.ListOf(task, now), list);

        return changed with
        {
            NextTasks = changed.NextTasks.Where(item => !Departs(OldTaskList.NextTasks, item, item.DoneAt is not null)).ToList(),
            Later = changed.Later.Where(item => !Departs(OldTaskList.Later, item, item.DoneAt is not null)).ToList(),
            Parked = changed.Parked.Where(item => !Departs(OldTaskList.Parked, item, item.DoneAt is not null)).ToList(),
            AutoSaved = changed.AutoSaved.Where(item => !Departs(OldTaskList.AutoSaved, item, false)).ToList(),
        };
    }

    private static IReadOnlyList<TaskStep>? Unfocused(IReadOnlyList<TaskStep>? steps)
    {
        return steps is null || steps.All(step => !step.Focus) ? steps : steps.Select(step => step with { Focus = false }).ToList();
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
