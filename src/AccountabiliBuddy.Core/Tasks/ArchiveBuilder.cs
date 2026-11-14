using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The Archive's tasks, pure (AR1-fix), so the PC and the phone build the same rows from the same synced data. Sources, in the order
/// that wins when one task id is in several: this device's own history, past tasks kept here, other PCs' histories, then the one task
/// list's finished tasks (ADR 76: Queued, Next, To sort ticks, the phone's). Each id shows once. ADR 72's settle records are laid over
/// the received rows (a tick or an untick done on any device shows at once, before the owner's file arrives; steps stay as the file has them) and a delete over the own ones too. A task
/// deleted from the one task list leaves the Archive and is listed in Recently deleted, as is a past task deleted anywhere. Received and
/// kept rows can be ticked and deleted (those go through settle records or the keeper) but never renamed: their text is the other
/// PC's. Task text is shown, never logged.
/// </summary>
public static class ArchiveBuilder
{
    /// <summary>How far back Recently deleted lists (ADR 50, 76).</summary>
    public static readonly TimeSpan DeletedListed = TimeSpan.FromDays(30);

    /// <summary>How far back a task of the task list shows in the Archive (the history is kept 90 days).</summary>
    public static readonly TimeSpan Listed = TimeSpan.FromDays(90);

    public static ArchiveRows Build(ArchiveInput input)
    {
        Dictionary<Guid, SyncPastSettled> settled = LatestRecords(input.Settled);
        Dictionary<Guid, DateTimeOffset> deletedInList = DeletedInList(input.Lists);
        Dictionary<Guid, (TaskItem Task, DateTimeOffset At)> finished = FinishedInList(input.Lists);
        var seen = new HashSet<Guid>();
        var rows = new List<ArchiveTaskRow>();
        var settledDeleted = new List<ArchiveDeletedRow>();

        void Past(ArchiveSource source, Guid? deviceId, string? deviceName, DateTimeOffset? sessionStart, bool outside, DateTimeOffset start, SessionTask task, bool settleApplies)
        {
            if (task.Title.Length == 0 || !seen.Add(task.Id))
            {
                return;
            }

            SyncPastSettled? record = settleApplies && settled.TryGetValue(task.Id, out SyncPastSettled? held) ? held : null;
            if (record?.State == SyncPastSettled.Deleted)
            {
                if (input.Now - record.EditedAt < DeletedListed)
                {
                    settledDeleted.Add(new ArchiveDeletedRow(task.Id, task.Title, record.EditedAt, source, deviceName, null, RestoreStateOf(task)));
                }

                return;
            }

            if (deletedInList.TryGetValue(task.Id, out DateTimeOffset deletedAt) && (task.DoneAt is null || deletedAt >= task.DoneAt))
            {
                return;
            }

            DateTimeOffset? doneAt = task.DoneAt;
            List<ArchiveStepRow> steps = task.Subtasks.Select(step => new ArchiveStepRow(step.Id, step.Title, step.DoneAt)).ToList();
            // This PC's own copy already carries every tick (the settle records are applied to it), so an own row takes only a delete.
            // A received copy is the other PC's file, maybe older than a tick made since: the record settles the task as a whole when the
            // copy disagrees, and never changes its steps (a step tick is the owner's own, or comes with its file).
            if (source == ArchiveSource.Received && record?.State == SyncPastSettled.Done && doneAt is null)
            {
                doneAt = record.EditedAt;
            }
            else if (source == ArchiveSource.Received && record?.State == SyncPastSettled.Open)
            {
                doneAt = null;
            }

            string title = task.Title;
            Guid? categoryId = task.CategoryId;
            bool ownsText = source == ArchiveSource.Own;
            // A "done outside a session" record is a per-PC copy of a task that lives in the one task list: that list's text and ticks
            // are the shared truth, so a rename or a step made on another device shows here too (and is changed there).
            if (outside && finished.TryGetValue(task.Id, out (TaskItem Task, DateTimeOffset At) live))
            {
                title = live.Task.Title;
                doneAt = live.At;
                categoryId = live.Task.LabelId ?? categoryId;
                steps = (live.Task.Steps ?? []).Select(step => new ArchiveStepRow(step.Id, step.Title, step.DoneAt)).ToList();
                ownsText = false;
            }

            rows.Add(new ArchiveTaskRow(
                task.Id, title, doneAt, sessionStart, outside, source, deviceId, deviceName,
                categoryId, task.Category, task.CategoryColor, steps, doneAt ?? start,
                CanTick: true, CanDelete: true, CanEdit: ownsText));
        }

        foreach (SyncTaskHistoryEntry entry in input.Own.Where(entry => entry.SessionStart is not null || entry.Outside))
        {
            DateTimeOffset start = StartOf(entry);
            foreach (SessionTask task in entry.Tasks)
            {
                Past(ArchiveSource.Own, null, null, entry.SessionStart, entry.Outside, start, task, settleApplies: true);
            }
        }

        foreach (SyncKeptPast item in input.Kept.OrderBy(item => item.Task.Id.ToString("D"), StringComparer.Ordinal))
        {
            // Kept here: ticked and deleted here only (no settle record), and its text is another PC's.
            Past(ArchiveSource.Kept, item.SourceDeviceId, item.SourceName, item.SessionStart, false, item.SessionStart ?? item.Task.DoneAt ?? default, item.Task, settleApplies: false);
        }

        foreach (ArchiveReceivedDevice device in input.Received.OrderBy(device => device.DeviceId.ToString("D"), StringComparer.Ordinal))
        {
            foreach (SyncTaskHistoryEntry entry in device.Entries)
            {
                DateTimeOffset start = StartOf(entry);
                foreach (SessionTask task in entry.Tasks.Where(task => task.LocalTo is null))
                {
                    Past(ArchiveSource.Received, device.DeviceId, device.Name, entry.SessionStart, entry.Outside, start, task, settleApplies: true);
                }
            }
        }

        foreach ((Guid id, (TaskItem task, DateTimeOffset at)) in finished.OrderBy(pair => pair.Key.ToString("D"), StringComparer.Ordinal))
        {
            if (input.Now - at >= Listed || input.Running?.Contains(id) == true || !seen.Add(id))
            {
                continue;
            }

            rows.Add(new ArchiveTaskRow(
                id, task.Title, at, null, false, ArchiveSource.TaskList, null, null,
                task.LabelId, null, null,
                (task.Steps ?? []).Select(step => new ArchiveStepRow(step.Id, step.Title, step.DoneAt)).ToList(),
                at, CanTick: false, CanDelete: false, CanEdit: false));
        }

        // A past task deleted on any device whose copy no history here holds any more (its owner's next file no longer carries it): this
        // device's own snapshot, if it has one, keeps it listed (AR1b). Same ids, same lines on every device that holds the same data.
        foreach (SessionTask snapshot in input.Snapshots ?? [])
        {
            if (snapshot.Title.Length > 0 && !seen.Contains(snapshot.Id) && settled.TryGetValue(snapshot.Id, out SyncPastSettled? gone)
                && gone.State == SyncPastSettled.Deleted && input.Now - gone.EditedAt < DeletedListed && seen.Add(snapshot.Id))
            {
                settledDeleted.Add(new ArchiveDeletedRow(snapshot.Id, snapshot.Title, gone.EditedAt, ArchiveSource.Own, null, null, RestoreStateOf(snapshot)));
            }
        }

        List<ArchiveDeletedRow> deleted = DeletedFromList(input.Lists, input.Now);
        HashSet<Guid> listed = deleted.Select(row => row.Id).ToHashSet();
        deleted.AddRange(settledDeleted.Where(row => listed.Add(row.Id)));
        return new ArchiveRows(
            rows.OrderByDescending(row => row.Sort).ThenBy(row => row.Id.ToString("D"), StringComparer.Ordinal).ToList(),
            deleted.OrderByDescending(row => row.DeletedAt).ThenBy(row => row.Id.ToString("D"), StringComparer.Ordinal).ToList())
        {
            Notes = Notes(input),
        };
    }

