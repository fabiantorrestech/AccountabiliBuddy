using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

// Tasks redesign 3.6 (spec 03 §B, `05` §2): the session layout of the Tasks page (Compact on by itself, the Shelf hidden with its rail
// reading "hidden during session", Queued and Finished faded), This session's Quiet / Grouped rows of a running session, the step list
// popover, "This session is full" (swap or Add anyway), the "Session ended at 12:52 AM" strip and "Hold to reset". Every way into This
// session keeps the session's cap (the session's own add, SessionTasksService.FocusStepAsync, SwapOutAsync); "Add anyway" is the
// over-limit add with its reason, recorded as before. Nothing here ends, shortens or unlocks a session. Times come from the trusted clock.
public sealed partial class TasksViewModel
{
    private SessionLayoutService? _sessionLayout;
    private bool _layoutActive;
    private TaskSettings _layoutSettings = Services.Tasks.TaskSettings.Default;
    private bool _shelfBeforeLayout;
    private bool _shelfPeeked;
    private bool? _densityPick;
    private DispatcherTimer? _noticeTimer;

    /// <summary>Asks the page's view to show "This session is full" (a modal dialog over the window).</summary>
    public event EventHandler<SessionFullViewModel>? SessionFullRequested;

    /// <summary>
    /// From now on the page follows the session layout (Compact, the Shelf hidden, faded Queued) while a real session runs and, once it
    /// ended, until Tasks is next opened or "Switch now". Called once by the app.
    /// </summary>
    public void UseSessionLayout(SessionLayoutService layout)
    {
        _sessionLayout = layout;
        layout.Changed += (_, _) => ApplySessionLayout();
        TodayPanel.FoldsChanged += (_, _) => NotifySessionViewChanged();
        Shelf.Panel.FoldsChanged += (_, _) => NotifySessionViewChanged();
        Shelf.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ShelfViewModel.CountText))
            {
                OnPropertyChanged(nameof(ShelfRailSub));
            }
            else if (e.PropertyName is nameof(ShelfViewModel.SearchText))
            {
                NotifySessionViewChanged();
            }
        };
        ApplySessionLayout();
    }

    /// <summary>The page is on screen (set by its view): the "Session ended" notice starts its countdown only then.</summary>
    public bool IsOnScreen
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                RefreshEndedNotice();
            }
        }
    }

    /// <summary>The session layout shows (a real session runs, or one ended and Tasks hasn't switched back yet).</summary>
    public bool InSessionLayout => _layoutActive;

    // ---- Density: Compact on by itself in the session layout (the setting); a pick made then lasts only for that layout ----

    private bool CompactNow => _layoutActive
        ? _densityPick ?? (_layoutSettings.CompactInSession || _layout.Current.Compact)
        : _layout.Current.Compact;

    private Task PickDensityAsync(bool compact)
    {
        if (!_layoutActive)
        {
            return _layout.SetCompactAsync(compact);
        }

        _densityPick = compact;
        ApplyDensity();
        NotifySessionViewChanged();
        return Task.CompletedTask;
    }

    // The Tasks page's panels follow the session layout's density; the Today page keeps its own (Q163).
    private void ApplyDensity()
    {
        bool? compact = _layoutActive ? CompactNow : null;
        TodayPanel.CompactOverride = compact;
        Shelf.Panel.CompactOverride = compact;
        OnPropertyChanged(nameof(IsCompact));
    }

    // ---- The Shelf's rail during a session ----

    /// <summary>The Shelf is hidden because a session runs (the setting): its rail reads "hidden during session".</summary>
    public bool IsShelfHiddenForSession => _layoutActive && _layoutSettings.HideShelfInSession && !IsShelfShown;

    /// <summary>"10 tasks", or "hidden during session".</summary>
    public string ShelfRailSub => IsShelfHiddenForSession ? "hidden during session" : Shelf.CountText;

    public MaterialIconKind ShelfRailIcon => IsShelfHiddenForSession ? MaterialIconKind.EyeOffOutline : MaterialIconKind.ArrowExpandLeft;

    public string ShelfRailTip => IsShelfHiddenForSession ? "The Shelf is hidden during the session · click to show it" : "Show the Shelf (Ctrl+\\)";

    public double ShelfRailOpacity => IsShelfHiddenForSession ? 0.8 : 1;

    partial void OnIsShelfShownChanged(bool value)
    {
        OnPropertyChanged(nameof(IsShelfHiddenForSession));
        OnPropertyChanged(nameof(ShelfRailSub));
        OnPropertyChanged(nameof(ShelfRailIcon));
        OnPropertyChanged(nameof(ShelfRailTip));
        OnPropertyChanged(nameof(ShelfRailOpacity));
        NotifySessionViewChanged();
    }

    partial void OnPickedCountChanged(int value)
    {
        NotifySessionViewChanged();
    }

    // The layout starts (a session runs) or goes back to planning (switched); the ended notice follows the service.
    private void ApplySessionLayout()
    {
        bool wanted = _sessionLayout?.InSessionLayout == true;
        if (wanted != _layoutActive)
        {
            if (wanted)
            {
                // What it starts with stays for the whole layout: changing the settings mid-session never jumps it.
                _layoutSettings = TaskSettings?.Current ?? Services.Tasks.TaskSettings.Default;
                _shelfBeforeLayout = IsShelfShown;
                _densityPick = null;
                _shelfPeeked = false;
                _layoutActive = true;
                if (_layoutSettings.HideShelfInSession)
                {
                    IsShelfShown = false;
                    IsTodayHidden = false;
                }
            }
            else
            {
                _layoutActive = false;
                _densityPick = null;
                _shelfPeeked = false;
                IsShelfShown = _shelfBeforeLayout;
                if (!_tasks.IsLive)
                {
                    // Raised on the task toasts; the toast stack shows it (3.8).
                    _actions?.Say("Back to planning");
                }
            }

            ApplyDensity();
            OnIsShelfShownChanged(IsShelfShown);
            OnPropertyChanged(nameof(InSessionLayout));
            QueueRefresh();
        }

        RefreshEndedNotice();
    }

    // ---- "Session ended at 12:52 AM" (P4), above the add box while Tasks is on screen ----

    /// <summary>The notice shows: a session ended, Tasks still has its layout, and the notice wasn't closed or counted down.</summary>
    [ObservableProperty]
    public partial bool ShowsEndedNotice { get; set; }

    /// <summary>"Session ended at 12:52 AM".</summary>
    [ObservableProperty]
    public partial string EndedTitle { get; set; } = string.Empty;

    /// <summary>The × ring's share of the 15 seconds left, 1 down to 0.</summary>
    [ObservableProperty]
    public partial double EndedCountdown { get; set; } = 1;

    /// <summary>"Switch now": Tasks goes back to planning at once.</summary>
    [RelayCommand]
    private void SwitchNow()
    {
        _sessionLayout?.SwitchNow();
    }

    /// <summary>× on the notice: it goes; the layout stays until Tasks is next opened.</summary>
    [RelayCommand]
    private void DismissEnded()
    {
        _sessionLayout?.DismissNotice();
    }

    /// <summary>"Save it as a view": Create a view from what the Shelf shows now, before the layout resets.</summary>
    [RelayCommand]
    private void SaveSessionView()
    {
        if (!Shelf.RequestCreateView())
        {
            Notice = "Views can be made once the Shelf's views are ready.";
        }
    }

    private void RefreshEndedNotice()
    {
        if (_sessionLayout is not { } layout)
        {
            return;
        }

        if (layout.IsNoticeShown && IsOnScreen)
        {
            layout.NoticeSeen();
        }

        bool shown = layout.IsNoticeShown;
        ShowsEndedNotice = shown;
        if (layout.EndedAt is DateTimeOffset endedAt)
        {
            EndedTitle = "Session ended at " + Local(endedAt).ToString("h:mm tt", CultureInfo.InvariantCulture);
        }

        TimeSpan left = layout.NoticeLeft ?? TimeSpan.Zero;
        EndedCountdown = Math.Clamp(left.TotalMilliseconds / SessionLayoutService.NoticeCountdown.TotalMilliseconds, 0, 1);

        // The countdown ticks only while the notice is on screen (a closed Tasks page, or the app in the tray for days, costs nothing);
        // coming back on screen reads it again (IsOnScreen, the service's Changed).
        bool ticking = shown && IsOnScreen;
        if (ticking && _noticeTimer is null)
        {
            _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _noticeTimer.Tick += (_, _) => RefreshEndedNotice();
            _noticeTimer.Start();
        }
        else if (!ticking && _noticeTimer is not null)
        {
            _noticeTimer.Stop();
            _noticeTimer = null;
        }
    }

    // ---- "Hold to reset" (1.5 s): the session view back the way it started ----

    /// <summary>
    /// The session view was changed (the Shelf shown, a density picked, sections folded or unfolded, a task open, a selection, a search):
    /// the reset button shows (it always keeps its slot).
    /// </summary>
    public bool IsSessionViewChanged => _layoutActive
        && (_shelfPeeked && IsShelfShown || _densityPick is not null || TodayPanel.HasFolds || Shelf.Panel.HasFolds || IsDetailOpen
            || PickedCount > 0 || Shelf.Search.Length > 0);

    /// <summary>The hint by the reset button ("Hold for 1.5 seconds…"), shown after a click that wasn't a hold.</summary>
    [ObservableProperty]
    public partial bool ShowsResetHint { get; set; }

    public static string ResetHint => "Hold for 1.5 seconds to put the session view back the way it started.";

    /// <summary>Held for 1.5 seconds: the Shelf hidden again, density back, folds forgotten, nothing open or selected, no search.</summary>
    public void ResetSessionView()
    {
        if (!_layoutActive)
        {
            return;
        }

        _shelfPeeked = false;
        _densityPick = null;
        if (_layoutSettings.HideShelfInSession)
        {
            IsShelfShown = false;
        }

        IsTodayHidden = false;
        ApplyDensity();
        TodayPanel.ForgetFolds();
        Shelf.Panel.ForgetFolds();
        ClearSelection();
        if (IsDetailOpen)
        {
            CloseDetail();
        }

        Shelf.SearchText = string.Empty;
        ShowsResetHint = false;
        _actions?.Say("Session view reset");
        NotifySessionViewChanged();
    }

    private void NotifySessionViewChanged()
    {
        OnPropertyChanged(nameof(IsSessionViewChanged));
    }

    // ---- This session's rows while a session runs: Quiet (one row per place) or Grouped (each task with its steps) ----

    private SessionTaskView SessionViewNow => _layoutActive ? _layoutSettings.SessionView : TaskSettings?.Current.SessionView ?? SessionTaskView.Quiet;

    private List<TaskListRowViewModel> LiveSessionRows(SessionChecklist current, IReadOnlyDictionary<Guid, TaskItem> stored, Guid? runningId)
    {
        IReadOnlyList<SessionSlot> slots = SessionActions.Slots(current);
        IReadOnlyCollection<Guid> focused = (IReadOnlyCollection<Guid>?)current.FocusedSteps?.ToHashSet() ?? [];
        var rows = new List<TaskListRowViewModel>();
        if (SessionViewNow == SessionTaskView.Quiet)
        {
            foreach (SessionSlot slot in slots)
            {
                SessionTask task = current.Tasks.First(candidate => candidate.Id == slot.TaskId);
                rows.Add(slot.StepId is Guid stepId
                    ? StepRow(current, task, stepId, slot.Number, stored)
                    : SessionTaskRow(task, slot.Number, runningId, []));
            }

            return rows;
        }

        foreach (SessionTask task in current.Tasks.Where(task => !task.IsDone))
        {
            List<SessionSlot> own = slots.Where(slot => slot.TaskId == task.Id).ToList();
            int number = own.FirstOrDefault(slot => slot.StepId is null)?.Number ?? 0;
            rows.Add(SessionTaskRow(task, number, runningId, task.Subtasks.Count > 0 ? GroupedLines(task, focused, own) : []));
        }

        return rows;
    }

    private TaskListRowViewModel SessionTaskRow(SessionTask task, int number, Guid? runningId, IReadOnlyList<SessionStepLineViewModel> lines)
    {
        TaskCategory? category = _tasks.Categories.FirstOrDefault(item => item.Id == task.CategoryId);
        Guid id = task.Id;
        return MakeRow(
            new TaskRowKey(TaskRowKind.Session, id), task.Title, SessionLine(task), category?.Name, category?.Color,
            number: number, isNow: id == runningId, categoryId: task.CategoryId, toggle: () => _tasks.SetTickedAsync(id, true), stepLines: lines);
    }

    // Quiet view: a focused step as its own row, with its letter chip and "↳ task · ● label · 142b ▾".
    private TaskListRowViewModel StepRow(SessionChecklist current, SessionTask task, Guid stepId, int number, IReadOnlyDictionary<Guid, TaskItem> stored)
    {
        int index = task.Subtasks.ToList().FindIndex(step => step.Id == stepId);
        SessionSubtask step = task.Subtasks[index];
        string letter = TaskStepLetters.Of(index);
        TaskItem? item = stored.TryGetValue(task.Id, out TaskItem? held) ? held : null;
        TaskCategory? category = _tasks.Categories.FirstOrDefault(candidate => candidate.Id == task.CategoryId);
        string idText = item?.Number is TaskNumber taskNumber ? taskNumber.Value.ToString(CultureInfo.InvariantCulture) + letter : string.Empty;
        int? minutes = item?.Steps?.FirstOrDefault(candidate => candidate.Id == stepId)?.EstimateMinutes;
        var key = new TaskRowKey(TaskRowKind.Session, stepId);
        var owner = new TaskRowKey(TaskRowKind.Session, task.Id);
        Guid taskId = task.Id;
        return new TaskListRowViewModel(
            key, step.Title, string.Empty, (_, _) => OpenOwner(owner), number, current.CurrentId == stepId,
            () => _tasks.SetTickedAsync(stepId, true), menu: () => MenuFor(owner))
        {
            OwnerKey = owner,
            Finishes = _finishes,
            IsFinishing = _finishes.IsPending(key),
            IsCollapsing = _finishes.IsFolding(key),
            NumberText = idText,
            TimeText = minutes is int stepMinutes ? EstimateText.Of(stepMinutes) : string.Empty,
            StepLetter = letter,
            StepSourceTitle = TaskToasts.Short(task.Title),
            StepSourceLabel = category?.Name ?? string.Empty,
            StepSourceBrush = TaskCategoryBrushes.Of(category?.Color),
            StepSourceId = idText,
            StepList = () => StepListFor(taskId),
        };
    }

    // Grouped view: done steps folded, focused ones numbered, skipped ones marked, the next two greyed, "+N more steps".
    private List<SessionStepLineViewModel> GroupedLines(SessionTask task, IReadOnlyCollection<Guid> focused, IReadOnlyList<SessionSlot> own)
    {
        var lines = new List<SessionStepLineViewModel>();
        string done = SessionStepGroups.DoneLetters(task);
        if (done.Length > 0)
        {
            lines.Add(SessionStepLineViewModel.Summary($"done ({done})"));
        }

        IReadOnlySet<Guid> skipped = SessionStepGroups.Skipped(task, focused);
        for (int index = 0; index < task.Subtasks.Count; index++)
        {
            SessionSubtask step = task.Subtasks[index];
            Guid stepId = step.Id;
            string letter = TaskStepLetters.Of(index);
            if (step.IsDone)
            {
                continue;
            }

            if (focused.Contains(stepId))
            {
                int number = own.FirstOrDefault(slot => slot.StepId == stepId)?.Number ?? 0;
                lines.Add(SessionStepLineViewModel.Focused(letter, step.Title, number, new AsyncRelayCommand(() => _tasks.SetTickedAsync(stepId, true))));
            }
            else if (skipped.Contains(stepId))
            {
                lines.Add(SessionStepLineViewModel.Skipped(letter, step.Title, new AsyncRelayCommand(() => FocusStepAsync(stepId))));
            }
        }

        IReadOnlyList<SessionSubtask> upcoming = SessionStepGroups.Upcoming(task, focused);
        foreach (SessionSubtask step in upcoming.Take(SessionStepGroups.NextShown))
        {
            Guid stepId = step.Id;
            int index = task.Subtasks.ToList().FindIndex(candidate => candidate.Id == stepId);
            lines.Add(SessionStepLineViewModel.Upcoming(TaskStepLetters.Of(index), step.Title, new AsyncRelayCommand(() => FocusStepAsync(stepId))));
        }

        int more = upcoming.Count - SessionStepGroups.NextShown;
        if (more > 0)
        {
            lines.Add(SessionStepLineViewModel.Summary(more == 1 ? "+1 more step" : $"+{more} more steps"));
        }

        return lines;
    }

    // A step row's click: its task opens (or closes, when it is the open one); nothing is selected.
    private void OpenOwner(TaskRowKey owner)
    {
        ClearSelection();
        if (IsDetailOpen && _selectedKey == owner)
        {
            CloseDetail();
            return;
        }

        IsDetailOpen = true;
        Select(owner);
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    // ---- The step list popover (P3) ----

    private StepListViewModel? StepListFor(Guid taskId)
    {
        if (!_tasks.IsLive)
        {
            return null;
        }

        var key = new TaskRowKey(TaskRowKind.Session, taskId);
        return new StepListViewModel(
            () => _tasks.Current.Tasks.FirstOrDefault(task => task.Id == taskId),
            () => (IReadOnlyCollection<Guid>?)_tasks.Current.FocusedSteps?.ToHashSet() ?? [],
            stepId => ToggleStepAsync(key, stepId),
            FocusStepAsync,
            title => AddStepAsync(key, title));
    }

    // ---- "This session is full" ----

    /// <summary>
    /// "Work on it now" from a reminder found This session full: the same dialog (a swap or Add anyway), shown over
    /// <paramref name="over"/> (the reminder's own topmost window) when given, so it isn't hidden under it.
    /// </summary>
    public void OfferSwapFor(Guid taskId, Avalonia.Visual? over = null)
    {
        OfferSwap([taskId], over: over);
    }

    // Tasks that found no room: the dialog offers a swap or Add anyway for each; closing leaves them where they were.
    private void OfferSwap(IReadOnlyList<Guid> taskIds, string? fallback = null, Avalonia.Visual? over = null)
    {
        List<SessionIncomingItem> items = taskIds.Distinct().Select(IncomingTask).OfType<SessionIncomingItem>().ToList();
        ShowSessionFull(items, fallback ?? (taskIds.Count == 1
            ? "This session is full. Finish one of its actions or move one to Queued first."
            : $"This session is full: {taskIds.Count} of them stayed where they were."), over);
    }

    // A step's target found no free place.
    private void OfferStepSwap(Guid stepId)
    {
        if (IncomingStep(stepId) is { } item)
        {
            ShowSessionFull([item], "This session is full. Finish one of its actions or take one out of focus first.");
        }
    }

    private void ShowSessionFull(IReadOnlyList<SessionIncomingItem> items, string fallback, Avalonia.Visual? over = null)
    {
        if (items.Count == 0)
        {
            return;
        }

        if (SessionFullRequested is null || _actions is null)
        {
            Notice = fallback;
            return;
        }

        var dialog = new SessionFullViewModel(items, SlotsFor, SwapInAsync, AddAnywayAsync, StillWaiting) { Over = over };
        if (!dialog.IsEmpty)
        {
            SessionFullRequested.Invoke(this, dialog);
        }
    }

    private SessionIncomingItem? IncomingTask(Guid taskId)
    {
        if (_store?.Lists.Tasks.FirstOrDefault(item => item.Id == taskId && item.Task.IsLive)?.Task is not TaskItem task)
        {
            return null;
        }

        TaskLabel? label = LabelOf(task.LabelId);
        var parts = new List<string>();
        if (label is not null)
        {
            parts.Add(label.Name);
        }

        if (task.EstimateMinutes is int minutes)
        {
            parts.Add(EstimateText.Of(minutes));
        }

        parts.Add("from " + WhereText(task.Bin));
        return new SessionIncomingItem(taskId, null, task.Title, string.Join(" · ", parts), label is null ? null : TaskLabelColors.Opaque(label.Color));
    }

    private SessionIncomingItem? IncomingStep(Guid stepId)
    {
        SessionTask? task = _tasks.Current.Tasks.FirstOrDefault(candidate => candidate.Subtasks.Any(step => step.Id == stepId));
        if (task is null)
        {
            return null;
        }

        int index = task.Subtasks.ToList().FindIndex(step => step.Id == stepId);
        TaskCategory? category = _tasks.Categories.FirstOrDefault(candidate => candidate.Id == task.CategoryId);
        string id = StepIdText(task.Id, index);
        string detail = "Step of " + TaskToasts.Short(task.Title) + (id.Length > 0 ? " · " + id : string.Empty);
        return new SessionIncomingItem(task.Id, stepId, task.Subtasks[index].Title, detail, category?.Color);
    }

    private static string WhereText(TaskBin bin) => bin switch
    {
        TaskBin.Queued => "Today · Queued",
        TaskBin.Sort => "Shelf · To sort",
        TaskBin.Next => "Shelf · Next",
        TaskBin.Later => "Shelf · Later",
        _ => "your lists",
    };

    // "142b", or empty while the task has no number.
    private string StepIdText(Guid taskId, int index)
    {
        TaskItem? item = _store?.Lists.Tasks.FirstOrDefault(candidate => candidate.Id == taskId)?.Task;
        return item?.Number is TaskNumber number ? number.Value.ToString(CultureInfo.InvariantCulture) + TaskStepLetters.Of(index) : string.Empty;
    }

    // This session's places as they are now: the running session's (steps of the incoming step's own task only when it keeps another in
    // focus), or the draft's tasks before a start.
    private IReadOnlyList<SessionSlotChoice> SlotsFor(SessionIncomingItem incoming)
    {
        SessionChecklist current = _tasks.Current;
        if (current.SessionStart is null)
        {
            // Only the draft's tasks whose move to Queued leaves room for the incoming one's actions.
            int incomingActions = IncomingActions(incoming);
            return _tasks.DraftSessionTasks()
                .Select((task, index) => new SessionSlotChoice(index + 1, null, task.Id, task.Title, string.Empty, ColorOf(task)))
                .Where(choice => _tasks.DraftSwapAdmits(choice.TaskId, incomingActions))
                .ToList();
        }

        IReadOnlyList<SessionSlot> slots = SessionActions.Slots(current);
        var choices = new List<SessionSlotChoice>();
        foreach (SessionSlot slot in slots)
        {
            SessionTask task = current.Tasks.First(candidate => candidate.Id == slot.TaskId);
            if (incoming.StepId is not null && slot.TaskId == incoming.TaskId && slots.Count(other => other.TaskId == slot.TaskId) < 2)
            {
                // Its only focused step: swapping it would send the whole task (and the incoming step) to Queued.
                continue;
            }

            if (!_tasks.SwapAdmits(slot, incoming.StepId))
            {
                // Freeing this place wouldn't let it in (over the cap already with approved adds, say).
                continue;
            }

            if (slot.StepId is Guid stepId)
            {
                int index = task.Subtasks.ToList().FindIndex(step => step.Id == stepId);
                string id = StepIdText(task.Id, index);
                string title = (id.Length > 0 ? id : TaskStepLetters.Of(index)) + " · " + task.Subtasks[index].Title;
                choices.Add(new SessionSlotChoice(slot.Number, slot, task.Id, title, "Step of " + TaskToasts.Short(task.Title), ColorOf(task)));
            }
            else
            {
                choices.Add(new SessionSlotChoice(slot.Number, slot, task.Id, task.Title, string.Empty, ColorOf(task)));
            }
        }

        return choices;
    }

    private string? ColorOf(SessionTask task) => _tasks.Categories.FirstOrDefault(candidate => candidate.Id == task.CategoryId)?.Color;

    // A swap: checked first (nothing moves unless freeing that place lets the incoming one in), then the place is freed (its task to
    // Queued, or the step out of focus), the incoming one comes in through the session's own add (or focus) and takes the freed place's
    // number (it goes in front of the task that followed it). Null when done; otherwise why not.
    private async Task<string?> SwapInAsync(SessionIncomingItem incoming, SessionSlotChoice choice)
    {
        const string Gone = "That one already left this session.";
        string stillNoRoom = $"Still no room for “{TaskToasts.Short(incoming.Title)}”: nothing was moved.";
        if (_actions is null)
        {
            return Gone;
        }

        if (choice.Slot is SessionSlot slot)
        {
            if (!_tasks.SwapAdmits(slot, incoming.StepId))
            {
                return _tasks.IsInChecklist(slot.TaskId) ? stillNoRoom : Gone;
            }

            Guid? follower = SessionActions.FollowerOf(_tasks.Current, slot);
            SessionTask? owner = _tasks.Current.Tasks.FirstOrDefault(task => task.Id == slot.TaskId);
            if (!await _tasks.SwapOutAsync(slot))
            {
                return Gone;
            }

            bool admitted;
            if (incoming.StepId is Guid stepId)
            {
                admitted = await _tasks.FocusStepAsync(stepId) == SessionFocusResult.Focused;
            }
            else
            {
                admitted = (await _actions.MoveToSessionAsync([incoming.TaskId])).Moved > 0;
                if (admitted)
                {
                    await _tasks.PlaceBeforeAsync(incoming.TaskId, follower == incoming.TaskId ? null : follower);
                }
            }

            // A step that only left focus is still the task's; a task that left is in Queued: the toast names it whole.
            bool stepOnly = slot.StepId is not null && _tasks.IsInChecklist(slot.TaskId);
            string left = stepOnly ? $"{TaskToasts.Short(choice.Title)} is out of focus" : $"“{TaskToasts.Short(owner?.Title ?? choice.Title)}” → Queued";
            if (!admitted)
            {
                // Something changed between the check and the swap (another device): what was swapped out takes its freed place back
                // through the same cap-checked paths, so nothing is stranded and nothing goes over the cap.
                if (stepOnly && slot.StepId is Guid outStep)
                {
                    await _tasks.FocusStepAsync(outStep);
                    return stillNoRoom;
                }

                if ((await _actions.MoveToSessionAsync([slot.TaskId])).Moved > 0)
                {
                    // Back in the session it was taken off from: no longer "taken off open" (ADR 25), as an undo does.
                    await _tasks.CountBackFromSwapAsync();
                    return stillNoRoom;
                }

                return $"Still no room for “{TaskToasts.Short(incoming.Title)}”, and “{TaskToasts.Short(owner?.Title ?? choice.Title)}” is in Queued now.";
            }

            _actions.Say("Swapped · " + left, explains: true);
            return null;
        }

        // Before a start: the draft's task goes to Queued and the incoming one takes its place in This session's order; checked first.
        List<SessionTask> draft = _tasks.DraftSessionTasks().ToList();
        int at = draft.FindIndex(task => task.Id == choice.TaskId);
        if (at < 0)
        {
            return Gone;
        }

        if (!_tasks.DraftSwapAdmits(choice.TaskId, IncomingActions(incoming)))
        {
            return stillNoRoom;
        }

        Guid? next = at + 1 < draft.Count ? draft[at + 1].Id : null;
        await _actions.MoveAsync([choice.TaskId], TaskBin.Queued);
        if ((await _actions.MoveToSessionAsync([incoming.TaskId])).Moved == 0)
        {
            // Not let in after all: the draft's task goes back to This session (it fitted there a moment ago), nothing stranded.
            await _actions.MoveToSessionAsync([choice.TaskId]);
            return stillNoRoom;
        }

        if (next is Guid before)
        {
            await _actions.ReorderAsync(incoming.TaskId, before);
        }

        _actions.Say($"Swapped · “{TaskToasts.Short(choice.Title)}” → Queued", explains: true);
        return null;
    }

    // The places the incoming task needs (its focused steps, at least one); a step needs one.
    private int IncomingActions(SessionIncomingItem incoming)
    {
        if (incoming.StepId is not null)
        {
            return 1;
        }

        TaskItem? task = _store?.Lists.Tasks.FirstOrDefault(item => item.Id == incoming.TaskId && item.Task.IsLive)?.Task;
        return task?.ActionCount ?? 1;
    }

    // "Add anyway" with its reason: the over-limit add as everywhere (recorded and counted; too short adds nothing).
    private async Task<AddToTodayResult> AddAnywayAsync(SessionIncomingItem incoming, string reason)
    {
        if (_actions is null)
        {
            return AddToTodayResult.NothingToAdd;
        }

        if (reason.Trim().Length < SessionTasksService.MinOverLimitReasonLength)
        {
            return AddToTodayResult.ReasonTooShort;
        }

        if (incoming.StepId is Guid stepId)
        {
            // A place may have come free meanwhile (a tick here or on the phone): then it simply comes into focus, nothing is recorded.
            SessionFocusResult first = await _tasks.FocusStepAsync(stepId);
            if (first != SessionFocusResult.Full)
            {
                return first == SessionFocusResult.Focused ? AddToTodayResult.Added : AddToTodayResult.NothingToAdd;
            }

            if (!await _tasks.RecordOverLimitAddAsync(reason))
            {
                return AddToTodayResult.ReasonTooShort;
            }

            return await _tasks.FocusStepAsync(stepId) == SessionFocusResult.Focused ? AddToTodayResult.Added : AddToTodayResult.NothingToAdd;
        }

        TaskMoveOutcome outcome = await _actions.MoveToSessionAsync([incoming.TaskId], reason);
        return outcome.Moved > 0 ? AddToTodayResult.Added : outcome.NeedsRoom.Count > 0 ? AddToTodayResult.AtLimit : AddToTodayResult.NothingToAdd;
    }

    // Still waiting for a place: a task not in This session (nor taken by the running checklist), a step still open and out of focus.
    private bool StillWaiting(SessionIncomingItem incoming)
    {
        if (incoming.StepId is Guid stepId)
        {
            SessionChecklist current = _tasks.Current;
            return current.SessionStart is not null
                && current.Tasks.Any(task => !task.IsDone && task.Subtasks.Any(step => step.Id == stepId && !step.IsDone))
                && !SessionActions.IsFocused(current, stepId);
        }

        TaskItem? task = _store?.Lists.Tasks.FirstOrDefault(item => item.Id == incoming.TaskId && item.Task.IsLive)?.Task;
        return task is not null && task.Bin is not (TaskBin.Session or TaskBin.Done)
            && !(_tasks.Current.SessionStart is not null && _tasks.IsInChecklist(incoming.TaskId));
    }
}
