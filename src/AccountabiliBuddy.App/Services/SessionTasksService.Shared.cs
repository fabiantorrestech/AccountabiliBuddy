using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services;

// ADR 72: this PC's task history for the sync file, other devices' settle records applied to it, labels, and local items.
public sealed partial class SessionTasksService
{
    /// <summary>The next session's list for a task-list backup (ADR 71): as <see cref="NextTasksForSync"/>, with the local tasks kept here in full.</summary>
    public IReadOnlyList<SyncNextTask> NextTasksForBackup()
    {
        DateTimeOffset keptFrom = _time.GetUtcNow() - SharedListMerge.TombstonesKept;
        return _nextTasks.Where(task => task.DeletedAt is not DateTimeOffset deletedAt || deletedAt > keptFrom || task.LocalTo is not null).OrderBy(task => task.Position).ToList();
    }

    /// <summary>The past tasks older versions kept as "moved" on this PC only, for <see cref="PastSettledStore.ImportMovedAsync"/>.</summary>
    public IReadOnlyList<(Guid Id, DateTimeOffset At)> LegacyMovedEntries()
    {
        return _movedToLater.Select(entry => (entry.Id, entry.At)).ToList();
    }

    /// <summary>
    /// This PC's 90-day task history as its sync file carries it (ADR 72): each ended session's checklist and each "done outside a
    /// session" record, with category names and colours filled in. A task kept on one device goes as its stub. Session notes and
    /// reasons are never part of a checklist, so they never travel.
    /// </summary>
    public IReadOnlyList<SyncTaskHistoryEntry> TaskHistoryForSync()
    {
        DateTimeOffset from = _time.GetUtcNow() - SyncTaskHistoryEntry.Kept;
        return _history
            .Where(checklist => KeptInHistory(checklist, from))
            .Select(checklist => new SyncTaskHistoryEntry(
                checklist.OutsideSession ? null : checklist.SessionStart,
                checklist.Labelled(_categories).Select(task => task.LocalTo is Guid keeper ? task.AsLocalStub(keeper) : task with { CategoryId = null }).ToList(),
                checklist.OutsideSession))
            .Where(entry => entry.Tasks.Count > 0)
            .ToList();
    }

    /// <summary>
    /// Applies other devices' settle records to this PC's own history (ADR 72): a tick, an untick, a delete, or a task now kept on
    /// one device. Records about tasks that aren't in this PC's history change nothing here.
    /// </summary>
    public async Task ApplySettledAsync(IReadOnlyList<SyncPastSettled> records)
    {
        Guid? self = SelfId();
        foreach (SyncPastSettled record in records)
        {
            SessionTask? task = _history.Where(checklist => !checklist.OutsideSession).SelectMany(checklist => checklist.Tasks).FirstOrDefault(candidate => candidate.Id == record.Id);
            if (task is null)
            {
                // AR1b: another device brought back a past task this PC's history lost to an earlier delete: it goes back from this PC's own
                // Recently deleted copy (never from the record, which carries no text).
                if (record.State is SyncPastSettled.Open or SyncPastSettled.Done)
                {
                    await ReviveDeletedPastTaskAsync(record);
                }

                continue;
            }

            switch (record.State)
            {
                case SyncPastSettled.Done when !task.IsDone:
                    await SetHistoryDoneAsync(record.Id, true);
                    break;
                case SyncPastSettled.Open when task.IsDone && task.LocalTo is null:
                    await SetHistoryDoneAsync(record.Id, false);
                    break;
                case SyncPastSettled.Deleted when task.Title.Length > 0:
                    await EditHistoryAsync(record.Id, null);
                    break;
                case SyncPastSettled.Local when record.LocalTo is Guid keeper:
                    await ChangeHistoryAsync(held => held.Id != record.Id ? held : keeper == self ? held with { LocalTo = keeper } : held.AsLocalStub(keeper));
                    break;
            }
        }
    }

