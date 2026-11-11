using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Moving tasks around from anywhere on the Tasks page (ADR 25, 50): "Add to Today" from Later, a come-back, the Archive or an
/// ended session's record, copies to Later and Come back to, ticking a task wherever it is, and changing what an ended session's
/// record holds (its ticks, categories, steps). Nothing here loosens a session's rules: a 4th task is never added without the
/// reason "Add anyway" asks for, and nothing ends or shortens a session.
/// </summary>
public sealed partial class SessionTasksService
{
    /// <summary>
    /// A task joins today's list: the next session's while no session runs, the running checklist (planning or focus) while one
    /// does, through the same add path and three-task rule as the add row. It always comes in open, as a new task with a new id.
    /// At three tasks nothing is added unless <paramref name="overLimitReason"/> is given (at least
    /// <see cref="MinOverLimitReasonLength"/> characters, kept on this PC and counted for the review, like "Add anyway").
    /// </summary>
    public async Task<(AddToTodayResult Result, Guid? TaskId)> AddToTodayAsync(TodaySource source, string? overLimitReason = null)
    {
        string title = source.Title.Trim();
        if (title.Length == 0)
        {
            return (AddToTodayResult.NothingToAdd, null);
        }

        if (IsAtActionLimit)
        {
            if (overLimitReason is null)
            {
                return (AddToTodayResult.AtLimit, null);
            }

            if (!await RecordOverLimitAsync(overLimitReason))
            {
                return (AddToTodayResult.ReasonTooShort, null);
            }
        }

        Guid? categoryId = source.Category is { } name && name.Trim().Length > 0
            ? await ResolveRemoteCategoryAsync(new SyncTaskRequest(Category: name, CategoryColor: source.CategoryColor))
            : null;
        // ADR 76: a task moved from another list of the task list keeps its id and its steps' ids (a bin change of the same task).
        bool keepsId = source.Id is Guid && _taskStore is not null;
        List<SessionSubtask> steps = source.Steps.Select(step => new SessionSubtask(keepsId ? step.Id : Guid.NewGuid(), step.Title, keepsId ? step.DoneAt : null)).ToList();
        var task = new SessionTask(
            keepsId ? source.Id!.Value : Guid.NewGuid(), title, null, steps, categoryId,
            EstimateMinutes: SessionChecklist.CleanEstimate(source.EstimateMinutes), Note: SessionChecklist.CleanNote(source.Note),
            Labels: TaskLabels.Clean(source.Labels));
        if (keepsId)
        {
            await HoldIfLiveAsync(task.Id);
        }

        SessionChecklist added = Current.AddTask(task);
        Guid addedId = added.Tasks.Count > Current.Tasks.Count ? added.Tasks[^1].Id : task.Id;
        await SetAsync(added);
        return (AddToTodayResult.Added, addedId);
    }

    /// <summary>
    /// A task chosen from a list for tomorrow's first task: at the top of the checklist, with what its list held, and gone from that list
    /// (a finished task stays in its record). Never over the three-task rule: as with a typed one, this is planning the next day.
    /// </summary>
    public async Task TakeFirstFromListAsync(ViewModels.PulledListKind kind, Guid id, string title)
    {
        TodaySource source = new(title, []);
        switch (kind)
        {
            case ViewModels.PulledListKind.Later when Later?.Items.FirstOrDefault(candidate => candidate.Id == id) is SyncLaterItem item:
                source = new TodaySource(item.Title, OpenStepsOf(item.Subtasks), item.Category, item.CategoryColor, item.EstimateMinutes, item.Note, item.Labels);
                await Later.DeleteAsync(id);
                break;
            case ViewModels.PulledListKind.ComeBack when Comebacks?.Open.FirstOrDefault(candidate => candidate.Id == id) is Comeback comeback:
                source = new TodaySource(comeback.Text, [], comeback.Category, comeback.CategoryColor, Labels: comeback.Labels);
                await Comebacks.TickAsync(id);
                break;
            case ViewModels.PulledListKind.AutoSaved when AutoSaved is not null:
                source = new TodaySource(title, [], Labels: AutoSaved.Find(id)?.Labels);
                await AutoSaved.RemoveAsync(id);
                break;
            case ViewModels.PulledListKind.Unfinished or ViewModels.PulledListKind.DoneToday or ViewModels.PulledListKind.Archive when PastTaskOf(id) is SessionTask task:
                source = SourceOfTask(task);
                if (!task.IsDone)
                {
                    await RememberMovedAsync(id);
                }

                break;
        }

        Guid? categoryId = source.Category is { } name && name.Trim().Length > 0
            ? await ResolveRemoteCategoryAsync(new SyncTaskRequest(Category: name, CategoryColor: source.CategoryColor))
            : null;
        List<SessionSubtask> steps = source.Steps.Select(step => new SessionSubtask(Guid.NewGuid(), step.Title, null)).ToList();
        var first = new SessionTask(
            Guid.NewGuid(), title.Trim(), null, steps, categoryId,
            EstimateMinutes: SessionChecklist.CleanEstimate(source.EstimateMinutes), Note: SessionChecklist.CleanNote(source.Note),
            Labels: TaskLabels.Clean(source.Labels));
        await SetAsync(Current with { Tasks = [first, .. Current.Tasks] });
    }

