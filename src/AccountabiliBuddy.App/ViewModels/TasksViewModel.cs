using System.Collections.ObjectModel;
using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Tasks page (Tasks redesign, spec 03 §B): the Today column (This session, Queued, Finished today) with the add row, and the
/// Shelf (<see cref="ShelfViewModel"/>: To sort, Next, Later, hidden by default behind its rail), each a panel of foldable sections
/// (<see cref="TaskSectionsPanelViewModel"/>); the Today page shows the same rows in its own panel (<see cref="TodayTabPanel"/>, with
/// Reminders due today). One detail card shows whatever row was opened. Every list reads its store again when one changes, once,
/// coalesced, keeping the rows that draw the same. Rows have a check box (where they can be ticked) and a ⋯ menu (add to Today,
/// rename, add subtask, label, move, delete); a delete leaves an Undo bar and a place in Archive › Recently deleted. Nothing here
/// loosens a rule of a running session: a task can leave it only as "taken off open" (ADR 25), a task joins it only through the
/// session's cap (<see cref="SessionTasksService.AddToTodayAsync"/>, <see cref="TaskActionsService.MoveToSessionAsync"/>), and
/// nothing ends or shortens a session.
/// </summary>
public sealed partial class TasksViewModel : ObservableObject, IDisposable
{
    /// <summary>Past sessions' unfinished tasks listed on the page: the last two weeks (the Archive keeps all 90 days).</summary>
    internal static readonly TimeSpan PastShown = TimeSpan.FromDays(14);

    private readonly SessionTasksService _tasks;
    private readonly LaterStore _later;
    private readonly ComebackStore _comebacks;
    private readonly AutoSavedTaskStore _autoSaved;
    private readonly SessionViewModel _session;
    private readonly ConfigurationService _configuration;
    private readonly TimeProvider _time;
    private readonly TaskLayoutStore _layout;
    private TaskRowKey? _selectedKey;
    private bool _refreshing;
    private readonly HashSet<TaskRowKey> _picked = [];
    private readonly TaskFinishQueue _finishes = new();
    private Func<Guid?, Task>? _applyNewCategory;

    // The task list and its services (ADR 76); null while the old lists are still the source.
    private TaskStore? _store;
    private TaskActionsService? _actions;
    private TaskReminderService? _reminders;

    // The rows of Today's sections, rebuilt in place: the Tasks page and the Today page show these same lists.
    private readonly ObservableCollection<TaskListRowViewModel> _sessionRows = new();
    private readonly ObservableCollection<TaskListRowViewModel> _queuedRows = new();
    private readonly ObservableCollection<TaskListRowViewModel> _reminderRows = new();
    private readonly ObservableCollection<TaskListRowViewModel> _doneRows = new();

    // The Shelf's rows, per group key ("bin:sort", "label:…").
    private readonly Dictionary<string, ObservableCollection<TaskListRowViewModel>> _shelfRows = [];

    public TasksViewModel(
        SessionTasksService tasks, LaterStore later, ComebackStore comebacks, AutoSavedTaskStore autoSaved, NotebookViewModel archive,
        SessionViewModel session, ConfigurationService configuration, TimeProvider time, TaskLayoutStore layout)
    {
        _tasks = tasks;
        _later = later;
        _comebacks = comebacks;
        _autoSaved = autoSaved;
        ListPicker = new ListPickerViewModel(tasks, time, PullFromListAsync);
        Archive = archive;
        _session = session;
        _configuration = configuration;
        _time = time;
        _layout = layout;
        Undo = new UndoBarViewModel(tasks);
        CategoryEditor = new CategoryEditorViewModel(tasks);
        TodayPanel = new TaskSectionsPanelViewModel("today", layout);
        TodayTabPanel = new TaskSectionsPanelViewModel("todaytab", layout);
        Shelf = new ShelfViewModel(new TaskSectionsPanelViewModel("shelf", layout), () => IsShelfShown = false, ToggleToday, () => IsTodayHidden);
        _finishes.Changed += OnFinishChanged;
        foreach (TaskSectionsPanelViewModel panel in new[] { TodayPanel, TodayTabPanel, Shelf.Panel })
        {
            panel.Drop = DropAsync;
            panel.RowClick = OnPanelRowClick;
            panel.SelectAllRows = SelectAllIn;
            panel.ClearSelectionOfRows = ClearSelection;
        }

        Notice = NewCategoryName = TodayDateText = TodayRailText = string.Empty;
        _tasks.Changed += OnStoreChanged;
        _comebacks.Changed += OnStoreChanged;
        _autoSaved.Changed += OnStoreChanged;
        _later.Changed += OnStoreChanged;
        Shelf.Changed += OnStoreChanged;
        _layout.Changed += OnLayoutChanged;
        _session.PropertyChanged += OnSessionPropertyChanged;
        if (_session.Focus is not null)
        {
            _session.Focus.PropertyChanged += OnFocusPropertyChanged;
        }

        Archive.Host = this;
        Archive.IsShown = () => ShowsArchive;
        Archive.Refresh();
        Refresh();
    }

    /// <summary>
    /// From now on the Shelf follows the task list (its views, labels and weekly tidy-up), Queued shows during a session, and Today
    /// lists the reminders due today (ADR 76). Called once by the app when the task list is the source.
    /// </summary>
    public void UseTaskModel(TaskStore store, TaskActionsService actions, TaskViewService views, TaskTidyUpService tidyUp, TaskReminderService reminders)
    {
        _store = store;
        _actions = actions;
        _reminders = reminders;
        Shelf.UseTaskModel(store, views, tidyUp);
        store.Changed += OnStoreChanged;
        reminders.PendingChanged += OnStoreChanged;
        QueueRefresh();
    }

    /// <summary>The Today column's sections on the Tasks page: This session, Queued, Finished today.</summary>
    public TaskSectionsPanelViewModel TodayPanel { get; }

    /// <summary>The Today page's sections: This session, Queued, Reminders due today, Finished today (the same rows as <see cref="TodayPanel"/>).</summary>
    public TaskSectionsPanelViewModel TodayTabPanel { get; }

    public ShelfViewModel Shelf { get; }

    /// <summary>Compact rows (32 DIP) on both columns and on Today; remembered on this PC.</summary>
    public bool IsCompact => _layout.Current.Compact;

    [RelayCommand]
    private Task UseNormalRows() => _layout.SetCompactAsync(false);

    [RelayCommand]
    private Task UseCompactRows() => _layout.SetCompactAsync(true);

    /// <summary>Today's share of the page in percent while both columns show (30 to 70, 46 by default); remembered on this PC.</summary>
    public int TodaySplit => _layout.Current.SplitNow;

    /// <summary>The resize handle let go (or double-clicked: back to 46).</summary>
    public Task SetSplitAsync(int percent) => _layout.SetSplitAsync(percent);

    /// <summary>The Shelf column shows; hidden by default, remembered until the app restarts (shared behaviour §1).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsShelfRail), nameof(ShowsToday), nameof(ShowsTodayRail), nameof(ShowsSplitHandle))]
    public partial bool IsShelfShown { get; set; }

    /// <summary>The Today column folded into its rail (the Shelf takes the page); only while the Shelf shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsToday), nameof(ShowsTodayRail), nameof(ShowsSplitHandle))]
    public partial bool IsTodayHidden { get; set; }

    public bool ShowsShelfRail => !IsShelfShown;

    public bool ShowsToday => !(IsTodayHidden && IsShelfShown);

    public bool ShowsTodayRail => IsTodayHidden && IsShelfShown;

    public bool ShowsSplitHandle => IsShelfShown && !IsTodayHidden;

    /// <summary>"Tue 11/10", after Today's title.</summary>
    [ObservableProperty]
    public partial string TodayDateText { get; set; }

    /// <summary>The Today rail's line: "3 in focus".</summary>
    [ObservableProperty]
    public partial string TodayRailText { get; set; }

    /// <summary>Ctrl+\ and the rails' arrows: shows or hides the Shelf (hiding it brings Today back).</summary>
    [RelayCommand]
    private void ToggleShelf()
    {
        IsShelfShown = !IsShelfShown;
        if (!IsShelfShown)
        {
            IsTodayHidden = false;
        }
    }

    [RelayCommand]
    private void ShowToday() => IsTodayHidden = false;

    // "Shelf ▾" › Hide Today / Show Today.
    private void ToggleToday()
    {
        if (IsTodayHidden)
        {
            IsTodayHidden = false;
            return;
        }

        IsShelfShown = true;
        IsTodayHidden = true;
    }