    // The past task of this PC's own history that was deleted here (or by another device's record) and is now settled Open or Done by a newer
    // record: put back in its session, as that record says. The record already settles it on every device, so nothing is written back.
    private async Task ReviveDeletedPastTaskAsync(SyncPastSettled record)
    {
        DeletedItem? entry = Deleted?.Items.FirstOrDefault(item => item.Kind == DeletedKind.HistoryTask && item.ItemId == record.Id && item.Task is not null);
        if (entry?.Task is null || Deleted is null)
        {
            return;
        }

        int at = HistoryChecklistIndex(entry);
        if (at < 0 || _history[at].OutsideSession)
        {
            return;
        }

        SessionTask back = record.State == SyncPastSettled.Done
            ? entry.Task.DoneAt is null ? entry.Task with { DoneAt = record.EditedAt } : entry.Task
            : entry.Task with { DoneAt = null };
        _history = _history.Select((checklist, index) => index == at ? checklist.Insert(back, entry.Index) : checklist).ToList();
        await SaveHistoryAsync();
        await Deleted.RemoveAsync(entry.EntryId);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// "Restore" of a past task deleted on any device that this PC doesn't hold itself (AR1b): a newer settle record (Done, or Open) over the
    /// delete, so the PC whose history held it puts it back and every device lists it again. Never a raw restore.
    /// </summary>
    /// <returns>False when there are no settle records here.</returns>
    public async Task<bool> RestorePastByStampAsync(Guid id, string state)
    {
        if (PastSettled is null || state is not (SyncPastSettled.Done or SyncPastSettled.Open))
        {
            return false;
        }

        await PastSettled.SetAsync(id, state);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Keeps a task of this PC's history on <paramref name="keeper"/> as a local item (ADR 72), or makes it synced again with null.
    /// Kept elsewhere, only its stub stays in the record here; the caller hands the returned full task over first.
    /// </summary>
    /// <returns>The task as it was, for the handover; null when it isn't in this PC's history.</returns>
    public async Task<SessionTask?> SetHistoryLocalAsync(Guid taskId, Guid? keeper)
    {
        SessionTask? task = HistoryTaskOf(taskId);
        if (task is null || task.Title.Length == 0)
        {
            return null;
        }

        Guid? self = SelfId();
        await ChangeHistoryAsync(held => held.Id != taskId ? held : keeper is Guid other && other != self ? held.AsLocalStub(other) : held with { LocalTo = keeper });
        if (PastSettled is not null)
        {
            await PastSettled.SetAsync(taskId, keeper is null ? SyncPastSettled.Open : SyncPastSettled.Local, keeper);
        }

        return task;
    }

    /// <summary>A task of this PC's history handed back from the device that kept it (ADR 72): the stub gets its text back, synced again.</summary>
    public Task RestoreHistoryTaskAsync(SessionTask task)
    {
        return ChangeHistoryAsync(held => held.Id == task.Id ? task with { LocalTo = null } : held);
    }

    /// <summary>The checklist start of history task <paramref name="taskId"/> (null for a "done outside a session" record or none).</summary>
    public DateTimeOffset? HistorySessionStartOf(Guid taskId)
    {
        return _history.FirstOrDefault(checklist => checklist.Tasks.Any(task => task.Id == taskId))?.SessionStart;
    }

    /// <summary>Sets the labels of a task (ADR 72), wherever it is: the draft, the running checklist or an ended session's record.</summary>
    public Task SetLabelsAsync(Guid taskId, IReadOnlyList<string>? labels)
    {
        if (Current.Tasks.Any(task => task.Id == taskId))
        {
            return SetAsync(Current.SetLabels(taskId, labels));
        }

        return ChangeHistoryAsync(task => task.Id == taskId ? task with { Labels = TaskLabels.Clean(labels) } : task);
    }

    /// <summary>
    /// Renames (blank <paramref name="to"/>: deletes) label <paramref name="from"/> on every task and list item that has it (ADR 72);
    /// each change is a stamped edit, so it reaches the other devices.
    /// </summary>
    public async Task RenameLabelAsync(string from, string to)
    {
        if (Current.Tasks.Any(task => TaskLabels.Has(task.Labels, from)))
        {
            await SetAsync(Current.RenameLabel(from, to));
        }

        if (_history.Any(checklist => checklist.Tasks.Any(task => TaskLabels.Has(task.Labels, from))))
        {
            await ChangeHistoryAsync(task => TaskLabels.Has(task.Labels, from) ? task with { Labels = TaskLabels.Renamed(task.Labels, from, to) } : task);
        }

        if (Later is not null)
        {
            await Later.RenameLabelAsync(from, to);
        }

        if (Comebacks is not null)
        {
            await Comebacks.RenameLabelAsync(from, to);
        }

        if (AutoSaved is not null)
        {
            await AutoSaved.RenameLabelAsync(from, to);
        }
    }

    /// <summary>Every label on this device's tasks and lists (ADR 72), once each, by name.</summary>
    public IReadOnlyList<string> AllLabels(IEnumerable<IReadOnlyList<string>?>? more = null)
    {
        IEnumerable<IReadOnlyList<string>?> lists = Current.Tasks.Select(task => task.Labels)
            .Concat(_nextTasks.Where(task => task.DeletedAt is null).Select(task => task.Labels))
            .Concat(_history.SelectMany(checklist => checklist.Tasks).Select(task => task.Labels))
            .Concat(Later?.Items.Select(item => item.Labels) ?? [])
            .Concat(Comebacks?.All.Select(comeback => comeback.Labels) ?? [])
            .Concat(AutoSaved?.Items.Select(item => item.Labels) ?? [])
            .Concat(more ?? []);
        return TaskLabels.AllOf(lists);
    }

    /// <summary>Whether a task is in the next session's list now (no session running), so it can be made local.</summary>
    public bool IsDraftTask(Guid taskId)
    {
        return Current.SessionStart is null && Current.Tasks.Any(task => task.Id == taskId);
    }

    /// <summary>
    /// Keeps a task of the next session's list on <paramref name="keeper"/> as a local item (ADR 72), or makes it synced again with
    /// null. Never during a session: the running checklist belongs to the session. Kept elsewhere, it leaves the list here (only its
    /// stub stays); the caller hands the returned item over first.
    /// </summary>
    /// <returns>The task as the shared list now holds it, for the handover; null when it isn't in the draft.</returns>
    public async Task<SyncNextTask?> SetNextTaskLocalAsync(Guid taskId, Guid? keeper)
    {
        if (!IsDraftTask(taskId))
        {
            return null;
        }

        await SetAsync(Current.SetLocal(taskId, keeper));
        SyncNextTask? item = _nextTasks.FirstOrDefault(task => task.Id == taskId && task.DeletedAt is null);
        if (item is null)
        {
            return null;
        }

        if (keeper is Guid other && other != SelfId())
        {
            _nextTasks = _nextTasks.Select(task => task.Id == taskId ? (SyncNextTask)task.AsLocalStub() : task).ToList();
            await SaveNextTasksAsync();
            await ShowSharedDraftAsync();
        }

        return item;
    }

    /// <summary>Takes in a next-session task handed over to this device to keep (ADR 72): it replaces any stub or older copy.</summary>
    public async Task AdoptNextTaskAsync(SyncNextTask item)
    {
        SyncNextTask? held = _nextTasks.FirstOrDefault(task => task.Id == item.Id);
        if (held is not null && held.DeletedAt is null && held.EditedAt > item.EditedAt)
        {
            return;
        }

        _nextTasks = _nextTasks.Where(task => task.Id != item.Id).Append(item with { DeletedAt = null }).OrderBy(task => task.Position).ToList();
        await SaveNextTasksAsync();
        if (Current.SessionStart is null)
        {
            await ShowSharedDraftAsync();
        }
        else
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Other PCs' past tasks (ADR 72: their open ones of the last two weeks, not settled) and the past tasks kept here as local items,
    /// for "Unfinished"; set by the app. Each carries the PC it came from.
    /// </summary>
    public Func<IReadOnlyList<PastTask>>? OtherPastUnfinished { get; set; }

    /// <summary>A task of another PC's history, or one kept here (ADR 72), by id; set by the app.</summary>
    public Func<Guid, SessionTask?>? ForeignTaskOf { get; set; }

    /// <summary>The past tasks of other PCs kept on this device as local items (ADR 72); set by the app.</summary>
    public KeptPastStore? Kept { get; set; }

    /// <summary>A past task by id: this PC's history first, then another PC's or one kept here (ADR 72).</summary>
    public SessionTask? PastTaskOf(Guid id)
    {
        return HistoryTaskOf(id) ?? ForeignTaskOf?.Invoke(id);
    }

    /// <summary>
    /// Deletes a past task that isn't in this PC's history (ADR 72): one kept here goes from here only; another PC's is recorded
    /// deleted, so it goes from every device and that PC's Recently deleted gets it.
    /// </summary>
    /// <returns>False when it isn't such a task.</returns>
    public async Task<bool> DeleteForeignPastAsync(Guid id)
    {
        if (Kept?.Find(id) is not null)
        {
            await Kept.RemoveAsync(id);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (IsOwnPastTask(id) || ForeignTaskOf?.Invoke(id) is not SessionTask foreign || PastSettled is null)
        {
            return false;
        }

        // The other PC's next file no longer carries the task, so this PC keeps its own copy for Recently deleted (AR1-fix): the delete is listed here too. Its restore only settles it again (never this PC's history).
        await RecordAsync(new DeletedItem(Guid.NewGuid(), _time.GetUtcNow(), DeletedKind.ForeignPastTask, foreign.Title, Task: foreign));
        await PastSettled.SetAsync(id, SyncPastSettled.Deleted);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Raises <see cref="Changed"/> when a list this service shows changed elsewhere (other PCs' history, kept tasks, settle records).</summary>
    public void NotifyListsChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // A task of an ended session in this PC's own history (not a "done outside a session" record).
    private bool IsOwnPastTask(Guid id)
    {
        return _history.Any(checklist => !checklist.OutsideSession && checklist.Tasks.Any(task => task.Id == id));
    }

    // A past task moved, ticked, deleted or kept elsewhere (ADR 72's shared record) no longer shows as unfinished.
    private bool WasMovedOff(Guid taskId)
    {
        return PastSettled?.Hides(taskId) == true;
    }
}