    /// <summary>"Add to Today" for a Later item: it joins today's list and leaves the Later list (its plan and due date go).</summary>
    public async Task<(AddToTodayResult Result, Guid? TaskId)> MoveFromLaterAsync(Guid laterId, string? overLimitReason = null)
    {
        SyncLaterItem? item = Later?.Items.FirstOrDefault(candidate => candidate.Id == laterId);
        if (Later is null || item is null)
        {
            return (AddToTodayResult.NothingToAdd, null);
        }

        bool keepsId = _taskStore is not null;
        var source = new TodaySource(
            item.Title, keepsId ? item.Subtasks ?? [] : OpenStepsOf(item.Subtasks), item.Category, item.CategoryColor, item.EstimateMinutes, item.Note, item.Labels,
            keepsId ? laterId : null);
        (AddToTodayResult Result, Guid? TaskId) added = await AddToTodayAsync(source, overLimitReason);
        if (added.Result == AddToTodayResult.Added)
        {
            await Later.DeleteAsync(laterId);
        }

        return added;
    }

    /// <summary>"Add to Today" for an Auto-saved item: it joins today's list and leaves the Auto-saved list, under the same rules as Later's.</summary>
    public async Task<(AddToTodayResult Result, Guid? TaskId)> AddAutoSavedToTodayAsync(Guid autoSavedId, string? overLimitReason = null)
    {
        AutoSavedTask? item = AutoSaved?.Items.FirstOrDefault(candidate => candidate.Id == autoSavedId);
        if (AutoSaved is null || item is null)
        {
            return (AddToTodayResult.NothingToAdd, null);
        }

        (AddToTodayResult Result, Guid? TaskId) added = await AddToTodayAsync(
            new TodaySource(item.Title, [], Labels: item.Labels, Id: _taskStore is not null ? autoSavedId : null), overLimitReason);
        if (added.Result == AddToTodayResult.Added)
        {
            await AutoSaved.RemoveAsync(autoSavedId);
        }

        return added;
    }

    /// <summary>"Add to Today" for an open come-back: it becomes a task and the come-back is ticked off.</summary>
    public async Task<(AddToTodayResult Result, Guid? TaskId)> AddComebackToTodayAsync(Guid comebackId, string? overLimitReason = null)
    {
        if (Comebacks?.Open.FirstOrDefault(candidate => candidate.Id == comebackId) is not Comeback comeback)
        {
            return (AddToTodayResult.NothingToAdd, null);
        }

        (AddToTodayResult Result, Guid? TaskId) added = await AddToTodayAsync(
            new TodaySource(comeback.Text, [], comeback.Category, comeback.CategoryColor, Labels: comeback.Labels, Id: _taskStore is not null ? comebackId : null),
            overLimitReason);
        if (added.Result == AddToTodayResult.Added)
        {
            // ADR 76: the task moved (same id), so the come-back goes rather than being ticked off.
            if (_taskStore is not null)
            {
                await Comebacks.DeleteAsync(comebackId);
            }
            else
            {
                await Comebacks.TickAsync(comebackId);
            }
        }

        return added;
    }

