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

        if (IsAtTaskLimit)
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
        List<SessionSubtask> steps = source.Steps.Select(step => new SessionSubtask(Guid.NewGuid(), step.Title, null)).ToList();
        var task = new SessionTask(
            Guid.NewGuid(), title, null, steps, categoryId,
            EstimateMinutes: SessionChecklist.CleanEstimate(source.EstimateMinutes), Note: SessionChecklist.CleanNote(source.Note));
        await SetAsync(Current.AddTask(task));
        return (AddToTodayResult.Added, task.Id);
    }

    /// <summary>"Add to Today" for a Later item: it joins today's list and leaves the Later list (its plan and due date go).</summary>
    public async Task<(AddToTodayResult Result, Guid? TaskId)> MoveFromLaterAsync(Guid laterId, string? overLimitReason = null)
    {
        SyncLaterItem? item = Later?.Items.FirstOrDefault(candidate => candidate.Id == laterId);
        if (Later is null || item is null)
        {
            return (AddToTodayResult.NothingToAdd, null);
        }

        var source = new TodaySource(item.Title, OpenStepsOf(item.Subtasks), item.Category, item.CategoryColor, item.EstimateMinutes, item.Note);
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

        (AddToTodayResult Result, Guid? TaskId) added = await AddToTodayAsync(new TodaySource(item.Title, []), overLimitReason);
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

        (AddToTodayResult Result, Guid? TaskId) added = await AddToTodayAsync(new TodaySource(comeback.Text, []), overLimitReason);
        if (added.Result == AddToTodayResult.Added)
        {
            await Comebacks.TickAsync(comebackId);
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
        if (HistoryTaskOf(taskId) is not SessionTask task)
        {
            return (AddToTodayResult.NothingToAdd, null);
        }

        (AddToTodayResult Result, Guid? TaskId) added = await AddToTodayAsync(SourceOfTask(task), overLimitReason);
        if (added.Result == AddToTodayResult.Added && !task.IsDone)
        {
            await RememberMovedAsync(taskId);
        }

        return added;
    }

    /// <summary>
    /// A task of an ended session's record goes to the Later list as a copy (open, with its steps, category, estimate and note).
    /// An unfinished one is remembered as moved. The new Later item's id; null when nothing moved.
    /// </summary>
    public async Task<Guid?> MoveHistoryTaskToLaterAsync(Guid taskId)
    {
        if (Later is null || HistoryTaskOf(taskId) is not SessionTask task)
        {
            return null;
        }

        TodaySource source = SourceOfTask(task);
        // Remembered before the Later item is added, so the page that redraws on its change already leaves the task out.
        if (!task.IsDone)
        {
            await RememberMovedAsync(taskId);
        }

        SyncLaterItem? item = await Later.AddAsync(source.Title, source.Steps, source.Category, source.CategoryColor, source.EstimateMinutes, source.Note, null);
        return item?.Id;
    }

    /// <summary>A task of an ended session's record is parked as a come-back (its title, with " (+N steps)"). Unfinished ones are remembered as moved.</summary>
    public async Task<bool> ParkHistoryTaskAsync(Guid taskId)
    {
        if (Comebacks is null || HistoryTaskOf(taskId) is not SessionTask task)
        {
            return false;
        }

        if (!task.IsDone)
        {
            await RememberMovedAsync(taskId);
        }

        await Comebacks.AddAsync(ParkTextOf(task.Title, SourceOfTask(task).Steps.Count));
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

        await Comebacks.AddAsync(ParkTextOf(item.Title, OpenStepsOf(item.Subtasks).Count));
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

        SyncLaterItem? item = await Later.AddAsync(comeback.Text, null, null, null, null, null, null);
        await Comebacks.DeleteAsync(comebackId);
        return item?.Id;
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

        if (!ticked && IsOutsideSessionTask(id))
        {
            await ReopenOutsideTaskToLaterAsync(id);
            return;
        }

        await SetHistoryDoneAsync(id, ticked);
    }

    /// <summary>
    /// A Later item is ticked off: it leaves the Later list and is kept as a task done now, in the day's "done outside a
    /// session" record (it shows under Done today and in the Archive). Nothing about a session changes.
    /// </summary>
    public async Task<bool> CompleteLaterAsync(Guid laterId)
    {
        SyncLaterItem? item = Later?.Items.FirstOrDefault(candidate => candidate.Id == laterId);
        if (Later is null || item is null)
        {
            return false;
        }

        DateTimeOffset now = _time.GetUtcNow();
        List<SessionSubtask> steps = (item.Subtasks ?? []).Select(step => step with { DoneAt = step.DoneAt ?? now }).ToList();
        var task = new SessionTask(Guid.NewGuid(), item.Title, now, steps, null, item.Category, item.CategoryColor, item.EstimateMinutes, item.Note);
        int at = _history.FindIndex(checklist => checklist.OutsideSession && checklist.Tasks.Any(held => held.DoneAt is DateTimeOffset done && LocalDate(done) == LocalDate(now)));
        _history = at >= 0
            ? _history.Select((checklist, index) => index == at ? checklist with { Tasks = [.. checklist.Tasks, task] } : checklist).ToList()
            : [.. _history, new SessionChecklist(null, false, [task], OutsideSession: true)];
        await SaveHistoryAsync();
        await Later.DeleteAsync(laterId);
        return true;
    }

    private bool IsOutsideSessionTask(Guid id)
    {
        return _history.Any(checklist => checklist.OutsideSession && checklist.Tasks.Any(task => task.Id == id));
    }

    // Unticked again: the task is open and goes back to Later, with its steps open and its category, estimate and note.
    private async Task ReopenOutsideTaskToLaterAsync(Guid taskId)
    {
        if (Later is null || HistoryTaskOf(taskId) is not SessionTask task)
        {
            return;
        }

        await Later.AddAsync(task.Title, SourceOfTask(task).Steps, task.Category, task.CategoryColor, task.EstimateMinutes, task.Note, null);
        _history = _history
            .Select(checklist => checklist.OutsideSession ? checklist with { Tasks = checklist.Tasks.Where(held => held.Id != taskId).ToList() } : checklist)
            .Where(checklist => !checklist.OutsideSession || checklist.Tasks.Count > 0)
            .ToList();
        await SaveHistoryAsync();
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

    private Task ChangeHistoryAsync(Func<SessionTask, SessionTask> change)
    {
        _history = _history.Select(checklist => checklist with { Tasks = checklist.Tasks.Select(change).ToList() }).ToList();
        return SaveHistoryAsync();
    }

    private async Task SaveHistoryAsync()
    {
        await _store.SaveAsync(HistoryKey, _history.Select(item => new StoredHistory(Seal(item))).ToList());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // A record is kept 90 days: a session by its start, a "done outside a session" record by its last tick.
    private static bool KeptInHistory(SessionChecklist checklist, DateTimeOffset cutoff)
    {
        return checklist.OutsideSession
            ? checklist.Tasks.Any(task => task.DoneAt >= cutoff)
            : checklist.SessionStart is DateTimeOffset start && start >= cutoff;
    }

    // Remembered as moved on (to Today, Later or Come back to): it no longer shows under "Unfinished from past sessions".
    private async Task RememberMovedAsync(Guid taskId)
    {
        DateTimeOffset keptFrom = _time.GetUtcNow() - HistoryKept;
        _movedToLater = [.. _movedToLater.Where(entry => entry.At >= keptFrom && entry.Id != taskId), new MovedEntry(taskId, _time.GetUtcNow())];
        await _store.SaveAsync(MovedToLaterKey, _movedToLater);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- Helpers ----

    private TodaySource SourceOfTask(SessionTask task)
    {
        TaskCategory? category = _categories.FirstOrDefault(candidate => candidate.Id == task.CategoryId);
        // A done task comes back open: all its steps reopen; an open one brings its open steps.
        IReadOnlyList<SessionSubtask> steps = task.IsDone ? task.Subtasks : OpenStepsOf(task.Subtasks);
        return new TodaySource(task.Title, steps, task.Category ?? category?.Name, task.CategoryColor ?? category?.Color, task.EstimateMinutes, task.Note);
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
