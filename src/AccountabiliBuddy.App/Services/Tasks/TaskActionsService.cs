using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// The data API of the task actions (ADR 76): move, finish, delete, archive, rename, label, tags, time limit, highlight, steps and
/// reorder, for one task or a selection (bulk), History (Do again, Bring back), and the undo stack of the last
/// <see cref="TaskUndoStack.Capacity"/> actions (Ctrl+Z). A thin layer over <see cref="TaskStore"/>: the rules are pure
/// (<see cref="TaskActions"/>, <see cref="TaskHistoryRules"/>, <see cref="TaskUndoRules"/>), so what they do can be checked without the
/// app. Every action raises <see cref="Toast"/> with its text; the screens show it with its Undo. Moving into This session never goes
/// around the session's cap: it is the session list's own add (<see cref="SessionTasksService.AddToTodayAsync"/>), which says when
/// there is no free place. Nothing here touches session enforcement. Titles in toasts and undo entries are the user's text: shown
/// and kept in memory only, never logged. Use from the UI thread only.
/// </summary>
public sealed class TaskActionsService
{
    private readonly TaskStore _store;
    private readonly SessionTasksService? _session;
    private readonly TimeProvider _time;
    private readonly TaskUndoStack _undo = new();

    // The Recently deleted entries a Bring back took out, kept with its undo entry so undoing it puts them back.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<TaskUndoEntry, List<DeletedItem>> _bringBackEntries = new();

    public TaskActionsService(TaskStore store, SessionTasksService? session, TimeProvider time)
    {
        _store = store;
        _session = session;
        _time = time;
    }

    /// <summary>Raised after every action (and after an undo) with the toast to show.</summary>
    public event EventHandler<TaskToastEventArgs>? Toast;

    public bool CanUndo => _undo.Count > 0;

    /// <summary>The newest undoable action's toast text (what "Undo" would take back), or null.</summary>
    public string? NextUndoText => _undo.Peek?.Text;

    // ---- moving and finishing ----

    /// <summary>
    /// Moves tasks to a bin. This session goes through the session's cap (<see cref="MoveToSessionAsync"/>); Finished today is a tick
    /// (<see cref="FinishAsync"/>); the others keep each task's id, number and everything else.
    /// </summary>
    public async Task<TaskMoveOutcome> MoveAsync(IReadOnlyList<Guid> ids, TaskBin bin)
    {
        if (bin == TaskBin.Session)
        {
            return await MoveToSessionAsync(ids);
        }

        if (bin == TaskBin.Done)
        {
            await FinishAsync(ids);
            return new TaskMoveOutcome(ids.Count, []);
        }

        bool changed = await RunAsync(TaskActionKind.Move, ids.Count, (lists, self, now) => TaskActions.Move(lists, ids, bin, now, self));
        return new TaskMoveOutcome(changed ? ids.Count : 0, []);
    }