    /// <summary>
    /// "Add to Today" for a task of an ended session's record (Done today, Unfinished, the Archive). It comes back open as a copy
    /// with a new id; the record keeps the original as it was. An unfinished one is remembered as moved, so it leaves "Unfinished
    /// from past sessions".
    /// </summary>
    public async Task<(AddToTodayResult Result, Guid? TaskId)> AddHistoryTaskToTodayAsync(Guid taskId, string? overLimitReason = null)
    {
        if (PastTaskOf(taskId) is not SessionTask task)
        {
            return (AddToTodayResult.NothingToAdd, null);
        }

        (AddToTodayResult Result, Guid? TaskId) added = await AddToTodayAsync(SourceOfTask(task), overLimitReason);
        if (added.Result == AddToTodayResult.Added && !task.IsDone)
        {
            await RememberMovedAsync(taskId);
        }

        if (added.TaskId is Guid copyId)
        {
            await CarryHeldAsync(taskId, copyId);
        }

        return added;
    }

    /// <summary>
    /// A task of an ended session's record goes to the Later list as a copy (open, with its steps, category, estimate and note).
    /// An unfinished one is remembered as moved. The new Later item's id; null when nothing moved.
    /// </summary>
    public async Task<Guid?> MoveHistoryTaskToLaterAsync(Guid taskId)
    {
        if (Later is null || PastTaskOf(taskId) is not SessionTask task)
        {
            return null;
        }

        TodaySource source = SourceOfTask(task);
        // Remembered before the Later item is added, so the page that redraws on its change already leaves the task out.
        if (!task.IsDone)
        {
            await RememberMovedAsync(taskId);
        }

        SyncLaterItem? item = await Later.AddAsync(source.Title, source.Steps, source.Category, source.CategoryColor, source.EstimateMinutes, source.Note, null, labels: source.Labels);
        if (item is not null)
        {
            await CarryHeldAsync(taskId, item.Id);
        }

        return item?.Id;
    }

    /// <summary>A task of an ended session's record is parked as a come-back (its title, with " (+N steps)"). Unfinished ones are remembered as moved.</summary>
    public async Task<bool> ParkHistoryTaskAsync(Guid taskId)
    {
        if (Comebacks is null || PastTaskOf(taskId) is not SessionTask task)
        {
            return false;
        }

        if (!task.IsDone)
        {
            await RememberMovedAsync(taskId);
        }

        TodaySource source = SourceOfTask(task);
        Guid copyId = Guid.NewGuid();
        await Comebacks.AddAsync(ParkTextOf(task.Title, source.Steps.Count), source.Category, source.CategoryColor, source.Labels, id: copyId);
        await CarryHeldAsync(taskId, copyId);
        return true;
    }

    /// <summary>A Later item becomes a come-back (its title, with " (+N steps)") and leaves the Later list.</summary>
    public async Task<bool> MoveLaterToComebackAsync(Guid laterId)
    {
        SyncLaterItem? item = Later?.Items.FirstOrDefault(candidate => candidate.Id == laterId);
        if (Later is null || Comebacks is null || item is null)
        {
            return false;
        }

        if (_taskStore is not null)
        {
            // ADR 76: the same task moves to To sort (id, number, steps and History stay), so its title stays as it is.
            await Comebacks.AddAsync(item.Title, item.Category, item.CategoryColor, item.Labels, id: laterId);
        }
        else
        {
            await Comebacks.AddAsync(ParkTextOf(item.Title, OpenStepsOf(item.Subtasks).Count), item.Category, item.CategoryColor, item.Labels);
        }

        await Later.DeleteAsync(laterId);
        return true;
    }