    /// <summary>The state Restore stamps for a deleted past task: <c>Done</c> when it was done, else <c>Open</c>.</summary>
    public static string RestoreStateOf(SessionTask task)
    {
        return task.DoneAt is null ? SyncPastSettled.Open : SyncPastSettled.Done;
    }

    // The session notes of every device (ADR 87), newest first: each one from another device says "from <device>" (a removed device's notes
    // stay, with its last known name, or "another device" when none is known); this device's own are unmarked.
    private static List<ArchiveNoteRow> Notes(ArchiveInput input)
    {
        return (input.Notes ?? [])
            .Where(note => note.DeletedAt is null && !string.IsNullOrWhiteSpace(note.Text))
            .Select(note =>
            {
                bool other = note.By != Guid.Empty && note.By != input.Self;
                string? name = other && input.DeviceNames is not null && input.DeviceNames.TryGetValue(note.By, out string? known) && !string.IsNullOrWhiteSpace(known) ? known : null;
                return new ArchiveNoteRow(note.Id, note.Text!, note.At, note.By, name, other);
            })
            .OrderByDescending(note => note.At)
            .ThenBy(note => note.Id.ToString("D"), StringComparer.Ordinal)
            .ToList();
    }

    // A session's start; a "done outside a session" record has none, so its latest tick stands in (as the Archive always ordered it).
    private static DateTimeOffset StartOf(SyncTaskHistoryEntry entry)
    {
        return entry.SessionStart ?? entry.Tasks.Max(task => task.DoneAt) ?? default;
    }