    /// <summary>
    /// Moves tasks into This session one by one through the session list's add, which keeps the cap: tasks that find no place are
    /// returned (<see cref="TaskMoveOutcome.NeedsRoom"/>) for the "This session is full" dialog, and given <paramref name="overLimitReason"/>
    /// are added anyway (the reason is kept for the review, as "Add anyway" always did). Not on the undo stack: a place in a session is
    /// the session's to take back.
    /// </summary>
    public async Task<TaskMoveOutcome> MoveToSessionAsync(IReadOnlyList<Guid> ids, string? overLimitReason = null)
    {
        if (_session is null)
        {
            return new TaskMoveOutcome(0, ids);
        }

        int moved = 0;
        var needsRoom = new List<Guid>();
        TaskLists lists = _store.Lists;
        foreach (Guid id in ids.Distinct())
        {
            // Already in This session, or already taken by the running checklist (a second click or drop before the list caught up).
            if (lists.Tasks.FirstOrDefault(item => item.Id == id) is not { Task: { IsLive: true } task } || task.Bin == TaskBin.Session
                || _session.Current.SessionStart is not null && _session.IsInChecklist(id))
            {
                continue;
            }

            // A Queued task of the next session's list (no session runs) is already in that list: only its bin changes, and only
            // when This session has room for its actions (the same cap; "Add anyway" is the session add's own, so it stays there).
            if (task.Bin == TaskBin.Queued && _session.IsInDraft(id))
            {
                if (!_session.HasDraftRoomFor(task.ActionCount))
                {
                    needsRoom.Add(id);
                }
                else if (await _store.ChangeAsync(null, (current, self, now) => TaskActions.PlaceInSession(current, id, now, self).Lists))
                {
                    moved++;
                }

                continue;
            }

            // While no session runs it must still fit This session's own actions (Queued's hold no place), as the Queued path above.
            if (_session.Current.SessionStart is null && !_session.HasDraftRoomFor(task.ActionCount) && overLimitReason is null)
            {
                needsRoom.Add(id);
                continue;
            }

            (AddToTodayResult result, _) = await _session.AddToTodayAsync(SourceOf(task, lists), overLimitReason);
            if (result == AddToTodayResult.Added)
            {
                moved++;
                if (_session.Current.SessionStart is not null)
                {
                    // A running checklist never reconciles the list (that is the draft's): its copy leaves the list here, held for the
                    // session (no History entry), so it is not both in This session and still on the Shelf or in Queued.
                    await _store.TakeBackAsync(_session, id);
                }
            }
            else if (result == AddToTodayResult.AtLimit)
            {
                needsRoom.Add(id);
            }
        }

        if (moved > 0)
        {
            Announce(moved == 1 ? "1 task → This session" : moved + " tasks → This session", canUndo: false);
        }

        return new TaskMoveOutcome(moved, needsRoom);
    }

    /// <summary>
    /// Ticks tasks. One that sits in the session's checklist is ticked there (its steps, the next step taking the slot, "All tasks
    /// done"); the others go to Finished today (a finished Shelf task settles into History).
    /// </summary>
    public async Task<bool> FinishAsync(IReadOnlyList<Guid> ids)
    {
        (List<Guid> inChecklist, List<Guid> inList) = Split(ids);
        foreach (Guid id in inChecklist)
        {
            await _session!.SetTickedAsync(id, true);
        }

        bool changed = inList.Count > 0 && await RunAsync(TaskActionKind.Finish, inList.Count, (lists, self, now) => TaskActions.Finish(lists, inList, now, self));
        return changed || inChecklist.Count > 0;
    }

    /// <summary>Unticks tasks: back to the bin they left.</summary>
    public async Task<bool> ReopenAsync(IReadOnlyList<Guid> ids)
    {
        (List<Guid> inChecklist, List<Guid> inList) = Split(ids);
        foreach (Guid id in inChecklist)
        {
            await _session!.SetTickedAsync(id, false);
        }

        bool changed = inList.Count > 0 && await RunAsync(TaskActionKind.Finish, inList.Count, (lists, self, now) => TaskActions.Reopen(lists, inList, now, self));
        return changed || inChecklist.Count > 0;
    }

    public Task<bool> TickStepAsync(Guid taskId, Guid stepId)
    {
        return RunAsync(TaskActionKind.Finish, 1, (lists, self, now) => TaskActions.TickStep(lists, taskId, stepId, now, self));
    }

    public Task<bool> AddStepAsync(Guid taskId, string title, Guid? afterStepId = null)
    {
        return RunAsync(TaskActionKind.Edit, 1, (lists, self, now) => TaskActions.AddStep(lists, taskId, title, afterStepId, now, self));
    }

    public Task<bool> ReorderAsync(Guid id, Guid? beforeId)
    {
        return RunAsync(TaskActionKind.Move, 1, (lists, self, now) => TaskActions.Reorder(lists, id, beforeId, now, self));
    }

    public Task<bool> RenameStepAsync(Guid taskId, Guid stepId, string title)
    {
        return RunAsync(TaskActionKind.Edit, 1, (lists, self, now) => TaskActions.RenameStep(lists, taskId, stepId, title, now, self));
    }