    /// <summary>A come-back becomes a Later item and leaves the come-backs.</summary>
    public async Task<Guid?> MoveComebackToLaterAsync(Guid comebackId)
    {
        if (Later is null || Comebacks?.All.FirstOrDefault(candidate => candidate.Id == comebackId) is not Comeback comeback)
        {
            return null;
        }

        SyncLaterItem? item = await Later.AddAsync(
            comeback.Text, null, comeback.Category, comeback.CategoryColor, null, null, null, labels: comeback.Labels, id: _taskStore is not null ? comebackId : null);
        await Comebacks.DeleteAsync(comebackId);
        return item?.Id;
    }

    /// <summary>
    /// A task for tomorrow's plan (the shutdown routine's "Plan tomorrow"): it sits on the Later list "put in plan" for the day part
    /// <paramref name="slot"/>, the same as the Tasks page's "Put in plan", so it joins the next session's list when that part starts
    /// (ADR 50) and never takes one of today's three places. A task chosen from a list moves from there (a finished one stays in its
    /// record); a typed one (<paramref name="source"/> null) is new.
    /// </summary>
    /// <returns>False when nothing was planned (no Later list, a blank title, or the chosen task is gone).</returns>
    public async Task<bool> PlanForTomorrowAsync((ViewModels.PulledListKind Kind, Guid Id)? source, string title, PlannedSlot slot)
    {
        if (Later is null || title.Trim().Length == 0)
        {
            return false;
        }

        Guid? laterId = null;
        switch (source?.Kind)
        {
            case ViewModels.PulledListKind.Later when Later.Items.Any(candidate => candidate.Id == source.Value.Id):
                laterId = source.Value.Id;
                break;
            case ViewModels.PulledListKind.ComeBack:
                laterId = await MoveComebackToLaterAsync(source.Value.Id);
                break;
            case ViewModels.PulledListKind.Unfinished or ViewModels.PulledListKind.DoneToday or ViewModels.PulledListKind.Archive:
                laterId = await MoveHistoryTaskToLaterAsync(source.Value.Id);
                break;
            case ViewModels.PulledListKind.AutoSaved when AutoSaved is not null:
                laterId = (await Later.AddAsync(title, null, null, null, null, null, null, slot, id: _taskStore is not null ? source.Value.Id : null))?.Id;
                await AutoSaved.RemoveAsync(source.Value.Id);
                return laterId is not null;
            case null:
                return await Later.AddAsync(title, null, null, null, null, null, null, slot) is not null;
        }

        if (laterId is not Guid id)
        {
            return false;
        }

        await Later.EditAsync(id, item => item with { PlannedFor = slot });
        return true;
    }

    /// <summary>A new open step just after the step <paramref name="stepId"/> of the checklist; it reopens a task that was done.</summary>
    public Task AddSubtaskAfterAsync(Guid stepId, string title)
    {
        return SetAsync(Current.AddSubtaskAfter(stepId, title));
    }

    // ---- Ticking a task wherever it is ----

    /// <summary>
    /// Ticks or unticks a task or step. In the draft or the running checklist it is the checklist's own tick (ADR 25 applies
    /// as on Today); in an ended session's record it changes the record. Unticking a task that was ticked off outside a session
    /// (a Later task) puts it back on the Later list.
    /// </summary>
    public async Task SetTickedAsync(Guid id, bool ticked)
    {
        bool? inCurrent = Current.Tasks.Select(task => task.Id == id ? task.IsDone : task.Subtasks.FirstOrDefault(step => step.Id == id)?.IsDone)
            .FirstOrDefault(state => state is not null);
        if (inCurrent is bool isDone)
        {
            if (isDone != ticked)
            {
                await ToggleAsync(id);
            }

            return;
        }

        if (!ticked && OutsideSessionTaskOf(id) is Guid outsideTask)
        {
            // A task of the "done outside a session" record, or one of its steps: it is open again, so it goes back to Later.
            await ReopenOutsideTaskToLaterAsync(outsideTask);
            return;
        }

        // ADR 72: a past task kept here as a local item is ticked here only.
        if (Kept?.Find(id) is not null)
        {
            await Kept.ChangeAsync(id, task => task with { DoneAt = ticked ? task.DoneAt ?? _time.GetUtcNow() : null });
            return;
        }

        // ADR 72: a past session's task ticked or unticked here (this PC's or another's) is settled the same way on every device.
        if (PastSettled is not null && (IsOwnPastTask(id) || ForeignTaskOf?.Invoke(id) is not null))
        {
            await PastSettled.SetAsync(id, ticked ? SyncPastSettled.Done : SyncPastSettled.Open);
        }

        await SetHistoryDoneAsync(id, ticked);
    }