    /// <summary>A row was opened (also from Today, which has no detail card): the window shows the Tasks page.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Tasks was opened again (another tab before): header clicks are forgotten, the pins and Expand / Collapse all decide.</summary>
    public void TasksOpened()
    {
        TodayPanel.ForgetFolds();
        Shelf.Panel.ForgetFolds();
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsCompact));
        OnPropertyChanged(nameof(TodaySplit));
    }

    /// <summary>The Notebook, restyled as the Archive row at the bottom.</summary>
    public NotebookViewModel Archive { get; }

    /// <summary>The bar after a delete: "Deleted “…” · Undo".</summary>
    public UndoBarViewModel Undo { get; }

    /// <summary>The Categories button's editor: rename, recolour, add and delete (the same rows as Today's tag button).</summary>
    public CategoryEditorViewModel CategoryEditor { get; }

    /// <summary>The Categories editor is open under the header.</summary>
    [ObservableProperty]
    public partial bool IsManagingCategories { get; set; }

    /// <summary>How many rows are selected (Ctrl/Shift+click, Ctrl+A); the bulk menu (3.5) acts on them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickedText), nameof(HasPicked))]
    public partial int PickedCount { get; set; }

    /// <summary>"3 selected".</summary>
    public string PickedText => $"{PickedCount} selected";

    public bool HasPicked => PickedCount > 0;

    /// <summary>A picker's "New category…" was chosen: the name box shows under the header.</summary>
    [ObservableProperty]
    public partial bool IsNamingCategory { get; set; }

    [ObservableProperty]
    public partial string NewCategoryName { get; set; }

    [ObservableProperty]
    public partial bool IsArchiveOpen { get; set; }

    /// <summary>The detail card's item; null shows its empty note.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    public partial TaskDetailViewModel? Detail { get; set; }

    /// <summary>Why something was not done, e.g. "That task is no longer there."</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string Notice { get; set; }

    public bool HasNotice => Notice.Length > 0;

    /// <summary>"Add to Today" at three tasks: asking before anything is added.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPending))]
    public partial PendingMoveViewModel? Pending { get; set; }

    public bool HasPending => Pending is not null;

    public bool HasDetail => Detail is not null;

    /// <summary>
    /// The user opened the detail card (a row click, Rename, Add subtask). Only a page under 980 wide hides the card while this
    /// is false; a wide page always shows it.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDetailOpen { get; set; }

    /// <summary>The detail card's close button and Esc (narrow pages): saves what is being edited, then hides the card and deselects.</summary>
    [RelayCommand]
    private void CloseDetail()
    {
        Detail?.Flush();
        IsDetailOpen = false;
        _selectedKey = null;
    }

    /// <summary>The Archive (the Notebook) shows open under the Shelf.</summary>
    public bool ShowsArchive => IsArchiveOpen;

    /// <summary>A session runs: the running checklist is the list, and the page offers no way out of it.</summary>
    public bool IsSessionLive => _tasks.IsLive;

    /// <summary>"Start with this" can start now: no session runs and Today's Start is ready.</summary>
    public bool CanStartSession => !_tasks.IsLive && _session.QuickStart.StartCommand.CanExecute(null);

    /// <summary>The view should put the keyboard in a box of the detail card: "title" or "step".</summary>
    public event EventHandler<string>? FocusRequested;

    partial void OnIsArchiveOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowsArchive));
        Archive.ShowIfStale();
    }

    public void Dispose()
    {
        _tasks.Changed -= OnStoreChanged;
        _comebacks.Changed -= OnStoreChanged;
        _autoSaved.Changed -= OnStoreChanged;
        _later.Changed -= OnStoreChanged;
        Shelf.Changed -= OnStoreChanged;
        _layout.Changed -= OnLayoutChanged;
        if (_store is not null)
        {
            _store.Changed -= OnStoreChanged;
        }

        if (_reminders is not null)
        {
            _reminders.PendingChanged -= OnStoreChanged;
        }

        _session.PropertyChanged -= OnSessionPropertyChanged;
        if (_session.Focus is not null)
        {
            _session.Focus.PropertyChanged -= OnFocusPropertyChanged;
        }

        Undo.Dispose();
        Detail?.Dispose();
    }

    [RelayCommand]
    private void ToggleArchive() => IsArchiveOpen = !IsArchiveOpen;

    [RelayCommand]
    private void ToggleManageCategories() => IsManagingCategories = !IsManagingCategories;

    [RelayCommand]
    private void DismissNotice() => Notice = string.Empty;

    // "New category…" in a picker: the name box takes the keyboard; Enter makes the category and applies it to what the picker was for.
    private void AskNewCategory(Func<Guid?, Task> apply)
    {
        _applyNewCategory = apply;
        NewCategoryName = string.Empty;
        IsNamingCategory = true;
        FocusRequested?.Invoke(this, "newCategory");
    }

    /// <summary>Enter in the name box: a new category in the next colour no category has (the same name in any case is the one that exists), then applied.</summary>
    [RelayCommand]
    private async Task SaveNewCategoryAsync()
    {
        string name = NewCategoryName.Trim();
        Func<Guid?, Task>? apply = _applyNewCategory;
        Func<string, Task>? applyLabel = _applyNewLabel;
        CancelNewCategory();
        if (applyLabel is not null)
        {
            _applyNewLabel = applyLabel;
            await SaveNewLabelAsync(name);
            return;
        }

        if (name.Length == 0 || apply is null)
        {
            return;
        }

        if (await _tasks.AdoptCategoryAsync(name, null) is Guid id)
        {
            await apply(id);
        }
    }

    /// <summary>Esc or the ✕ in the name box: nothing is added.</summary>
    [RelayCommand]
    private void CancelNewCategory()
    {
        _applyNewCategory = null;
        _applyNewLabel = null;
        OnPropertyChanged(nameof(NamePrompt));
        NewCategoryName = string.Empty;
        IsNamingCategory = false;
    }

    /// <summary>A row clicked: it shows in the detail card.</summary>
    public void Select(TaskRowKey key)
    {
        if (_selectedKey == key && Detail is not null)
        {
            return;
        }

        Detail?.Flush();
        _selectedKey = key;
        ShowSelected();
    }

    // Opens a row's task in the open-task card (a plain click on a closed row, Enter, a row opened on Today).
    private void SelectRow(TaskListRowViewModel row)
    {
        IsDetailOpen = true;
        Select(row.Key);
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A This-session row dropped by its handle: before or after another task (taking that task's category, as on Today).</summary>
    public Task MoveSessionRowAsync(Guid id, Guid targetId, bool after)
    {
        return _tasks.MoveToAsync(id, targetId, after);
    }

    // ---- What the detail card and the menus do ----

    internal Task RenameAsync(TaskRowKey key, string title)
    {
        return key.Kind switch
        {
            TaskRowKind.Session => _tasks.RenameAsync(key.Id, title),
            TaskRowKind.Later => EditLaterAsync(key.Id, item => item with { Title = title }),
            TaskRowKind.ComeBack => _comebacks.EditAsync(key.Id, title),
            TaskRowKind.Queued => ActAsync(actions => actions.RenameAsync(key.Id, title)),
            TaskRowKind.AutoSaved => Task.CompletedTask, // an Auto-saved row has no editor
            _ => _tasks.IsInChecklist(key.Id) ? _tasks.RenameAsync(key.Id, title) : _tasks.EditHistoryAsync(key.Id, title),
        };
    }

    internal Task SetNoteAsync(TaskRowKey key, string note)
    {
        return key.Kind switch
        {
            TaskRowKind.Session => _tasks.SetNoteAsync(key.Id, note),
            TaskRowKind.Later => EditLaterAsync(key.Id, item => item with { Note = SessionChecklist.CleanNote(note) }),
            TaskRowKind.Queued => ActAsync(actions => actions.SetNoteAsync(key.Id, note)),
            _ => Task.CompletedTask,
        };
    }

    internal Task SetEstimateAsync(TaskRowKey key, int? minutes)
    {
        return key.Kind switch
        {
            TaskRowKind.Session => _tasks.SetEstimateAsync(key.Id, minutes),
            TaskRowKind.Later => EditLaterAsync(key.Id, item => item with { EstimateMinutes = SessionChecklist.CleanEstimate(minutes) }),
            TaskRowKind.Queued => ActAsync(actions => actions.SetTimeLimitAsync([key.Id], minutes)),
            _ => Task.CompletedTask,
        };
    }

    internal Task SetPlannedForAsync(TaskRowKey key, PlannedSlot? slot)
    {
        return key.Kind switch
        {
            TaskRowKind.Session => _tasks.SetPlannedForAsync(key.Id, slot),
            TaskRowKind.Later => EditLaterAsync(key.Id, item => item with { PlannedFor = slot }),
            _ => Task.CompletedTask,
        };
    }

    internal Task AddStepAsync(TaskRowKey key, string title)
    {
        return key.Kind switch
        {
            TaskRowKind.Session => _tasks.AddAsync(title, key.Id),
            TaskRowKind.Later => EditLaterAsync(key.Id, item => item with { Subtasks = [.. item.Subtasks ?? [], new SessionSubtask(Guid.NewGuid(), title, null)] }),
            TaskRowKind.Done or TaskRowKind.Past => _tasks.IsInChecklist(key.Id) ? _tasks.AddAsync(title, key.Id) : _tasks.AddHistoryStepAsync(key.Id, title),
            TaskRowKind.Queued => ActAsync(actions => actions.AddStepAsync(key.Id, title)),
            _ => Task.CompletedTask,
        };
    }

    internal Task ToggleStepAsync(TaskRowKey key, Guid stepId)
    {
        DateTimeOffset now = _time.GetUtcNow();
        return key.Kind switch
        {
            TaskRowKind.Session => _tasks.ToggleAsync(stepId),
            TaskRowKind.Later => EditLaterAsync(key.Id, item => item with
            {
                Subtasks = (item.Subtasks ?? []).Select(step => step.Id == stepId ? step with { DoneAt = step.IsDone ? null : now } : step).ToList(),
            }),
            TaskRowKind.Queued => ActAsync(actions => actions.TickStepAsync(key.Id, stepId)),
            _ => Task.CompletedTask,
        };
    }

    internal Task RemoveStepAsync(TaskRowKey key, Guid stepId)
    {
        return key.Kind switch
        {
            TaskRowKind.Later => _tasks.DeleteLaterStepAsync(key.Id, stepId),
            TaskRowKind.Queued => ActAsync(actions => actions.RemoveStepAsync(key.Id, stepId)),
            _ => _tasks.IsInChecklist(stepId) ? _tasks.RemoveAsync(stepId) : _tasks.EditHistoryAsync(stepId, null),
        };
    }

    internal Task RenameStepAsync(TaskRowKey key, Guid stepId, string title)
    {
        return key.Kind switch
        {
            TaskRowKind.Later => EditLaterAsync(key.Id, item => item with
            {
                Subtasks = (item.Subtasks ?? []).Select(step => step.Id == stepId ? step with { Title = title } : step).ToList(),
            }),
            TaskRowKind.Queued => ActAsync(actions => actions.RenameStepAsync(key.Id, stepId, title)),
            _ => _tasks.IsInChecklist(stepId) ? _tasks.RenameAsync(stepId, title) : _tasks.EditHistoryAsync(stepId, title),
        };
    }

    internal Task AddStepAfterAsync(TaskRowKey key, Guid stepId, string title)
    {
        return key.Kind switch
        {
            TaskRowKind.Later => EditLaterAsync(key.Id, item => item with { Subtasks = InsertAfter(item.Subtasks ?? [], stepId, new SessionSubtask(Guid.NewGuid(), title, null)) }),
            TaskRowKind.Queued => ActAsync(actions => actions.AddStepAsync(key.Id, title, stepId)),
            _ => _tasks.IsInChecklist(stepId) ? _tasks.AddSubtaskAfterAsync(stepId, title) : _tasks.AddHistoryStepAsync(key.Id, title, stepId),
        };
    }

    private static List<SessionSubtask> InsertAfter(IReadOnlyList<SessionSubtask> steps, Guid stepId, SessionSubtask added)
    {
        List<SessionSubtask> list = steps.ToList();
        int at = list.FindIndex(step => step.Id == stepId);
        list.Insert(at < 0 ? list.Count : at + 1, added);
        return list;
    }

    internal Task EditLaterAsync(Guid id, Func<SyncLaterItem, SyncLaterItem> change)
    {
        return _later.EditAsync(id, change);
    }

    /// <summary>Puts a task, Later item or come-back in a category (null: none), wherever it is. Nothing here is locked in a session: a relabel loosens nothing.</summary>
    internal Task SetCategoryAsync(TaskRowKey key, Guid? categoryId)
    {
        TaskCategory? category = _tasks.Categories.FirstOrDefault(item => item.Id == categoryId);
        return key.Kind switch
        {
            TaskRowKind.Later => EditLaterAsync(key.Id, item => item with { Category = category?.Name, CategoryColor = category?.Color }),
            TaskRowKind.ComeBack => _comebacks.SetCategoryAsync(key.Id, category?.Name, category?.Color),
            TaskRowKind.Queued => ActAsync(actions => actions.SetLabelAsync([key.Id], LabelIdOf(categoryId))),
            TaskRowKind.AutoSaved => Task.CompletedTask, // an Auto-saved row has no category
            _ => _tasks.IsInChecklist(key.Id) ? _tasks.SetCategoryAsync(key.Id, categoryId) : _tasks.SetHistoryCategoryAsync(key.Id, categoryId),
        };
    }

    /// <summary>"Move to Later": the task leaves this session (counted as taken off open while it runs) and shows on the Later list. A done or past one is copied.</summary>
    internal async Task MoveToLaterAsync(TaskRowKey key)
    {
        Guid? laterId = key.Kind switch
        {
            TaskRowKind.Session => await _tasks.MoveToLaterAsync(key.Id),
            TaskRowKind.ComeBack => await _tasks.MoveComebackToLaterAsync(key.Id),
            TaskRowKind.Done or TaskRowKind.Past => await _tasks.MoveHistoryTaskToLaterAsync(key.Id),
            TaskRowKind.Queued => await MoveQueuedAsync(key.Id, TaskBin.Later),
            _ => null,
        };
        if (laterId is Guid id)
        {
            Select(new TaskRowKey(TaskRowKind.Later, id));
        }
    }

    /// <summary>"Come back to": the task is parked as a come-back (it leaves this session as taken off open, or is copied if it is done).</summary>
    internal async Task MoveToComebackAsync(TaskRowKey key)
    {
        switch (key.Kind)
        {
            case TaskRowKind.Session:
                await _tasks.ParkAsync(key.Id);
                break;
            case TaskRowKind.Later:
                await _tasks.MoveLaterToComebackAsync(key.Id);
                break;
            case TaskRowKind.Done or TaskRowKind.Past:
                await _tasks.ParkHistoryTaskAsync(key.Id);
                break;
            case TaskRowKind.Queued:
                await MoveQueuedAsync(key.Id, TaskBin.Sort);
                break;
        }
    }

    /// <summary>
    /// "Add to Today": a Later item, a come-back or a done / unfinished task becomes a task of today's list: the next session's
    /// while none runs, the running checklist (planning or focus) while one does. It comes in open (a done one as a copy with a
    /// new id; the record keeps the done one). At three tasks nothing is added until the card asks: add it anyway with a reason,
    /// or swap it for one of today's open tasks.
    /// </summary>
    internal async Task AddToTodayAsync(TaskRowKey key)
    {
        Notice = string.Empty;
        await RunAddToTodayAsync(key, null);
    }

    private async Task<AddToTodayResult> RunAddToTodayAsync(TaskRowKey key, string? reason)
    {
        (AddToTodayResult result, Guid? taskId) = key.Kind switch
        {
            TaskRowKind.Later => await _tasks.MoveFromLaterAsync(key.Id, reason),
            TaskRowKind.ComeBack => await _tasks.AddComebackToTodayAsync(key.Id, reason),
            TaskRowKind.Done or TaskRowKind.Past => await _tasks.AddHistoryTaskToTodayAsync(key.Id, reason),
            _ => (AddToTodayResult.NothingToAdd, null),
        };
        switch (result)
        {
            case AddToTodayResult.Added:
                Pending = null;
                if (taskId is Guid id)
                {
                    Select(new TaskRowKey(TaskRowKind.Session, id));
                }

                break;
            case AddToTodayResult.AtLimit:
                Pending = BuildPending(key);
                break;
            case AddToTodayResult.NothingToAdd:
                Notice = "That task is no longer there.";
                break;
        }

        return result;
    }

    /// <summary>"From your lists" by the add box.</summary>
    public ListPickerViewModel ListPicker { get; }

    // An item chosen by the add box: "Add to Today" for its row, with the same three-task card.
    private Task PullFromListAsync(PulledListKind kind, Guid id, string title)
    {
        return kind switch
        {
            PulledListKind.Later => AddToTodayAsync(new TaskRowKey(TaskRowKind.Later, id)),
            PulledListKind.ComeBack => AddToTodayAsync(new TaskRowKey(TaskRowKind.ComeBack, id)),
            PulledListKind.Unfinished => AddToTodayAsync(new TaskRowKey(TaskRowKind.Past, id)),
            PulledListKind.DoneToday or PulledListKind.Archive => AddToTodayAsync(new TaskRowKey(TaskRowKind.Done, id)),
            PulledListKind.AutoSaved => AddAutoSavedToTodayAsync(id, title),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    /// <summary>"Add to Today" for an Auto-saved row: same path and three-task rule as Later's.</summary>
    private async Task AddAutoSavedToTodayAsync(Guid id, string title)
    {
        Notice = string.Empty;
        await RunAddAutoSavedAsync(id, title, null);
    }

    private async Task<AddToTodayResult> RunAddAutoSavedAsync(Guid id, string title, string? reason)
    {
        (AddToTodayResult result, Guid? taskId) = await _tasks.AddAutoSavedToTodayAsync(id, reason);
        switch (result)
        {
            case AddToTodayResult.Added:
                Pending = null;
                if (taskId is Guid added)
                {
                    Select(new TaskRowKey(TaskRowKind.Session, added));
                }

                break;
            case AddToTodayResult.AtLimit:
                Pending = BuildPending(title, text => RunAddAutoSavedAsync(id, title, text), () => _autoSaved.Items.Any(item => item.Id == id));
                break;
            case AddToTodayResult.NothingToAdd:
                Notice = "That task is no longer there.";
                break;
        }

        return result;
    }

    /// <summary>An Auto-saved row's ⋯ menu: Add to Today (the three-task rule applies), Later, Delete.</summary>
    private IReadOnlyList<TaskMenuEntry> AutoSavedMenu(Guid id, string title)
    {
        var key = new TaskRowKey(TaskRowKind.AutoSaved, id);
        var entries = new List<TaskMenuEntry>
        {
            TaskMenuEntry.Async("Add to Today", () => AddAutoSavedToTodayAsync(id, title), MaterialIconKind.CalendarToday),
            TaskMenuEntry.Async("Later", () => MoveAutoSavedToLaterAsync(id, title), MaterialIconKind.ClockOutline),
        };
        // ADR 72: labels and where it is kept, like every other row.
        if (LabelsEntries(key) is { } labels)
        {
            entries.Add(TaskMenuEntry.Group("Labels", labels, MaterialIconKind.TagMultipleOutline));
        }

        if (KeepOnEntries(key) is { } keepOn)
        {
            entries.Add(TaskMenuEntry.Group("Keep on…", keepOn, MaterialIconKind.Devices));
        }

        entries.Add(TaskMenuEntry.Separator);
        entries.Add(TaskMenuEntry.Async("Delete", () => _tasks.DeleteAutoSavedAsync(id), MaterialIconKind.DeleteOutline));
        return entries;
    }

    private async Task MoveAutoSavedToLaterAsync(Guid id, string title)
    {
        if (await _later.AddAsync(title, null, null, null, null, null, null) is not null)
        {
            await _autoSaved.RemoveAsync(id);
        }
    }

    private PendingMoveViewModel BuildPending(TaskRowKey key)
    {
        return BuildPending(Read(key)?.Title ?? "this task", reason => RunAddToTodayAsync(key, reason), () => Read(key) is not null);
    }

    private PendingMoveViewModel BuildPending(string title, Func<string?, Task<AddToTodayResult>> run, Func<bool> sourceExists)
    {
        List<(Guid, string)> choices = _tasks.Current.Tasks.Where(task => !task.IsDone).Select(task => (task.Id, task.Title)).ToList();
        return new PendingMoveViewModel(
            $"Today already has {_tasks.Current.Tasks.Count} tasks. Add “{title}” anyway?",
            _tasks.Comebacks is not null,
            choices,
            reason => run(reason),
            async parkedId =>
            {
                if (!sourceExists())
                {
                    // It left in the meantime: nothing is parked for it.
                    Pending = null;
                    Notice = "That task is no longer there.";
                    return;
                }

                await _tasks.ParkAsync(parkedId);
                await run(null);
            },
            () => Pending = null);
    }

    /// <summary>
    /// "Start with this": the same Start as Today's. The task becomes the one the session opens on (the on-screen bar's pick,
    /// as "pick" does on Today), after being brought in from Later when it was there.
    /// </summary>
    internal async Task StartWithThisAsync(TaskRowKey key)
    {
        if (_tasks.IsLive)
        {
            return;
        }

        Guid taskId = key.Id;
        if (key.Kind == TaskRowKind.Later)
        {
            (AddToTodayResult result, Guid? movedId) = await _tasks.MoveFromLaterAsync(key.Id);
            if (movedId is not Guid id)
            {
                if (result == AddToTodayResult.AtLimit)
                {
                    Pending = BuildPending(key);
                }

                return;
            }

            taskId = id;
        }

        if (_tasks.Current.CurrentId != taskId && _tasks.Current.Tasks.Any(task => task.Id == taskId && !task.IsDone))
        {
            await _tasks.PickAsync(taskId);
        }

        QuickSessionViewModel start = _session.QuickStart;
        if (start.StartCommand.CanExecute(null))
        {
            start.StartCommand.Execute(null);
        }
    }

    /// <summary>Deletes a task, Later item or come-back: it leaves the list, an Undo bar shows, and Archive › Recently deleted keeps it for 30 days.</summary>
    internal Task DeleteAsync(TaskRowKey key)
    {
        return key.Kind switch
        {
            TaskRowKind.Later => _tasks.DeleteLaterAsync(key.Id),
            TaskRowKind.ComeBack => _tasks.DeleteComebackAsync(key.Id),
            TaskRowKind.Session => _tasks.RemoveAsync(key.Id),
            TaskRowKind.AutoSaved => _tasks.DeleteAutoSavedAsync(key.Id),
            TaskRowKind.Queued => ActAsync(actions => actions.DeleteAsync([key.Id])),
            _ => _tasks.IsInChecklist(key.Id) ? _tasks.RemoveAsync(key.Id) : _tasks.EditHistoryAsync(key.Id, null),
        };
    }

    internal Task TickComeBackAsync(Guid id) => _comebacks.TickAsync(id);

    // ---- The ⋯ menu ----

    /// <summary>
    /// The entries of a task's ⋯ menu: Add to Today (first, where it applies), Rename, Add subtask, Label (a category or none),
    /// Move to… (Later, Come back to, Put in plan: only the moves that make sense for that row) and Delete. <paramref name="rename"/> and <paramref name="addStep"/> say how the place the row is shown
    /// edits in place (the Archive); without them the detail card's boxes take the keyboard.
    /// </summary>
    internal IReadOnlyList<TaskMenuEntry> MenuFor(TaskRowKey key, Action? rename = null, Action? addStep = null)
    {
        var entries = new List<TaskMenuEntry>();
        if (CanAddToToday(key))
        {
            entries.Add(TaskMenuEntry.Async("Add to Today", () => AddToTodayAsync(key), MaterialIconKind.CalendarToday));
        }

        // A Queued task while a session runs joins it only through the session's cap.
        if (key.Kind == TaskRowKind.Queued && StoredTask(key.Id)?.Bin == TaskBin.Queued)
        {
            entries.Add(TaskMenuEntry.Async("Move to This session", () => MoveQueuedToSessionAsync(key.Id), MaterialIconKind.ArrowLeftBoldOutline));
        }

        entries.Add(TaskMenuEntry.Do("Rename", rename ?? (() => BeginDetailEdit(key, "title")), MaterialIconKind.PencilOutline));
        if (key.Kind is not (TaskRowKind.ComeBack or TaskRowKind.Queued))
        {
            entries.Add(TaskMenuEntry.Do("Add subtask", addStep ?? (() => BeginDetailEdit(key, "step")), MaterialIconKind.PlaylistPlus));
        }

        entries.Add(TaskMenuEntry.Group("Category", LabelEntries(key), MaterialIconKind.ShapeOutline));
        // ADR 72: labels (several, plain names) and where the item is kept.
        if (LabelsEntries(key) is { } labels)
        {
            entries.Add(TaskMenuEntry.Group("Labels", labels, MaterialIconKind.TagMultipleOutline));
        }

        if (KeepOnEntries(key) is { } keepOn)
        {
            entries.Add(TaskMenuEntry.Group("Keep on…", keepOn, MaterialIconKind.Devices));
        }

        List<TaskMenuEntry> moves = MoveEntries(key);
        if (moves.Count > 0)
        {
            entries.Add(TaskMenuEntry.Group("Move to…", moves, MaterialIconKind.ArrowRightBox));
        }

        entries.Add(TaskMenuEntry.Separator);
        entries.Add(TaskMenuEntry.Async("Delete", () => DeleteAsync(key), MaterialIconKind.DeleteOutline));
        return entries;
    }

    private void BeginDetailEdit(TaskRowKey key, string target)
    {
        IsDetailOpen = true;
        Select(key);
        FocusRequested?.Invoke(this, target);
    }

    // "Add to Today" is the first entry of the menu: for a Later item, an open come-back, and a done or unfinished task of the
    // sessions' records (one ticked in the running checklist is already on today's list).
    private bool CanAddToToday(TaskRowKey key)
    {
        if (key.Kind is TaskRowKind.Done or TaskRowKind.Past && _tasks.IsInChecklist(key.Id))
        {
            return false;
        }

        bool isOpenComeBack = key.Kind == TaskRowKind.ComeBack && _comebacks.Open.Any(item => item.Id == key.Id);
        return key.Kind is TaskRowKind.Later or TaskRowKind.Done or TaskRowKind.Past || isOpenComeBack;
    }

    private List<TaskMenuEntry> MoveEntries(TaskRowKey key)
    {
        var moves = new List<TaskMenuEntry>();
        if (key.Kind is TaskRowKind.Done or TaskRowKind.Past && _tasks.IsInChecklist(key.Id))
        {
            // Ticked in the running checklist: the moves look in the sessions' records only, so none of them would find it.
            return moves;
        }

        bool isOpenSessionTask = key.Kind == TaskRowKind.Session;
        if (key.Kind != TaskRowKind.Later)
        {
            moves.Add(TaskMenuEntry.Async("Later", () => MoveToLaterAsync(key), MaterialIconKind.ClockOutline));
        }

        if (key.Kind != TaskRowKind.ComeBack)
        {
            moves.Add(TaskMenuEntry.Async("To sort", () => MoveToComebackAsync(key), MaterialIconKind.FlagOutline));
        }

        // Put in plan is for the next session's tasks and Later; a running session's task can't be planned for later.
        if (key.Kind == TaskRowKind.Later || isOpenSessionTask && !_tasks.IsLive)
        {
            moves.Add(TaskMenuEntry.Group("Put in plan", PlanEntries(key), MaterialIconKind.ViewAgendaOutline));
        }

        return moves;
    }

    private List<TaskMenuEntry> PlanEntries(TaskRowKey key)
    {
        PlannedSlot? current = Read(key)?.PlannedFor;
        var entries = new List<TaskMenuEntry>();
        foreach (PlanDayChoiceViewModel day in BuildPlanDays(current, slot => SetPlannedForAsync(key, slot)))
        {
            entries.Add(TaskMenuEntry.Group(day.Label, day.Parts.Select(part => new TaskMenuEntry(part.Text, part.PickCommand, IsChecked: part.IsSelected)).ToList()));
        }

        if (entries.Count == 0)
        {
            entries.Add(new TaskMenuEntry("Nothing in the plan for the coming week"));
        }

        if (current is not null)
        {
            entries.Add(TaskMenuEntry.Separator);
            entries.Add(TaskMenuEntry.Async("Not in the plan", () => SetPlannedForAsync(key, null)));
        }

        return entries;
    }

    // The picker of ⋯ › Label and of a row's category chip: the one the row has is marked.
    private List<TaskMenuEntry> LabelEntries(TaskRowKey key)
    {
        return CategoryChoices(CategoryNameOf(key), markCurrent: true, id => SetCategoryAsync(key, id));
    }

    // One entry per category (with its dot) and "No category"; <paramref name="apply"/> gets the choice (null: none).
    // Select mode's bulk picker marks none, since the picked rows may differ.
    private List<TaskMenuEntry> CategoryChoices(string? current, bool markCurrent, Func<Guid?, Task> apply)
    {
        IReadOnlyList<DeviceCategories> others = _tasks.OtherCategories();
        if (others.Count > 0)
        {
            return GroupedCategoryChoices(others, current, markCurrent, apply);
        }

        var entries = new List<TaskMenuEntry>();
        foreach (TaskCategory category in _tasks.Categories)
        {
            Guid id = category.Id;
            entries.Add(new TaskMenuEntry(
                category.Name,
                new AsyncRelayCommand(() => apply(id)),
                IsChecked: markCurrent && string.Equals(current, category.Name, StringComparison.OrdinalIgnoreCase),
                Dot: TaskCategoryBrushes.Of(category.Color)));
        }

        entries.Add(new TaskMenuEntry("No category", new AsyncRelayCommand(() => apply(null)), IsChecked: markCurrent && current is null));
        entries.Add(NewCategoryEntry(apply));
        return entries;
    }

    // With other devices' categories to offer: "No category", then "This PC" and its categories, then each other device's name with
    // its categories under it. Picking another device's category takes it into this PC's list first (the same name, any case, is this
    // PC's already and keeps its colour), then applies it like any other choice.
    private List<TaskMenuEntry> GroupedCategoryChoices(IReadOnlyList<DeviceCategories> others, string? current, bool markCurrent, Func<Guid?, Task> apply)
    {
        var entries = new List<TaskMenuEntry>
        {
            new("No category", new AsyncRelayCommand(() => apply(null)), IsChecked: markCurrent && current is null),
        };
        if (_tasks.Categories.Count > 0)
        {
            entries.Add(new TaskMenuEntry("This PC", IsHeading: true));
            foreach (TaskCategory category in _tasks.Categories)
            {
                Guid id = category.Id;
                entries.Add(new TaskMenuEntry(
                    category.Name,
                    new AsyncRelayCommand(() => apply(id)),
                    IsChecked: markCurrent && string.Equals(current, category.Name, StringComparison.OrdinalIgnoreCase),
                    Dot: TaskCategoryBrushes.Of(category.Color),
                    Indented: true));
            }
        }

        foreach (DeviceCategories device in others)
        {
            entries.Add(new TaskMenuEntry(device.DeviceName, IsHeading: true));
            foreach (SyncCategory category in device.Categories)
            {
                SyncCategory taken = category;
                entries.Add(new TaskMenuEntry(
                    taken.Name,
                    new AsyncRelayCommand(async () =>
                    {
                        Guid? adopted = await _tasks.AdoptCategoryAsync(taken.Name, taken.Color);
                        await apply(adopted);
                    }),
                    Dot: TaskCategoryBrushes.Of(taken.Color),
                    Indented: true));
            }
        }

        entries.Add(NewCategoryEntry(apply));
        return entries;
    }

    private TaskMenuEntry NewCategoryEntry(Func<Guid?, Task> apply)
    {
        return TaskMenuEntry.Do("New category…", () => AskNewCategory(apply), MaterialIconKind.Plus);
    }

    private string? CategoryNameOf(TaskRowKey key)
    {
        switch (key.Kind)
        {
            case TaskRowKind.Later:
                return _later.Items.FirstOrDefault(item => item.Id == key.Id)?.Category;
            case TaskRowKind.ComeBack:
                return _comebacks.All.FirstOrDefault(item => item.Id == key.Id)?.Category;
            case TaskRowKind.Queued:
                return LabelOf(StoredTask(key.Id)?.LabelId)?.Name;
            default:
                SessionTask? task = FindTask(key.Id);
                return task is null ? null : TaskCategoryDisplay.Resolve(_tasks.Categories, task.CategoryId, task.Category, task.CategoryColor).Name;
        }
    }

    // ---- Put in plan ----

    /// <summary>The coming week's day parts that have not started, to put a task in, a day at a time, sleep left out; the part the task is in now is marked.</summary>
    internal IReadOnlyList<PlanDayChoiceViewModel> BuildPlanDays(PlannedSlot? current, Func<PlannedSlot?, Task> pick)
    {
        DateTime now = LocalNow();
        var window = new LocalWindow(now.Date.AddDays(-1), now.Date.AddDays(9));
        return _configuration.Current.DayPlan.PartsIn(window)
            .Where(part => part.Category.Behavior != CategoryBehavior.Sleep && part.Window.Start > now
                && part.Window.Start.Date < now.Date.AddDays(7))
            .GroupBy(part => DateOnly.FromDateTime(part.Window.Start))
            .Select(day => new PlanDayChoiceViewModel(
                Capitalised(DayLabel(day.Key, DateOnly.FromDateTime(now))),
                day.Select(part =>
                {
                    var slot = PlannedSlot.At(day.Key, part.Part.Id, TimeOnly.FromDateTime(part.Window.Start));
                    return new PlanPartChoiceViewModel($"{Clock(part.Window.Start)} · {part.Name}", slot == current, () => pick(slot));
                }).ToList()))
            .ToList();
    }

    /// <summary>E.g. "Planned for Fri 3:00 PM · Deep work"; empty for none.</summary>
    internal string PlanTextOf(PlannedSlot? slot)
    {
        if (slot is null)
        {
            return string.Empty;
        }

        string name = PartName(slot) is string found ? $" · {found}" : string.Empty;
        return $"Planned for {DayLabel(slot.Date, DateOnly.FromDateTime(LocalNow()))} {Clock(slot.StartsAt)}{name}";
    }

    private static string Capitalised(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private string? PartName(PlannedSlot slot)
    {
        var window = new LocalWindow(slot.Date.AddDays(-1).ToDateTime(TimeOnly.MinValue), slot.Date.AddDays(2).ToDateTime(TimeOnly.MinValue));
        return _configuration.Current.DayPlan.PartsIn(window).FirstOrDefault(part => part.Part.Id == slot.PartId)?.Name;
    }

    // ---- Reading the stores ----

    // Three tasks (more only where the session was allowed them), never in a test session: the same rule as adding one on Today.
    private bool SessionIsFull => _tasks.IsAtActionLimit && _tasks.ActionsUsed >= _tasks.ActionAllowance;

    // A tick changes several stores one after another (the record, then the list): the page is rebuilt once, after the tick has
    // drawn, not once per store. Background priority lets the check box and the input go first.
    private bool _refreshQueued;

    private void OnStoreChanged(object? sender, EventArgs e)
    {
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_refreshQueued)
        {
            return;
        }

        _refreshQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            Refresh();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // The running bar counts down with Today's clock; the buttons follow the session starting or ending.
        if (e.PropertyName is nameof(SessionViewModel.QuickStart) or nameof(SessionViewModel.CardState) or nameof(SessionViewModel.IsSessionActive))
        {
            OnPropertyChanged(nameof(IsSessionLive));
            OnPropertyChanged(nameof(CanStartSession));
            Refresh();
        }
    }

    // The running bar counts down with Today's clock.
    private void OnFocusPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FocusViewModel.TimeLeftText))
        {
            TickRunningBar();
        }
    }

    private void TickRunningBar()
    {
        if (Detail is { ShowsRunningBar: true } detail)
        {
            // While planning, the clock is the planning time ("Planning · 6:43 left"); once focus runs, the focus time.
            detail.SetRunningText(_session.Focus?.IsPlanning == true ? "Planning" : "Running", _session.Focus?.TimeLeftText ?? string.Empty);
        }
    }

    private void Refresh()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            using IDisposable timing = TickDiag.Measure("tasks page rebuild");
            RebuildLists();
            ShowSelected();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RebuildLists()
    {
        SessionChecklist current = _tasks.Current;
        bool live = _tasks.IsLive;
        Guid? runningId = live ? current.Shown?.Task.Id : null;
        DateTime now = LocalNow();
        DateOnly today = DateOnly.FromDateTime(now);
        CategoryEditor.Refresh(_tasks.Categories);
        Dictionary<Guid, TaskItem> stored = StoredTasks();
        _rebuildStored = stored;
        _nextReminders = NextReminders();

        // This session: the open tasks, numbered as the session runs them. With no session running the next session's list also
        // holds Queued's tasks (the task list says which); during one, Queued is read from the task list.
        var sessionRows = new List<TaskListRowViewModel>();
        var queuedRows = new List<TaskListRowViewModel>();
        int readyActions = 0;
        foreach (SessionTask task in current.Tasks.Where(task => !task.IsDone))
        {
            TaskCategory? category = _tasks.Categories.FirstOrDefault(item => item.Id == task.CategoryId);
            Guid id = task.Id;
            bool queued = !live && stored.TryGetValue(id, out TaskItem? item) && item.Bin == TaskBin.Queued;
            if (queued)
            {
                queuedRows.Add(MakeRow(
                    new TaskRowKey(TaskRowKind.Session, id), task.Title, SessionLine(task), category?.Name, category?.Color,
                    categoryId: task.CategoryId, toggle: () => _tasks.SetTickedAsync(id, true), isQueued: true));
                continue;
            }

            readyActions += stored.TryGetValue(id, out TaskItem? inSession) ? inSession.ActionCount : 1;
            sessionRows.Add(MakeRow(
                new TaskRowKey(TaskRowKind.Session, id), task.Title, SessionLine(task), category?.Name, category?.Color,
                number: sessionRows.Count + 1, isNow: id == runningId, categoryId: task.CategoryId, toggle: () => _tasks.SetTickedAsync(id, true)));
        }

        if (live)
        {
            queuedRows.AddRange(stored.Values.Where(item => item.Bin == TaskBin.Queued).OrderBy(item => item.Position).Select(QueuedRow));
        }

        Replace(_sessionRows, sessionRows);
        Replace(_queuedRows, queuedRows);

        // Finished today: ticked tasks of the running checklist and of sessions that ended today (and Later tasks ticked off).
        var doneRows = new List<TaskListRowViewModel>();
        foreach (SessionTask task in DoneToday(today))
        {
            Guid id = task.Id;
            (string? categoryName, string? categoryColor) = TaskCategoryDisplay.Resolve(_tasks.Categories, task.CategoryId, task.Category, task.CategoryColor);
            doneRows.Add(MakeRow(
                new TaskRowKey(TaskRowKind.Done, id), task.Title, $"Finished {Clock(Local(task.DoneAt!.Value))}", categoryName,
                categoryColor, categoryId: task.CategoryId, isTicked: true, toggle: () => _tasks.SetTickedAsync(id, false),
                sortTime: task.DoneAt!.Value));
        }

        // During a session, a Queued task ticked today sits in the task list's Finished today, outside the checklist: listed here too,
        // and unticking it puts it back in Queued (the task list's reopen).
        if (live)
        {
            HashSet<Guid> listed = doneRows.Select(row => row.Id).ToHashSet();
            IEnumerable<TaskItem> finishedInList = stored.Values
                .Where(item => item.Bin == TaskBin.Done && item.DoneAt is DateTimeOffset at && DateOnly.FromDateTime(Local(at)) == today)
                .Where(item => !listed.Contains(item.Id) && !_tasks.IsInChecklist(item.Id))
                .OrderByDescending(item => item.DoneAt);
            foreach (TaskItem item in finishedInList)
            {
                Guid id = item.Id;
                TaskLabel? label = LabelOf(item.LabelId);
                doneRows.Add(MakeRow(
                    new TaskRowKey(TaskRowKind.Queued, id), item.Title, $"Finished {Clock(Local(item.DoneAt!.Value))}", label?.Name,
                    label is null ? null : TaskLabelColors.Opaque(label.Color), categoryId: CategoryIdOf(item.LabelId), isTicked: true,
                    toggle: () => ActAsync(actions => actions.ReopenAsync([id])), sortTime: item.DoneAt!.Value));
            }
        }

        Replace(_doneRows, doneRows);

        List<TaskListRowViewModel> shelfRows = RebuildShelf(today);
        RebuildReminders(today, sessionRows.Concat(queuedRows).Concat(doneRows).Concat(shelfRows));

        // A selected row that left the lists is no longer selected.
        if (_picked.Count > 0)
        {
            HashSet<TaskRowKey> present = AllRows().Select(row => row.Key).ToHashSet();
            _picked.RemoveWhere(key => !present.Contains(key));
            PickedCount = _picked.Count;
        }

        string sessionHint = live
            ? $"{_tasks.ActionsUsed}/{_tasks.ActionCap} in focus"
            : readyActions > 0 ? $"{readyActions} ready" : string.Empty;
        ShowTodaySections(TodayPanel, sessionHint, withReminders: false, live);
        ShowTodaySections(TodayTabPanel, sessionHint, withReminders: true, live);
        TodayRailText = $"{(live ? _tasks.ActionsUsed : readyActions)} in focus";
        TodayDateText = now.ToString("ddd M/d", CultureInfo.InvariantCulture);
        Jot.IsQuiet = live;

        OnPropertyChanged(nameof(IsSessionLive));
        OnPropertyChanged(nameof(CanStartSession));
    }

    // This session, Queued, (Reminders due today,) Finished today, with their hints; This session is pinned open by default, the others are quieter and start folded.
    private void ShowTodaySections(TaskSectionsPanelViewModel panel, string sessionHint, bool withReminders, bool live)
    {
        TaskSectionViewModel session = panel.Section("session", _sessionRows, canPin: true);
        session.Title = "This session";
        session.Hint = sessionHint;
        session.Bin = TaskBin.Session;
        TaskSectionViewModel queued = panel.Section("queued", _queuedRows, quiet: true, canPin: true, opensByDefault: false);
        queued.Title = "Queued";
        queued.Bin = TaskBin.Queued;
        queued.Hint = CountText(_queuedRows.Count);
        TaskSectionViewModel done = panel.Section("done", _doneRows, quiet: true, canPin: true, opensByDefault: false);
        done.Title = "Finished today";
        done.Bin = TaskBin.Done;
        done.Hint = CountText(_doneRows.Count);
        var sections = new List<TaskSectionViewModel> { session, queued };
        if (withReminders)
        {
            TaskSectionViewModel reminders = panel.Section("reminders", _reminderRows, quiet: true, canPin: true, opensByDefault: false);
            reminders.Title = "Reminders due today";
            reminders.Icon = MaterialIconKind.BellOutline;
            reminders.Hint = CountText(_reminderRows.Count);
            sections.Add(reminders);
        }

        sections.Add(done);
        panel.InSession = live;
        panel.Show(sections);
    }

    private static string CountText(int count) => count > 0 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;

    // Reminders due today (the Today page only): one row per task with a reminder due today, soonest first, "3:00 PM · 10 min before".
    private void RebuildReminders(DateOnly today, IEnumerable<TaskListRowViewModel> shown)
    {
        var rows = new List<TaskListRowViewModel>();
        if (_reminders is not null)
        {
            Dictionary<Guid, TaskListRowViewModel> byId = [];
            foreach (TaskListRowViewModel row in shown)
            {
                byId.TryAdd(row.Id, row);
            }

            IEnumerable<TaskReminderEntry> dueToday = _reminders.Entries
                .Where(entry => DateOnly.FromDateTime(Local(entry.Due)) == today)
                .OrderBy(entry => entry.Due)
                .DistinctBy(entry => entry.TaskId);
            foreach (TaskReminderEntry entry in dueToday)
            {
                if (byId.TryGetValue(entry.TaskId, out TaskListRowViewModel? row))
                {
                    rows.Add(row.WithSubText($"{Clock(Local(entry.Due))} · {TaskReminderText.Rule(entry, _time.LocalTimeZone)}"));
                }
            }
        }

        Replace(_reminderRows, rows);
    }

    // The Shelf: its rows come from the old lists' views of the task list (come-backs and Auto-saved = To sort, Later = Next and Later),
    // shown as the active view groups them; before the task list exists, by bins with the search only. Returns every Shelf row built.
    private List<TaskListRowViewModel> RebuildShelf(DateOnly today)
    {
        var rows = new Dictionary<Guid, (TaskListRowViewModel Row, TaskBin Bin)>();
        foreach (Comeback comeback in _comebacks.Open)
        {
            Guid id = comeback.Id;
            rows.TryAdd(id, (MakeRow(
                new TaskRowKey(TaskRowKind.ComeBack, id), comeback.Text, $"Parked {When(comeback.CreatedAt, today)}", comeback.Category,
                LocalColor(comeback.Category, comeback.CategoryColor), toggle: () => _comebacks.TickAsync(id), sortTime: comeback.CreatedAt), TaskBin.Sort));
        }

        foreach (AutoSavedTask item in _autoSaved.Items)
        {
            Guid id = item.Id;
            string title = item.Title;
            rows.TryAdd(id, (MakeRow(
                new TaskRowKey(TaskRowKind.AutoSaved, id), title, $"Saved {When(item.SavedAt, today)}", null, null,
                menu: () => AutoSavedMenu(id, title), selectable: false, sortTime: item.SavedAt), TaskBin.Sort));
        }

        foreach (SyncLaterItem item in _later.Items)
        {
            Guid id = item.Id;
            string line = string.Join(" · ", new[] { item.DueOn is DateOnly due ? $"due {DayLabel(due, today)}" : string.Empty, LaterLine(item, today) }.Where(part => part.Length > 0));
            TaskBin bin = item.Bin == SyncLaterItem.NextBin ? TaskBin.Next : TaskBin.Later;
            rows.TryAdd(id, (MakeRow(
                new TaskRowKey(TaskRowKind.Later, id), item.Title, line, item.Category, LocalColor(item.Category, item.CategoryColor),
                toggle: () => _tasks.CompleteLaterAsync(id), sortTime: item.EditedAt, dueOn: item.DueOn), bin));
        }

        var groups = new List<(string Key, string Title, TaskBin? Bin, Guid? LabelId, List<TaskListRowViewModel> Rows)>();
        if (Shelf.Query() is TaskViewResult result)
        {
            foreach (TaskViewGroup group in result.Groups)
            {
                List<TaskListRowViewModel> groupRows = group.Tasks
                    .Select(task => rows.TryGetValue(task.Id, out var found) ? found.Row : null)
                    .OfType<TaskListRowViewModel>()
                    .ToList();
                groups.Add((group.Key, group.Title, group.Bin, group.LabelId, groupRows));
            }
        }
        else
        {
            string search = Shelf.Search;
            foreach (TaskBin bin in ShelfViewModel.Bins)
            {
                List<TaskListRowViewModel> groupRows = rows.Values
                    .Where(entry => entry.Bin == bin && (search.Length == 0 || entry.Row.Title.Contains(search, StringComparison.OrdinalIgnoreCase)))
                    .Select(entry => entry.Row)
                    .ToList();
                groups.Add(("bin:" + bin.ToString().ToLowerInvariant(), TaskBins.Name(bin), bin, null, groupRows));
            }
        }

        var sections = new List<TaskSectionViewModel>();
        foreach ((string key, string title, TaskBin? bin, Guid? labelId, List<TaskListRowViewModel> groupRows) in groups)
        {
            if (!_shelfRows.TryGetValue(key, out ObservableCollection<TaskListRowViewModel>? list))
            {
                list = new ObservableCollection<TaskListRowViewModel>();
                _shelfRows[key] = list;
            }

            Replace(list, groupRows);
            TaskSectionViewModel section = Shelf.Panel.Section(key, list);
            section.Title = title;
            section.Bin = bin;
            section.Hint = CountText(groupRows.Count);
            TaskLabel? label = LabelOf(labelId);
            section.Dot = label is null ? null : TaskCategoryBrushes.Of(TaskLabelColors.Opaque(label.Color));
            if (bin is TaskBin shelfBin)
            {
                section.UseAdd(this, shelfBin, $"Add to {title}");
            }
            else
            {
                section.UseAdd(null, TaskBin.Sort, string.Empty);
            }

            sections.Add(section);
        }

        // Groups no longer shown (a view or grouping changed) let their rows go, so nothing deleted stays selectable.
        HashSet<string> shown = groups.Select(group => group.Key).ToHashSet();
        foreach (string gone in _shelfRows.Keys.Where(key => !shown.Contains(key)).ToList())
        {
            _shelfRows[gone].Clear();
            _shelfRows.Remove(gone);
        }

        Shelf.Panel.Show(sections);
        Shelf.CountText = rows.Count == 1 ? "1 task" : $"{rows.Count} tasks";
        Shelf.Refresh();
        return rows.Values.Select(entry => entry.Row).ToList();
    }

    // ---- Queued while a session runs: a task of the task list only ----

    private TaskListRowViewModel QueuedRow(TaskItem item)
    {
        TaskLabel? label = LabelOf(item.LabelId);
        Guid id = item.Id;
        var parts = new List<string>();
        if (item.Steps is { Count: > 0 } steps)
        {
            parts.Add($"{steps.Count(step => !step.IsDone)} of {steps.Count} steps left");
        }

        if (item.EstimateMinutes is int minutes)
        {
            parts.Add(EstimateText.Of(minutes));
        }

        return MakeRow(
            new TaskRowKey(TaskRowKind.Queued, id), item.Title, string.Join(" · ", parts), label?.Name,
            label is null ? null : TaskLabelColors.Opaque(label.Color), categoryId: CategoryIdOf(item.LabelId),
            toggle: () => ActAsync(actions => actions.FinishAsync([id])));
    }

    // "Move to This session": the session's cap decides (a full session says so; nothing is added around it).
    private async Task MoveQueuedToSessionAsync(Guid id)
    {
        Notice = string.Empty;
        if (_actions is null)
        {
            return;
        }

        TaskMoveOutcome outcome = await _actions.MoveToSessionAsync([id]);
        if (outcome.NeedsRoom.Count > 0)
        {
            Notice = "This session is full. Finish one of its tasks or move one to Queued first.";
        }
    }

    // A Queued task moved to a Shelf bin; the Later move answers with its id (the same task, so the detail card can follow it).
    private async Task<Guid?> MoveQueuedAsync(Guid id, TaskBin bin)
    {
        if (_actions is null)
        {
            return null;
        }

        TaskMoveOutcome outcome = await _actions.MoveAsync([id], bin);
        return outcome.Moved > 0 && bin == TaskBin.Later ? id : null;
    }

    private Task ActAsync(Func<TaskActionsService, Task> run)
    {
        return _actions is null ? Task.CompletedTask : run(_actions);
    }

    // The task list's live tasks by id; none before the task list exists.
    private Dictionary<Guid, TaskItem> StoredTasks()
    {
        if (_store is not { IsActive: true } store)
        {
            return [];
        }

        var tasks = new Dictionary<Guid, TaskItem>();
        foreach (SyncTaskItem item in store.Lists.Tasks.Where(item => item.Task.IsLive))
        {
            tasks.TryAdd(item.Id, item.Task);
        }

        return tasks;
    }

    private TaskItem? StoredTask(Guid id)
    {
        return _store?.Lists.Tasks.FirstOrDefault(item => item.Id == id && item.Task.IsLive)?.Task;
    }

    private TaskLabel? LabelOf(Guid? labelId)
    {
        return labelId is Guid id ? _store?.Lists.Labels.Select(item => item.Label).FirstOrDefault(label => label.IsLive && label.Id == id) : null;
    }

    // A label's id as the PC's categories know it (a label keeps the id its category had here), and back.
    private Guid? CategoryIdOf(Guid? labelId)
    {
        return labelId is Guid id && _store is not null && _store.CategoryIds.TryGetValue(id, out Guid categoryId) ? categoryId : labelId;
    }

    private Guid? LabelIdOf(Guid? categoryId)
    {
        if (categoryId is not Guid id || _store is null)
        {
            return categoryId;
        }

        foreach ((Guid labelId, Guid mapped) in _store.CategoryIds)
        {
            if (mapped == id)
            {
                return labelId;
            }
        }

        return id;
    }

    // Every row the page shows (a row in two sections, once per section).
    private IEnumerable<TaskListRowViewModel> AllRows()
    {
        return _sessionRows.Concat(_queuedRows).Concat(_doneRows).Concat(_reminderRows).Concat(_shelfRows.Values.SelectMany(list => list));
    }

    // A Later item or come-back carries its category's name and the colour it had when it was set; this PC's own colour for that name
    // wins when it has one, else the item's own shows.
    private string? LocalColor(string? categoryName, string? storedColor)
    {
        return TaskCategoryDisplay.Resolve(_tasks.Categories, null, categoryName, storedColor).Color;
    }

    private TaskListRowViewModel MakeRow(
        TaskRowKey key, string title, string subText, string? categoryName, string? categoryColor, int number = 0, bool isNow = false,
        bool isTicked = false, Guid? categoryId = null, Func<Task>? toggle = null, string statusTag = "",
        Func<IReadOnlyList<TaskMenuEntry>>? menu = null, bool selectable = true, DateTimeOffset sortTime = default, DateOnly? dueOn = null,
        bool isQueued = false)
    {
        Action<TaskListRowViewModel, TaskRowClick> select = selectable ? OnRowClick : (_, _) => { };
        // Every kind of row but Auto-saved can have a category: its chip, or the quiet "+ category" spot, opens the picker.
        Func<IReadOnlyList<TaskMenuEntry>>? labelMenu = key.Kind == TaskRowKind.AutoSaved ? null : () => LabelEntries(key);
        // ADR 72: the row's labels and, for an item kept on this PC only, a quiet mark, after its own line (rows the task list
        // doesn't hold; the others show the detail line's pieces, tags included).
        TaskItem? item = _rebuildStored is not null && _rebuildStored.TryGetValue(key.Id, out TaskItem? held) ? held : null;
        string labels = LabelsText(LabelsOf(key));
        string kept = LocalToOf(key) is Guid keeper && keeper == SelfId?.Invoke() ? "only on this PC" : string.Empty;
        subText = string.Join(" · ", new[] { subText, labels, kept }.Where(part => part.Length > 0));
        RowExtras extras = ExtrasOf(key, item, isQueued, isTicked, selectable);
        // DIAG (slow tick, remove): the whole tick, from the click to the last store change it awaited.
        Func<Task>? original = toggle;
        Func<Task>? timedToggle = original is null ? null : async () =>
        {
            TickDiag.Mark($"tick {key.Kind}");
            using IDisposable timing = TickDiag.Measure("tick total");
            await original();
        };
        toggle = timedToggle;
        return new TaskListRowViewModel(key, title, subText, select, number, isNow, toggle, isTicked, menu ?? (() => MenuFor(key)), labelMenu)
        {
            IsPicked = _picked.Contains(key),
            Finishes = _finishes,
            IsFinishing = _finishes.IsPending(key),
            IsCollapsing = _finishes.IsFolding(key),
            NumberText = extras.NumberText,
            Bits = extras.Bits,
            IsHighlighted = extras.IsHighlighted,
            TimeText = extras.TimeText,
            LeftAction = extras.Left,
            RightAction = extras.Right,
            CanDrag = extras.CanDrag,
            CategoryId = categoryId,
            CategoryName = categoryName,
            CategoryColor = categoryColor,
            CategoryBrush = TaskCategoryBrushes.Of(categoryColor),
            StatusTag = statusTag,
            SortTime = sortTime,
            DueOn = dueOn,
            IsQueued = isQueued,
        };
    }

    // Only what changed goes through to the screen: a row that shows the same as before keeps its object (and so its drawn
    // control), rows are moved, inserted or removed one by one, never the whole list cleared and drawn again.
    private static void Replace(ObservableCollection<TaskListRowViewModel> target, IReadOnlyList<TaskListRowViewModel> rows)
    {
        var held = target.ToDictionary(row => row.Key);
        var wanted = new List<TaskListRowViewModel>(rows.Count);
        foreach (TaskListRowViewModel row in rows)
        {
            if (held.TryGetValue(row.Key, out TaskListRowViewModel? old) && old.ShowsSameAs(row))
            {
                old.IsPicked = row.IsPicked;
                wanted.Add(old);
            }
            else
            {
                wanted.Add(row);
            }
        }

        for (int index = 0; index < wanted.Count; index++)
        {
            TaskListRowViewModel row = wanted[index];
            if (index < target.Count && ReferenceEquals(target[index], row))
            {
                continue;
            }

            int at = -1;
            for (int search = index + 1; search < target.Count; search++)
            {
                if (ReferenceEquals(target[search], row))
                {
                    at = search;
                    break;
                }
            }

            if (at >= 0)
            {
                target.Move(at, index);
            }
            else
            {
                target.Insert(index, row);
            }
        }

        while (target.Count > wanted.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    // The detail card follows the selected key; nothing selected (or gone) falls back to the first task of the session.
    private void ShowSelected()
    {
        TaskSnapshot? snapshot = _selectedKey is TaskRowKey key ? Enrich(Read(key)) : null;
        if (snapshot is null && _sessionRows.Count > 0)
        {
            _selectedKey = _sessionRows[0].Key;
            snapshot = Enrich(Read(_selectedKey.Value));
        }

        if (snapshot is null)
        {
            _selectedKey = null;
            Detail?.Dispose();
            Detail = null;
        }
        else if (Detail is not null && Detail.Key == snapshot.Key)
        {
            Detail.Reload(snapshot);
        }
        else
        {
            Detail?.Dispose();
            Detail = new TaskDetailViewModel(snapshot, this);
            TickRunningBar();
        }

        TaskRowKey? selected = Detail?.Key;
        _selectedKey = selected ?? _selectedKey;
        MarkOpenRow();
    }

    private TaskSnapshot? Read(TaskRowKey key)
    {
        SessionChecklist current = _tasks.Current;
        switch (key.Kind)
        {
            case TaskRowKind.Session:
            {
                List<SessionTask> open = current.Tasks.Where(task => !task.IsDone).ToList();
                int index = open.FindIndex(task => task.Id == key.Id);
                if (index < 0)
                {
                    // Ticked meanwhile (on Today): it is a Done row now.
                    return current.Tasks.Any(task => task.Id == key.Id) ? Read(new TaskRowKey(TaskRowKind.Done, key.Id)) : null;
                }

                SessionTask task = open[index];
                bool running = _tasks.IsLive && current.Shown?.Task.Id == task.Id;
                return new TaskSnapshot(key, $"In this session · {index + 1} of {open.Count}", task.Title, task.Subtasks, task.EstimateMinutes, task.Note, null, task.PlannedFor, running, false, null);
            }

            case TaskRowKind.Later:
            {
                SyncLaterItem? item = _later.Items.FirstOrDefault(candidate => candidate.Id == key.Id);
                if (item is null)
                {
                    return null;
                }

                string kind = item.DueOn is DateOnly due ? $"Later · due {DayLabel(due, DateOnly.FromDateTime(LocalNow()))}" : "Later";
                return new TaskSnapshot(key, kind, item.Title, item.Subtasks ?? [], item.EstimateMinutes, item.Note, item.DueOn, item.PlannedFor, false, false, null);
            }

            case TaskRowKind.ComeBack:
            {
                Comeback? comeback = _comebacks.All.FirstOrDefault(candidate => candidate.Id == key.Id);
                if (comeback is null)
                {
                    return null;
                }

                string kind = comeback.DoneAt is null ? "Come back to" : "Come back to · done";
                return new TaskSnapshot(key, kind, comeback.Text, [], null, null, null, null, false, comeback.DoneAt is not null, $"Parked {When(comeback.CreatedAt, DateOnly.FromDateTime(LocalNow()))}");
            }

            case TaskRowKind.AutoSaved:
                return null; // not selectable: its menu is all it has

            case TaskRowKind.Queued:
            {
                // The task list's own task: its steps, time limit and note change through the task actions.
                TaskItem? item = StoredTask(key.Id);
                // A Queued task ticked during the session shows (read only) from Finished today.
                return item is null || item.Bin is not (TaskBin.Queued or TaskBin.Done)
                    ? null
                    : new TaskSnapshot(
                        key, item.Bin == TaskBin.Done ? "Finished today" : "Queued", item.Title, (item.Steps ?? []).Select(step => step.ToSubtask()).ToList(), item.EstimateMinutes, item.Note,
                        item.DueOn, item.PlannedFor, false, item.Bin == TaskBin.Done, null);
            }

            default:
            {
                // Done today, or left open by an ended session: shown, not edited (its title, steps and category change from the menu).
                SessionTask? task = FindTask(key.Id);
                if (task is null)
                {
                    return null;
                }

                if (!task.IsDone && current.Tasks.Any(candidate => candidate.Id == key.Id))
                {
                    return Read(new TaskRowKey(TaskRowKind.Session, key.Id)); // Unticked on the list: it is a task of this session again.
                }

                string kind = task.IsDone ? "Done today" : "Left open";
                return new TaskSnapshot(key, kind, task.Title, task.Subtasks, task.EstimateMinutes, task.Note, null, null, false, true, null);
            }
        }
    }

    // A task of the draft or running checklist, or of an ended session's record.
    private SessionTask? FindTask(Guid id)
    {
        return _tasks.Current.Tasks.FirstOrDefault(task => task.Id == id) ?? _tasks.HistoryTaskOf(id);
    }

    private IEnumerable<SessionTask> DoneToday(DateOnly today)
    {
        IEnumerable<SessionTask> fromHistory = _tasks.History.SelectMany(checklist => checklist.Tasks);
        return _tasks.Current.Tasks.Concat(fromHistory)
            .Where(task => task.DoneAt is DateTimeOffset at && DateOnly.FromDateTime(Local(at)) == today)
            .OrderByDescending(task => task.DoneAt)
            .GroupBy(task => task.Id)
            .Select(group => group.First());
    }

    private string SessionLine(SessionTask task)
    {
        var parts = new List<string>();
        if (task.Subtasks.Count > 0)
        {
            parts.Add($"{task.Subtasks.Count(step => !step.IsDone)} of {task.Subtasks.Count} steps left");
        }

        if (task.EstimateMinutes is int minutes)
        {
            parts.Add(EstimateText.Of(minutes));
        }

        if (task.PlannedFor is PlannedSlot slot)
        {
            parts.Add($"planned {DayLabel(slot.Date, DateOnly.FromDateTime(LocalNow()))} {Clock(slot.StartsAt)}");
        }

        return string.Join(" · ", parts);
    }

    private string LaterLine(SyncLaterItem item, DateOnly today)
    {
        var parts = new List<string>();
        if (item.Subtasks is { Count: > 0 } steps)
        {
            parts.Add(steps.Count == 1 ? "1 step" : $"{steps.Count} steps");
        }

        if (item.EstimateMinutes is int minutes)
        {
            parts.Add(EstimateText.Of(minutes));
        }

        if (item.PlannedFor is PlannedSlot slot)
        {
            parts.Add($"planned {DayLabel(slot.Date, today)} {Clock(slot.StartsAt)}");
        }

        return string.Join(" · ", parts);
    }

    private DateTime LocalNow() => TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).DateTime;

    private DateTime Local(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, _time.LocalTimeZone).DateTime;

    // "11:02 AM" today, else "Thu 10/23 11:02 AM"; always 12-hour.
    private string When(DateTimeOffset time, DateOnly today)
    {
        DateTime local = Local(time);
        return DateOnly.FromDateTime(local) == today
            ? Clock(local)
            : $"{local.ToString("ddd M/d", CultureInfo.InvariantCulture)} {Clock(local)}";
    }

    private static string Clock(DateTime time) => time.ToString("h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>"today", "tomorrow", "Fri" within the next six days, else "Nov 3" (earlier days read "yesterday" or the date).</summary>
    internal static string DayLabel(DateOnly date, DateOnly today)
    {
        int days = date.DayNumber - today.DayNumber;
        return days switch
        {
            0 => "today",
            1 => "tomorrow",
            -1 => "yesterday",
            > 1 and < 7 => date.ToString("ddd", CultureInfo.InvariantCulture),
            _ => date.ToString("MMM d", CultureInfo.InvariantCulture),
        };
    }
}