    public Task<bool> RemoveStepAsync(Guid taskId, Guid stepId)
    {
        return RunAsync(TaskActionKind.Edit, 1, (lists, self, now) => TaskActions.RemoveStep(lists, taskId, stepId, now, self));
    }

    /// <summary>A step dragged (or Alt+↑/↓) before or after another step of its task.</summary>
    public Task<bool> MoveStepAsync(Guid taskId, Guid stepId, Guid targetStepId, bool after)
    {
        return RunAsync(TaskActionKind.Edit, 1, (lists, self, now) => TaskActions.MoveStep(lists, taskId, stepId, targetStepId, after, now, self));
    }

    /// <summary>
    /// Keeps the open task's note. Saved a moment after typing stops, so it is not an undo step of its own (ten of those would push
    /// every real action off the stack) and shows no toast. The note is private text: never logged.
    /// </summary>
    public Task<bool> SetNoteAsync(Guid id, string? note)
    {
        return _store.ChangeAsync(null, (lists, self, now) => TaskActions.SetNote(lists, id, note, now, self).Lists);
    }

    // ---- adding ----

    /// <summary>
    /// A new task from the add box or Jot it down (<see cref="TaskActions.Add"/>), on the undo stack (undo deletes it). This session is
    /// refused there: the add box adds to Queued and then calls <see cref="MoveToSessionAsync"/>, so the cap decides.
    /// </summary>
    public Task<bool> AddAsync(Guid id, string title, TaskBin bin, Guid? labelId, IReadOnlyList<string>? tags, string? note, string? origin = null)
    {
        return RunAsync(TaskActionKind.Add, 1, (lists, self, now) => TaskActions.Add(lists, id, title, bin, labelId, tags, note, origin, now, self));
    }

    // ---- removing ----

    public Task<bool> DeleteAsync(IReadOnlyList<Guid> ids)
    {
        return RunAsync(TaskActionKind.Delete, ids.Count, (lists, self, now) => TaskActions.Delete(lists, ids, now, self));
    }

    public Task<bool> ArchiveAsync(IReadOnlyList<Guid> ids)
    {
        return RunAsync(TaskActionKind.Delete, ids.Count, (lists, self, now) => TaskActions.Archive(lists, ids, now, self));
    }

    // ---- editing (one task or a selection) ----

    public Task<bool> RenameAsync(Guid id, string title)
    {
        return RunAsync(TaskActionKind.Edit, 1, (lists, self, now) => TaskActions.Rename(lists, id, title, now, self));
    }

    /// <summary>"Rename one by one": nothing is saved or synced until this is called with every new name.</summary>
    public Task<bool> RenameEachAsync(IReadOnlyDictionary<Guid, string> titles)
    {
        return RunAsync(TaskActionKind.Bulk, titles.Count, (lists, self, now) => TaskActions.RenameEach(lists, titles, now, self));
    }

    // ---- "Changed on both devices" (spec 05 §11) ----

    /// <summary>
    /// Keeps one version of a title or note two devices changed (<see cref="TaskActions.ResolveConflict"/>). Not on the undo stack: an
    /// undo would bring the old text back without the choice, so the other version would be lost.
    /// </summary>
    public Task<bool> ResolveConflictAsync(Guid id, TaskConflict conflict, bool keepOther)
    {
        return RunUnrecordedAsync((lists, self, now) => TaskActions.ResolveConflict(lists, id, conflict, keepOther, now, self));
    }

    /// <summary>"Keep both as separate tasks" (<see cref="TaskActions.KeepBothVersions"/>): the other version goes to Queued. Not on the undo stack either.</summary>
    public Task<bool> KeepBothVersionsAsync(Guid id, TaskConflict conflict)
    {
        Guid copyId = Guid.NewGuid();
        return RunUnrecordedAsync((lists, self, now) => TaskActions.KeepBothVersions(lists, id, conflict, copyId, now, self));
    }