    /// <summary>
    /// A Later item is ticked off (ADR 50): it stays on the shared list with its text and a done time, so every device shows it as
    /// done, and is kept as a task done now in the day's "done outside a session" record (Done today, then the Archive). The record
    /// has the Later item's id. Nothing about a session changes, and it never counts as focus.
    /// </summary>
    public async Task<bool> CompleteLaterAsync(Guid laterId)
    {
        SyncLaterItem? item = Later?.Items.FirstOrDefault(candidate => candidate.Id == laterId);
        if (Later is null || item is null)
        {
            return false;
        }

        DateTimeOffset now = _time.GetUtcNow();
        await RecordLaterDoneAsync(item, now);
        await Later.CompleteAsync(laterId, now);
        return true;
    }

    // The "done outside a session" record of a ticked Later item: once only, under the item's id (an item already recorded is left alone).
    private async Task RecordLaterDoneAsync(SyncLaterItem item, DateTimeOffset doneAt)
    {
        if (HistoryTaskOf(item.Id) is not null)
        {
            return;
        }

        List<SessionSubtask> steps = (item.Subtasks ?? []).Select(step => step with { DoneAt = step.DoneAt ?? doneAt }).ToList();
        var task = new SessionTask(item.Id, item.Title, doneAt, steps, null, item.Category, item.CategoryColor, item.EstimateMinutes, item.Note, Labels: item.Labels);
        int at = _history.FindIndex(checklist => checklist.OutsideSession && checklist.Tasks.Any(held => held.DoneAt is DateTimeOffset done && LocalDate(done) == LocalDate(doneAt)));
        _history = at >= 0
            ? _history.Select((checklist, index) => index == at ? checklist with { Tasks = [.. checklist.Tasks, task] } : checklist).ToList()
            : [.. _history, new SessionChecklist(null, false, [task], OutsideSession: true)];
        await SaveHistoryAsync();
    }