    // The newest live record per past task.
    private static Dictionary<Guid, SyncPastSettled> LatestRecords(IReadOnlyList<SyncPastSettled> records)
    {
        var latest = new Dictionary<Guid, SyncPastSettled>();
        foreach (SyncPastSettled record in records.Where(record => record.DeletedAt is null))
        {
            if (!latest.TryGetValue(record.Id, out SyncPastSettled? held) || record.EditedAt > held.EditedAt)
            {
                latest[record.Id] = record;
            }
        }

        return latest;
    }

    private static Dictionary<Guid, DateTimeOffset> DeletedInList(TaskLists lists)
    {
        return lists.Log
            .Select(item => item.Entry)
            .Where(entry => entry.IsLive && entry.Kind == TaskHistoryKind.Deleted)
            .GroupBy(entry => entry.Task.Id)
            .ToDictionary(group => group.Key, group => group.Max(entry => entry.When));
    }

    // The tasks finished in the one task list: the live tasks with a done time first, then History's finished entries (a task that
    // is live and open again has no finished entry that counts).
    private static Dictionary<Guid, (TaskItem Task, DateTimeOffset At)> FinishedInList(TaskLists lists)
    {
        var finished = new Dictionary<Guid, (TaskItem Task, DateTimeOffset At)>();
        HashSet<Guid> liveOpen = lists.Tasks.Where(item => item.Task.IsLive && item.Task.DoneAt is null).Select(item => item.Id).ToHashSet();
        foreach (SyncTaskItem item in lists.Tasks.Where(item => item.Task.IsLive && item.Task.DoneAt is not null))
        {
            finished[item.Id] = (item.Task, item.Task.DoneAt!.Value);
        }

        foreach (TaskHistoryEntry entry in lists.Log.Select(item => item.Entry).Where(entry => entry.IsLive && entry.Kind == TaskHistoryKind.Finished))
        {
            if (!finished.ContainsKey(entry.Task.Id) && !liveOpen.Contains(entry.Task.Id))
            {
                finished[entry.Task.Id] = (entry.Task, entry.Task.DoneAt ?? entry.When);
            }
        }

        return finished;
    }

    private static List<ArchiveDeletedRow> DeletedFromList(TaskLists lists, DateTimeOffset now)
    {
        return lists.Log
            .Select(item => item.Entry)
            .Where(entry => entry.IsLive && entry.Kind == TaskHistoryKind.Deleted && now - entry.When < DeletedListed && entry.Task.Title.Length > 0)
            .Select(entry => new ArchiveDeletedRow(entry.Task.Id, entry.Task.Title, entry.When, ArchiveSource.TaskList, null, entry.Id))
            .ToList();
    }
}