    public Task<bool> SetLabelAsync(IReadOnlyList<Guid> ids, Guid? labelId)
    {
        return RunAsync(TaskActionKind.Edit, ids.Count, (lists, self, now) => TaskActions.SetLabel(lists, ids, labelId, now, self));
    }

    public Task<bool> AddTagsAsync(IReadOnlyList<Guid> ids, IReadOnlyList<string> tags)
    {
        return RunAsync(TaskActionKind.Edit, ids.Count, (lists, self, now) => TaskActions.AddTags(lists, ids, tags, now, self));
    }

    public Task<bool> RemoveTagsAsync(IReadOnlyList<Guid> ids, IReadOnlyList<string> tags)
    {
        return RunAsync(TaskActionKind.Edit, ids.Count, (lists, self, now) => TaskActions.RemoveTags(lists, ids, tags, now, self));
    }

    public Task<bool> ToggleTagAsync(IReadOnlyList<Guid> ids, string tag)
    {
        return RunAsync(TaskActionKind.Edit, ids.Count, (lists, self, now) => TaskActions.ToggleTag(lists, ids, tag, now, self));
    }

    public Task<bool> SetTimeLimitAsync(IReadOnlyList<Guid> ids, int? minutes)
    {
        return RunAsync(TaskActionKind.Edit, ids.Count, (lists, self, now) => TaskActions.SetTimeLimit(lists, ids, minutes, now, self));
    }

    public Task<bool> SetHighlightAsync(IReadOnlyList<Guid> ids, bool on)
    {
        return RunAsync(TaskActionKind.Edit, ids.Count, (lists, self, now) => TaskActions.SetHighlight(lists, ids, on, now, self));
    }

    public Task<bool> ToggleHighlightAsync(IReadOnlyList<Guid> ids)
    {
        return RunAsync(TaskActionKind.Edit, ids.Count, (lists, self, now) => TaskActions.ToggleHighlight(lists, ids, now, self));
    }

    // ---- Reminders & due date (the menu of the same name; <paramref name="destination"/> = the "Add new reminders to" setting) ----

    public Task<bool> AddReminderAsync(Guid id, string rule, DateTimeOffset? at, TaskCalendarDestination destination)
    {
        Guid reminderId = Guid.NewGuid();
        return RunAsync(TaskActionKind.Edit, 1, (lists, self, now) => TaskActions.AddReminder(lists, id, reminderId, rule, at, destination, now, self));
    }

    public Task<bool> RemoveReminderAsync(Guid id, Guid reminderId)
    {
        return RunAsync(TaskActionKind.Edit, 1, (lists, self, now) => TaskActions.RemoveReminder(lists, id, reminderId, now, self));
    }

    /// <summary>"Done by" a date (with a time, or the whole day); null removes it.</summary>
    public Task<bool> SetDueAsync(IReadOnlyList<Guid> ids, DateOnly? on, TimeOnly? at, TaskCalendarDestination destination)
    {
        return RunAsync(TaskActionKind.Edit, ids.Count, (lists, self, now) => TaskActions.SetDue(lists, ids, on, at, destination, now, self));
    }

    public Task<bool> SetCantMissAsync(IReadOnlyList<Guid> ids, bool on)
    {
        return RunAsync(TaskActionKind.Edit, ids.Count, (lists, self, now) => TaskActions.SetCantMiss(lists, ids, on, now, self));
    }

    /// <summary>The task's own "Add to Google Calendar" switch (ADR 78).</summary>
    public Task<bool> SetCalendarAsync(IReadOnlyList<Guid> ids, bool on)
    {
        return RunAsync(TaskActionKind.Edit, ids.Count, (lists, self, now) => TaskActions.SetCalendar(lists, ids, on, now, self));
    }