    /// <summary>
    /// What another device's tick (or Undo) did to Later items (ADR 50): an item ticked there gets its record here once; an item
    /// that is open again loses it. Called only by a merge that changed something, so a restart makes no record again.
    /// </summary>
    private async Task ApplyLaterDoneChangeAsync(LaterDoneChange change)
    {
        DateTimeOffset keptFrom = _time.GetUtcNow() - HistoryKept;
        foreach (SyncLaterItem item in change.NewlyDone)
        {
            if (item.DoneAt is DateTimeOffset doneAt && doneAt >= keptFrom)
            {
                await RecordLaterDoneAsync(item, doneAt);
            }
        }

        foreach (Guid id in change.Reopened)
        {
            if (OutsideSessionTaskOf(id) is Guid recorded)
            {
                await RemoveOutsideTaskAsync(recorded);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task RemoveOutsideTaskAsync(Guid taskId)
    {
        _history = _history
            .Select(checklist => checklist.OutsideSession ? checklist with { Tasks = checklist.Tasks.Where(held => held.Id != taskId).ToList() } : checklist)
            .Where(checklist => !checklist.OutsideSession || checklist.Tasks.Count > 0)
            .ToList();
        await SaveHistoryAsync();
    }

    // The task of the "done outside a session" record that is <paramref name="id"/> or has a step with that id; null if none.
    private Guid? OutsideSessionTaskOf(Guid id)
    {
        return _history
            .Where(checklist => checklist.OutsideSession)
            .SelectMany(checklist => checklist.Tasks)
            .FirstOrDefault(task => task.Id == id || task.Subtasks.Any(step => step.Id == id))?.Id;
    }

    // Unticked again: the task is open and goes back to Later. A ticked Later item (the record has its id) is opened again in place, as a
    // newer edit every device takes; an older record (from before ticked items stayed on the list) comes back as a new item with its
    // steps open and its category, estimate and note.
    private async Task ReopenOutsideTaskToLaterAsync(Guid taskId)
    {
        if (Later is null || HistoryTaskOf(taskId) is not SessionTask task)
        {
            return;
        }

        if (!await Later.ReopenAsync(taskId))
        {
            TodaySource source = SourceOfTask(task);
            await Later.AddAsync(task.Title, source.Steps, source.Category, source.CategoryColor, task.EstimateMinutes, task.Note, null);
        }

        await RemoveOutsideTaskAsync(taskId);
    }

    // ---- An ended session's record ----

    /// <summary>The task <paramref name="id"/> of an ended session's record (or of a "done outside a session" record), or null.</summary>
    public SessionTask? HistoryTaskOf(Guid id)
    {
        return _history.SelectMany(checklist => checklist.Tasks).FirstOrDefault(task => task.Id == id);
    }

    /// <summary>The task or step is part of the draft or the running checklist.</summary>
    public bool IsInChecklist(Guid id)
    {
        return Current.Tasks.Any(task => task.Id == id || task.Subtasks.Any(step => step.Id == id));
    }

    /// <summary>
    /// Ticks or unticks a task or step in an ended session's record: a task takes its steps with it, and a task whose last step
    /// is ticked is done at that step's time, as in the checklist.
    /// </summary>
    public Task SetHistoryDoneAsync(Guid id, bool done)
    {
        DateTimeOffset now = _time.GetUtcNow();

        SessionTask Apply(SessionTask task)
        {
            if (task.Id == id)
            {
                DateTimeOffset? doneAt = done ? task.DoneAt ?? now : null;
                return task with
                {
                    DoneAt = doneAt,
                    Subtasks = task.Subtasks.Select(step => step with { DoneAt = done ? step.DoneAt ?? now : null }).ToList(),
                };
            }

            if (task.Subtasks.All(step => step.Id != id))
            {
                return task;
            }

            List<SessionSubtask> steps = task.Subtasks.Select(step => step.Id == id ? step with { DoneAt = done ? step.DoneAt ?? now : null } : step).ToList();
            return task with { DoneAt = steps.All(step => step.IsDone) ? steps.Max(step => step.DoneAt) : null, Subtasks = steps };
        }

        return ChangeHistoryAsync(Apply);
    }

    /// <summary>Puts a task of an ended session's record in a category (null: none); the record keeps the category's name and colour.</summary>
    public Task SetHistoryCategoryAsync(Guid taskId, Guid? categoryId)
    {
        TaskCategory? category = _categories.FirstOrDefault(candidate => candidate.Id == categoryId);
        return ChangeHistoryAsync(task => task.Id == taskId
            ? task with { CategoryId = category?.Id, Category = category?.Name, CategoryColor = category?.Color }
            : task);
    }

    /// <summary>
    /// A new open step in a task of an ended session's record: at the end, or just after the step <paramref name="afterStepId"/>.
    /// Like any new step it reopens a task that was done.
    /// </summary>
    public Task AddHistoryStepAsync(Guid taskId, string title, Guid? afterStepId = null)
    {
        string trimmed = title.Trim();
        if (trimmed.Length == 0)
        {
            return Task.CompletedTask;
        }

        return ChangeHistoryAsync(task =>
        {
            if (task.Id != taskId)
            {
                return task;
            }

            List<SessionSubtask> steps = task.Subtasks.ToList();
            int after = afterStepId is Guid step ? steps.FindIndex(held => held.Id == step) : -1;
            steps.Insert(after >= 0 ? after + 1 : steps.Count, new SessionSubtask(Guid.NewGuid(), trimmed, null));
            return task with { DoneAt = null, Subtasks = steps };
        });
    }

    // A checklist none of whose tasks changed stays the same object, so its sealed copy is reused (see Sealed).
    private Task ChangeHistoryAsync(Func<SessionTask, SessionTask> change)
    {
        _history = _history.Select(checklist =>
        {
            List<SessionTask> tasks = checklist.Tasks.Select(change).ToList();
            return tasks.Where((task, index) => !ReferenceEquals(task, checklist.Tasks[index])).Any() ? checklist with { Tasks = tasks } : checklist;
        }).ToList();
        return SaveHistoryAsync();
    }

    // DPAPI-sealing 90 days of history on every change was the slow part of a tick: each checklist is sealed once and the sealed text
    // kept with it while it stays the same object (records are never changed in place).
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<SessionChecklist, StoredHistory> _sealedHistory = new();

    private StoredHistory Sealed(SessionChecklist checklist)
    {
        return _sealedHistory.GetValue(checklist, item => new StoredHistory(Seal(item)));
    }

    private async Task SaveHistoryAsync()
    {
        List<StoredHistory> sealedHistory;
        using (TickDiag.Measure($"history seal ({_history.Count} checklists)"))
        {
            sealedHistory = _history.Select(Sealed).ToList();
        }

        await _store.SaveAsync(HistoryKey, sealedHistory);
        using (TickDiag.Measure("history Changed handlers"))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    // A record is kept 90 days: a session by its start, a "done outside a session" record by its last tick.
    private static bool KeptInHistory(SessionChecklist checklist, DateTimeOffset cutoff)
    {
        return checklist.OutsideSession
            ? checklist.Tasks.Any(task => task.DoneAt >= cutoff)
            : checklist.SessionStart is DateTimeOffset start && start >= cutoff;
    }

    // Remembered as moved on (to Today, Later or Come back to): it no longer shows under "Unfinished from past sessions".
    // ADR 72: a shared record, so every device stops listing it and its PC's record follows. A past task kept here as a local
    // item leaves this device only: it is local, so nothing goes out.
    private async Task RememberMovedAsync(Guid taskId)
    {
        if (Kept?.Find(taskId) is not null)
        {
            await Kept.RemoveAsync(taskId);
        }
        else if (PastSettled is not null)
        {
            await PastSettled.SetAsync(taskId, SyncPastSettled.Moved);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- Helpers ----

    private TodaySource SourceOfTask(SessionTask task)
    {
        (string? categoryName, string? categoryColor) = TaskCategoryDisplay.Resolve(_categories, task.CategoryId, task.Category, task.CategoryColor);
        // A done task comes back open: all its steps reopen; an open one brings its open steps.
        IReadOnlyList<SessionSubtask> steps = task.IsDone ? task.Subtasks : OpenStepsOf(task.Subtasks);
        return new TodaySource(task.Title, steps, categoryName, categoryColor, task.EstimateMinutes, task.Note, task.Labels);
    }

    // ADR 76: a copy made from an ended session's record takes its original's number, reminders and the like from the copy held
    // since that session started (kept a day after it ended, and until its review is done); the copy is in the task list only
    // outside a session (inside one it joined the running checklist).
    private Task CarryHeldAsync(Guid originalId, Guid copyId)
    {
        return _taskStore is null ? Task.CompletedTask : _taskStore.CarryHeldAsync(originalId, copyId);
    }

    // A task joining a running session from a list: its copy in the task list is kept with the session's (ADR 76), before the list
    // it leaves deletes it there, so it can come back whole.
    private Task HoldIfLiveAsync(Guid taskId)
    {
        return _taskStore is not null && Current.SessionStart is not null && IsLive ? _taskStore.AlsoHoldAsync(taskId) : Task.CompletedTask;
    }

    private static IReadOnlyList<SessionSubtask> OpenStepsOf(IReadOnlyList<SessionSubtask>? steps)
    {
        return (steps ?? []).Where(step => !step.IsDone).ToList();
    }

    private static string ParkTextOf(string title, int openSteps)
    {
        return openSteps > 0 ? $"{title} (+{openSteps} {(openSteps == 1 ? "step" : "steps")})" : title;
    }

    private DateOnly LocalDate(DateTimeOffset time)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, _sessions.TimeProvider.LocalTimeZone).DateTime);
    }
}
