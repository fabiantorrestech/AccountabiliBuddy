using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Delete and Undo (ADR 50). Whatever the user deletes (a task, a step, a Later item, a come-back, in the checklist or in an
/// ended session's record) is kept with its content in <see cref="RecentlyDeletedStore"/> for 30 days, on this PC only and
/// sealed, so it can come back as it was: same id, place, steps, note, estimate and category. For the shared lists a restore
/// is a normal newer edit that beats the tombstone. Undo during a running session counts the "taken off open" back only when
/// the task comes back open into the same checklist (ADR 25), and never past the three-task rule.
/// </summary>
public sealed partial class SessionTasksService
{
    /// <summary>
    /// Recently deleted, set once the store exists. Null: a delete cannot be undone.
    /// </summary>
    public RecentlyDeletedStore? Deleted { get; set; }

    /// <summary>The Auto-saved list, set once the store exists (it is built after this service). Null: no Auto-saved items.</summary>
    public AutoSavedTaskStore? AutoSaved { get; set; }

    /// <summary>Deletes a Later item; it stays in Recently deleted for 30 days.</summary>
    public async Task DeleteLaterAsync(Guid id)
    {
        SyncLaterItem? item = Later?.Items.FirstOrDefault(candidate => candidate.Id == id);
        if (Later is null || item is null)
        {
            return;
        }

        await RecordAsync(new DeletedItem(Guid.NewGuid(), _time.GetUtcNow(), DeletedKind.Later, item.Title, Later: item));
        await Later.DeleteAsync(id);
    }

    /// <summary>Deletes an Auto-saved item; it stays in Recently deleted for 30 days.</summary>
    public async Task DeleteAutoSavedAsync(Guid id)
    {
        AutoSavedTask? item = AutoSaved?.Items.FirstOrDefault(candidate => candidate.Id == id);
        if (AutoSaved is null || item is null)
        {
            return;
        }

        await RecordAsync(new DeletedItem(Guid.NewGuid(), _time.GetUtcNow(), DeletedKind.AutoSaved, item.Title, AutoSaved: item));
        await AutoSaved.RemoveAsync(id);
    }

    /// <summary>Deletes a step of a Later item; it stays in Recently deleted for 30 days.</summary>
    public async Task DeleteLaterStepAsync(Guid laterId, Guid stepId)
    {
        SyncLaterItem? item = Later?.Items.FirstOrDefault(candidate => candidate.Id == laterId);
        List<SessionSubtask> steps = item?.Subtasks?.ToList() ?? [];
        int index = steps.FindIndex(step => step.Id == stepId);
        if (Later is null || item is null || index < 0)
        {
            return;
        }

        SessionSubtask step = steps[index];
        await RecordAsync(new DeletedItem(Guid.NewGuid(), _time.GetUtcNow(), DeletedKind.LaterStep, step.Title, Step: step, ParentId: laterId, Index: index));
        steps.RemoveAt(index);
        await Later.EditAsync(laterId, held => held with { Subtasks = steps.Count > 0 ? steps : null });
    }

    /// <summary>Deletes a come-back, open or ticked; it stays in Recently deleted for 30 days.</summary>
    public async Task DeleteComebackAsync(Guid id)
    {
        Comeback? comeback = Comebacks?.All.FirstOrDefault(candidate => candidate.Id == id);
        if (Comebacks is null || comeback is null)
        {
            return;
        }

        await RecordAsync(new DeletedItem(Guid.NewGuid(), _time.GetUtcNow(), DeletedKind.ComeBack, comeback.Text, ComeBack: comeback));
        await Comebacks.DeleteAsync(id);
    }

    /// <summary>Brings a deleted item back as it was. False with the reason when it can't come back now; the entry then stays.</summary>
    public async Task<(bool Restored, string? Message)> RestoreAsync(Guid entryId)
    {
        if (Deleted?.Find(entryId) is not DeletedItem entry)
        {
            return (false, "That item is no longer in Recently deleted.");
        }

        (bool Restored, string? Message) outcome = entry.Kind switch
        {
            DeletedKind.Later when Later is not null => await RestoreLaterAsync(entry),
            DeletedKind.ComeBack when Comebacks is not null => await RestoreComebackAsync(entry),
            DeletedKind.Task => await RestoreTaskAsync(entry),
            DeletedKind.Step => await RestoreStepAsync(entry),
            DeletedKind.AutoSaved when AutoSaved is not null => await RestoreAutoSavedAsync(entry),
            DeletedKind.LaterStep when Later is not null => await RestoreLaterStepAsync(entry),
            DeletedKind.HistoryTask => await RestoreHistoryTaskAsync(entry),
            DeletedKind.HistoryStep => await RestoreHistoryStepAsync(entry),
            _ => (false, "That can't be restored here."),
        };
        if (outcome.Restored)
        {
            await Deleted.RemoveAsync(entryId);
        }

        return outcome;
    }

    private async Task<(bool, string?)> RestoreLaterAsync(DeletedItem entry)
    {
        await Later!.RestoreAsync(entry.Later!);
        return (true, null);
    }

    private async Task<(bool, string?)> RestoreComebackAsync(DeletedItem entry)
    {
        await Comebacks!.RestoreAsync(entry.ComeBack!);
        return (true, null);
    }

    private async Task<(bool, string?)> RestoreTaskAsync(DeletedItem entry)
    {
        SessionTask task = entry.Task!;
        if (!InSameChecklist(entry))
        {
            // The list it left is gone (that session ended, or a session started since): it comes back as a new task today.
            (AddToTodayResult result, _) = await AddToTodayAsync(SourceOfTask(task));
            return result == AddToTodayResult.Added ? (true, null) : (false, $"Today already has {MaxTasks} tasks. Take one off first.");
        }

        if (!_sessions.State.IsTest && Current.Tasks.Count >= TaskAllowance)
        {
            return (false, $"This session already has {TaskAllowance} tasks.");
        }

        Guid? categoryId = await CategoryIdOfAsync(task);
        SessionChecklist restored = Current.Insert(task with { CategoryId = categoryId, Category = null, CategoryColor = null }, entry.Index);
        if (ReferenceEquals(restored, Current))
        {
            return (true, null); // Already back (restored twice).
        }

        // ADR 25: an open task that comes back into the session it was taken off from is no longer "taken off open".
        if (entry.WasLive && IsLive && !task.IsDone)
        {
            restored = restored.WithOneLessDropped();
        }

        await SetAsync(restored);
        return (true, null);
    }

    private async Task<(bool, string?)> RestoreAutoSavedAsync(DeletedItem entry)
    {
        await AutoSaved!.RestoreAsync(entry.AutoSaved!);
        return (true, null);
    }

    private async Task<(bool, string?)> RestoreLaterStepAsync(DeletedItem entry)
    {
        if (entry.ParentId is not Guid parent || Later!.Items.All(item => item.Id != parent))
        {
            return (false, "The task it belonged to is gone.");
        }

        await Later.EditAsync(parent, held =>
        {
            List<SessionSubtask> steps = held.Subtasks?.ToList() ?? [];
            if (steps.Any(step => step.Id == entry.Step!.Id))
            {
                return held; // Already back (restored twice).
            }

            steps.Insert(Math.Clamp(entry.Index, 0, steps.Count), entry.Step!);
            return held with { Subtasks = steps };
        });
        return (true, null);
    }

    private async Task<(bool, string?)> RestoreStepAsync(DeletedItem entry)
    {
        if (entry.ParentId is not Guid parent || Current.Tasks.All(task => task.Id != parent))
        {
            return (false, "The task it belonged to is gone.");
        }

        await SetAsync(Current.InsertSubtask(parent, entry.Step!, entry.Index));
        return (true, null);
    }

    private async Task<(bool, string?)> RestoreHistoryTaskAsync(DeletedItem entry)
    {
        int at = HistoryChecklistIndex(entry);
        if (at < 0)
        {
            (AddToTodayResult result, _) = await AddToTodayAsync(SourceOfTask(entry.Task!));
            return result == AddToTodayResult.Added ? (true, null) : (false, $"Today already has {MaxTasks} tasks. Take one off first.");
        }

        _history = _history.Select((checklist, index) => index == at ? checklist.Insert(entry.Task!, entry.Index) : checklist).ToList();
        await SaveHistoryAsync();
        return (true, null);
    }

    private async Task<(bool, string?)> RestoreHistoryStepAsync(DeletedItem entry)
    {
        int at = _history.FindIndex(checklist => checklist.Tasks.Any(task => task.Id == entry.ParentId));
        if (at < 0)
        {
            return (false, "The task it belonged to is gone.");
        }

        _history = _history.Select((checklist, index) => index == at && entry.ParentId is Guid parent ? checklist.InsertSubtask(parent, entry.Step!, entry.Index) : checklist).ToList();
        await SaveHistoryAsync();
        return (true, null);
    }

    // ---- Recording ----

    private async Task RecordCurrentDeletionAsync(Guid id)
    {
        if (Deleted is null)
        {
            return;
        }

        IReadOnlyList<SessionTask> labelled = Labelled(Current);
        DateTimeOffset now = _time.GetUtcNow();
        DateTimeOffset? start = Current.SessionStart;
        int taskIndex = labelled.ToList().FindIndex(task => task.Id == id);
        if (taskIndex >= 0)
        {
            await Deleted.RecordAsync(new DeletedItem(Guid.NewGuid(), now, DeletedKind.Task, labelled[taskIndex].Title, Task: labelled[taskIndex], Index: taskIndex, ChecklistStart: start, WasLive: IsLive));
            return;
        }

        foreach (SessionTask task in labelled)
        {
            int stepIndex = task.Subtasks.ToList().FindIndex(step => step.Id == id);
            if (stepIndex >= 0)
            {
                await Deleted.RecordAsync(new DeletedItem(Guid.NewGuid(), now, DeletedKind.Step, task.Subtasks[stepIndex].Title, Step: task.Subtasks[stepIndex], ParentId: task.Id, Index: stepIndex, ChecklistStart: start, WasLive: IsLive));
                return;
            }
        }
    }

    private async Task RecordHistoryDeletionAsync(Guid id)
    {
        if (Deleted is null)
        {
            return;
        }

        DateTimeOffset now = _time.GetUtcNow();
        foreach (SessionChecklist checklist in _history)
        {
            int taskIndex = checklist.Tasks.ToList().FindIndex(task => task.Id == id);
            if (taskIndex >= 0)
            {
                SessionTask task = checklist.Tasks[taskIndex];
                await Deleted.RecordAsync(new DeletedItem(Guid.NewGuid(), now, DeletedKind.HistoryTask, task.Title, Task: task, Index: taskIndex, ChecklistStart: checklist.SessionStart));
                return;
            }

            foreach (SessionTask owner in checklist.Tasks)
            {
                int stepIndex = owner.Subtasks.ToList().FindIndex(step => step.Id == id);
                if (stepIndex >= 0)
                {
                    await Deleted.RecordAsync(new DeletedItem(Guid.NewGuid(), now, DeletedKind.HistoryStep, owner.Subtasks[stepIndex].Title, Step: owner.Subtasks[stepIndex], ParentId: owner.Id, Index: stepIndex, ChecklistStart: checklist.SessionStart));
                    return;
                }
            }
        }
    }

    private Task RecordAsync(DeletedItem item)
    {
        return Deleted is null ? Task.CompletedTask : Deleted.RecordAsync(item);
    }

    // ---- Helpers ----

    // The deleted task left the draft (no session start) or the running checklist that started when this entry says.
    private bool InSameChecklist(DeletedItem entry)
    {
        return entry.ChecklistStart is null
            ? Current.SessionStart is null
            : Current.SessionStart is DateTimeOffset start && (start - entry.ChecklistStart.Value).Duration() <= TimeSpan.FromSeconds(5);
    }

    // The record the task left: the session that started then, or the "done outside a session" record of the day it was done
    // (none left for that day: the task comes back as a new one on Today).
    private int HistoryChecklistIndex(DeletedItem entry)
    {
        return entry.ChecklistStart is DateTimeOffset start
            ? _history.FindIndex(checklist => checklist.SessionStart is DateTimeOffset held && (held - start).Duration() <= TimeSpan.FromSeconds(5))
            : _history.FindIndex(checklist => checklist.OutsideSession && checklist.Tasks.Any(task => task.DoneAt is DateTimeOffset done && LocalDate(done) == LocalDate(entry.Task?.DoneAt ?? entry.DeletedAt)));
    }

    // Its category by id when it still exists, else by the name and colour it was labelled with (made again when it is gone).
    private async Task<Guid?> CategoryIdOfAsync(SessionTask task)
    {
        if (task.CategoryId is Guid id && _categories.Any(category => category.Id == id))
        {
            return id;
        }

        return task.Category is { } name && name.Trim().Length > 0
            ? await ResolveRemoteCategoryAsync(new SyncTaskRequest(Category: name, CategoryColor: task.CategoryColor))
            : null;
    }
}