    /// <summary>
    /// A task was opened (its details shown): it counts as touched, which resets its age, unless it was touched within the last hour. Only the touch time moves: no toast, no undo.
    /// The task's other views are told, this one (<paramref name="source"/>) is not.
    /// </summary>
    public async Task OpenedAsync(Guid id, object? source = null)
    {
        // Opening again within the hour doesn't touch again (it would only make the task sync every time it is looked at).
        if (_store.Lists.Tasks.FirstOrDefault(item => item.Id == id) is { Task.IsLive: true } held && TaskUntouched.TouchedRecently(held.Task, _time.GetUtcNow()))
        {
            return;
        }

        await _store.ChangeAsync(source, (lists, _, now) => TaskActions.Touch(lists, id, now).Lists);
    }

    // ---- History ----

    /// <summary>History as listed: the last 30 days, finished (Do again) and archived or deleted (Bring back).</summary>
    public TaskHistoryList History()
    {
        return TaskHistoryRules.List(_store.Lists, _time.GetUtcNow());
    }

    /// <summary>"Do again": a new task in Queued from a finished one (new id and number, origin noted). The new task's id; null when the entry is gone.</summary>
    public async Task<Guid?> DoAgainAsync(Guid entryId)
    {
        Guid? made = null;
        await RunAsync(TaskActionKind.History, 1, (lists, self, now) =>
        {
            TaskActionResult result = TaskHistoryRules.DoAgain(lists, entryId, now, self);
            made = result.Changed ? result.Affected[0] : null;
            return result;
        });
        return made;
    }

    /// <summary>
    /// "Bring back": an archived or deleted task returns to its last bin under its own id and number. The same task's entry in Recently
    /// deleted (ADR 50), if it has one, goes with it, so it can't be restored twice.
    /// </summary>
    public async Task<bool> BringBackAsync(Guid entryId)
    {
        Guid? taskId = _store.Lists.Log.FirstOrDefault(item => item.Id == entryId)?.Entry.Task.Id;
        bool changed = await RunAsync(TaskActionKind.History, 1, (lists, self, now) => TaskHistoryRules.BringBack(lists, entryId, now, self));
        if (changed && taskId is Guid id && _session?.Deleted is { } deleted)
        {
            List<DeletedItem> removed = deleted.Items.Where(item => item.ItemId == id && item.Kind is DeletedKind.Task or DeletedKind.Later or DeletedKind.ComeBack or DeletedKind.AutoSaved).ToList();
            foreach (DeletedItem item in removed)
            {
                await deleted.RemoveAsync(item.EntryId);
            }

            if (removed.Count > 0 && _undo.Peek is { } entry)
            {
                _bringBackEntries.AddOrUpdate(entry, removed);
            }
        }

        return changed;
    }

    // ---- undo ----

    /// <summary>
    /// Ctrl+Z: puts back what the newest action touched, exactly as it was. A task that was in This session comes back to Queued and is
    /// offered to the session's own add (the cap rules): when it finds no room it stays in Queued (<see cref="TaskUndoOutcome.QueuedNoRoom"/>,
    /// toasted "No room in this session · put in Queued"). A task the session holds now, one a newer History entry has, or one that went
    /// another way since is left alone (<see cref="TaskUndoOutcome.ChangedSince"/>, toasted "Can't undo · it changed since").
    /// </summary>
    public async Task<TaskUndoOutcome> UndoAsync()
    {
        if (!_undo.TryPop(out TaskUndoEntry? popped) || popped is null)
        {
            return new TaskUndoOutcome(false, [], []);
        }

        // What changed since (the session holds it, a newer History entry has it, it went another way) is left out, and said so.
        IReadOnlyList<Guid> sessionIds = [];
        IReadOnlyList<Guid> skipped = [];
        await _store.ChangeAsync(null, (lists, self, now) =>
        {
            (TaskUndoEntry entry, sessionIds, skipped) = TaskUndoRules.ForRestore(popped, lists, id => _session?.IsInChecklist(id) == true || _store.IsHeld(id));
            return TaskUndoRules.Restore(lists, entry, self, now);
        });
        // Undoing a Bring back puts its Recently deleted entries back too (the task is gone again, so they can restore it), but not those
        // of a task left alone because it changed since: it is live or in History, and a second Bring back would duplicate it.
        if (_bringBackEntries.TryGetValue(popped, out List<DeletedItem>? removedByBringBack) && _session?.Deleted is { } deletedStore)
        {
            foreach (DeletedItem item in TaskUndoRules.ToReinstate(removedByBringBack, item => item.ItemId, skipped))
            {
                await deletedStore.ReinstateAsync(item);
            }
        }

        int touched = popped.Tasks.Keys.Union(popped.Log.Keys).Count();
        if (skipped.Count > 0 && skipped.Count >= touched)
        {
            Announce(TaskToasts.ChangedSince, canUndo: false);
            return new TaskUndoOutcome(false, [], skipped);
        }

        List<Guid> noRoom = [];
        if (sessionIds.Count > 0)
        {
            TaskMoveOutcome outcome = await MoveToSessionAsync(sessionIds);
            noRoom.AddRange(_session is null ? sessionIds : outcome.NeedsRoom);
        }

        Announce(noRoom.Count > 0 ? "No room in this session · put in Queued" : skipped.Count > 0 ? TaskToasts.PartlyChangedSince : TaskToasts.Undone, canUndo: false);
        return new TaskUndoOutcome(true, noRoom, skipped);
    }

    /// <summary>Shows a toast for something another service did (the tidy-up's "Tidied 4 of 5 tasks").</summary>
    public void Announce(string text, bool canUndo)
    {
        Toast?.Invoke(this, new TaskToastEventArgs(text, canUndo));
    }

    // Runs a pure action over the lists; when it changed something, keeps it on the undo stack and raises its toast.
    private async Task<bool> RunAsync(TaskActionKind kind, int count, Func<TaskLists, Guid, DateTimeOffset, TaskActionResult> action)
    {
        TaskLists? before = null;
        TaskActionResult? result = null;
        DateTimeOffset at = default;
        bool changed = await _store.ChangeAsync(null, (lists, self, now) =>
        {
            before = lists;
            at = now;
            result = action(lists, self, now);
            return result.Lists;
        });
        if (!changed || result is not { Changed: true } || before is null)
        {
            return false;
        }

        TaskActionKind recorded = count > 1 && kind is TaskActionKind.Edit or TaskActionKind.Delete or TaskActionKind.Move ? TaskActionKind.Bulk : kind;
        _undo.Push(TaskUndoRules.Capture(before, result.Affected, recorded, result.Toast, result.Lists, at));
        Announce(result.Toast, canUndo: true);
        return true;
    }

    // Runs a pure action over the lists and raises its toast, without an undo entry.
    private async Task<bool> RunUnrecordedAsync(Func<TaskLists, Guid, DateTimeOffset, TaskActionResult> action)
    {
        TaskActionResult? result = null;
        bool changed = await _store.ChangeAsync(null, (lists, self, now) =>
        {
            result = action(lists, self, now);
            return result.Lists;
        });
        if (!changed || result is not { Changed: true })
        {
            return false;
        }

        Announce(result.Toast, canUndo: false);
        return true;
    }

    // The ids that sit in the session's checklist (ticked there) and the rest (ticked in the list).
    private (List<Guid> InChecklist, List<Guid> InList) Split(IReadOnlyList<Guid> ids)
    {
        List<Guid> inChecklist = _session is null ? [] : ids.Where(id => _session.IsInChecklist(id)).ToList();
        return (inChecklist, ids.Except(inChecklist).ToList());
    }

    // What the session's add takes from a task: its id and its steps' ids and ticks stay (a bin change of the same task).
    private static TodaySource SourceOf(TaskItem task, TaskLists lists)
    {
        TaskLabel? label = task.LabelId is Guid id ? lists.Labels.Select(item => item.Label).FirstOrDefault(item => item.IsLive && item.Id == id) : null;
        return new TodaySource(
            task.Title, (task.Steps ?? []).Select(step => step.ToSubtask()).ToList(), label?.Name, label?.Color, task.EstimateMinutes, task.Note, task.Tags, task.Id);
    }
}
