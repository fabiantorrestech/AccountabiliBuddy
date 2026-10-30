using System.Collections.ObjectModel;
using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Tasks page (design 5x): the next session's tasks (or the running checklist), come-backs, the Later list (ADR 50), what
/// was done today, the tasks past sessions left open, and the Archive (the Notebook), with one detail card for whatever is
/// selected. Every list reads its store again when one changes; tasks are grouped under their category (Muted header with its
/// colour dot, tasks set in, "No category" last). Every row has a check box and a ⋯ menu (delete, move, label, add subtask,
/// rename); a delete leaves an Undo bar and a place in Archive › Recently deleted. Nothing here loosens a rule of a running
/// session: a task can leave it only as "taken off open" (ADR 25), a 4th task is never added without the reason "Add anyway"
/// asks for, and nothing ends or shortens a session.
/// </summary>
public sealed partial class TasksViewModel : ObservableObject, IDisposable
{
    /// <summary>Past sessions' unfinished tasks listed on the page: the last two weeks (the Archive keeps all 90 days).</summary>
    private static readonly TimeSpan PastShown = TimeSpan.FromDays(14);

    private readonly SessionTasksService _tasks;
    private readonly LaterStore _later;
    private readonly ComebackStore _comebacks;
    private readonly AutoSavedTaskStore _autoSaved;
    private readonly SessionViewModel _session;
    private readonly ConfigurationService _configuration;
    private readonly TimeProvider _time;
    private TaskRowKey? _selectedKey;
    private Guid? _categoryFilterId;
    private bool _refreshing;
    private List<Guid> _unfinishedIds = [];
    private int _sessionRowCount;
    private int _doneRowCount;

    public TasksViewModel(
        SessionTasksService tasks, LaterStore later, ComebackStore comebacks, AutoSavedTaskStore autoSaved, NotebookViewModel archive,
        SessionViewModel session, ConfigurationService configuration, TimeProvider time)
    {
        _tasks = tasks;
        _later = later;
        _comebacks = comebacks;
        _autoSaved = autoSaved;
        Archive = archive;
        _session = session;
        _configuration = configuration;
        _time = time;
        Undo = new UndoBarViewModel(tasks);
        CountsText = LaterHeader = LaterNextDueText = DoneHeader = UnfinishedHeader = AutoSavedHeader = AddPlaceholder = SearchText = NewTaskText = Notice = string.Empty;
        _tasks.Changed += OnStoreChanged;
        _comebacks.Changed += OnStoreChanged;
        _autoSaved.Changed += OnStoreChanged;
        _session.PropertyChanged += OnSessionPropertyChanged;
        if (_session.Focus is not null)
        {
            _session.Focus.PropertyChanged += OnFocusPropertyChanged;
        }

        Archive.Host = this;
        Archive.Refresh();
        Refresh();
    }

    /// <summary>The Notebook, restyled as the Archive row at the bottom.</summary>
    public NotebookViewModel Archive { get; }

    /// <summary>The bar after a delete: "Deleted “…” · Undo".</summary>
    public UndoBarViewModel Undo { get; }

    public ObservableCollection<TaskGroupViewModel> SessionGroups { get; } = new();

    public ObservableCollection<TaskListRowViewModel> ComeBackRows { get; } = new();

    public ObservableCollection<TaskGroupViewModel> LaterGroups { get; } = new();

    public ObservableCollection<TaskGroupViewModel> DoneGroups { get; } = new();

    public ObservableCollection<TaskGroupViewModel> UnfinishedGroups { get; } = new();

    /// <summary>Tomorrow's first tasks typed on a shutdown card that closed by itself; each goes 30 days after it was saved.</summary>
    public ObservableCollection<PastTaskRowViewModel> AutoSavedRows { get; } = new();

    /// <summary>The category filter: All, then each category.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TaskChipViewModel> CategoryChips { get; set; } = [];

    /// <summary>E.g. "3 for this session · 2 to come back to · 11 later".</summary>
    [ObservableProperty]
    public partial string CountsText { get; set; }

    [ObservableProperty]
    public partial string LaterHeader { get; set; }

    /// <summary>E.g. "Next due: Expense report, Fri"; empty when no item has a date.</summary>
    [ObservableProperty]
    public partial string LaterNextDueText { get; set; }

    [ObservableProperty]
    public partial string DoneHeader { get; set; }

    [ObservableProperty]
    public partial string UnfinishedHeader { get; set; }

    [ObservableProperty]
    public partial string AutoSavedHeader { get; set; }

    [ObservableProperty]
    public partial string AddPlaceholder { get; set; }

    [ObservableProperty]
    public partial string NewTaskText { get; set; }

    [ObservableProperty]
    public partial bool IsSearchOpen { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial bool IsLaterOpen { get; set; }

    [ObservableProperty]
    public partial bool IsDoneOpen { get; set; }

    [ObservableProperty]
    public partial bool IsUnfinishedOpen { get; set; }

    [ObservableProperty]
    public partial bool IsAutoSavedOpen { get; set; }

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

    public bool HasSessionRows => _sessionRowCount > 0;

    public bool HasComeBackRows => ComeBackRows.Count > 0;

    public bool HasLater => _later.Items.Count > 0;

    public bool HasDone => _doneRowCount > 0;

    public bool HasUnfinished => _unfinishedIds.Count > 0;

    public bool HasAutoSaved => AutoSavedRows.Count > 0;

    public bool HasLaterNextDue => LaterNextDueText.Length > 0;

    /// <summary>A search shows every group open, so a hit can't hide in a folded one.</summary>
    public bool IsSearching => SearchText.Trim().Length > 0;

    public bool ShowsLaterRows => IsLaterOpen || IsSearching;

    public bool ShowsDoneRows => IsDoneOpen || IsSearching;

    public bool ShowsUnfinishedRows => IsUnfinishedOpen || IsSearching;

    public bool ShowsAutoSavedRows => IsAutoSavedOpen || IsSearching;

    public bool ShowsArchive => IsArchiveOpen || IsSearching;

    /// <summary>A session runs: the running checklist is the list, and the page offers no way out of it.</summary>
    public bool IsSessionLive => _tasks.IsLive;

    /// <summary>"Start with this" can start now: no session runs and Today's Start is ready.</summary>
    public bool CanStartSession => !_tasks.IsLive && _session.QuickStart.StartCommand.CanExecute(null);

    /// <summary>The view should put the keyboard in a box of the detail card: "title" or "step".</summary>
    public event EventHandler<string>? FocusRequested;

    partial void OnIsLaterOpenChanged(bool value) => OnPropertyChanged(nameof(ShowsLaterRows));

    partial void OnIsDoneOpenChanged(bool value) => OnPropertyChanged(nameof(ShowsDoneRows));

    partial void OnIsUnfinishedOpenChanged(bool value) => OnPropertyChanged(nameof(ShowsUnfinishedRows));

    partial void OnIsAutoSavedOpenChanged(bool value) => OnPropertyChanged(nameof(ShowsAutoSavedRows));

    partial void OnIsArchiveOpenChanged(bool value) => OnPropertyChanged(nameof(ShowsArchive));

    partial void OnSearchTextChanged(string value)
    {
        Archive.Search = value;
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(ShowsLaterRows));
        OnPropertyChanged(nameof(ShowsDoneRows));
        OnPropertyChanged(nameof(ShowsUnfinishedRows));
        OnPropertyChanged(nameof(ShowsAutoSavedRows));
        OnPropertyChanged(nameof(ShowsArchive));
        Refresh();
    }

    public void Dispose()
    {
        _tasks.Changed -= OnStoreChanged;
        _comebacks.Changed -= OnStoreChanged;
        _autoSaved.Changed -= OnStoreChanged;
        _session.PropertyChanged -= OnSessionPropertyChanged;
        if (_session.Focus is not null)
        {
            _session.Focus.PropertyChanged -= OnFocusPropertyChanged;
        }

        Undo.Dispose();
        Detail?.Dispose();
    }

    [RelayCommand]
    private void ToggleSearch()
    {
        IsSearchOpen = !IsSearchOpen;
        if (!IsSearchOpen)
        {
            SearchText = string.Empty;
        }
    }

    [RelayCommand]
    private void ToggleLater() => IsLaterOpen = !IsLaterOpen;

    [RelayCommand]
    private void ToggleDone() => IsDoneOpen = !IsDoneOpen;

    [RelayCommand]
    private void ToggleUnfinished() => IsUnfinishedOpen = !IsUnfinishedOpen;

    [RelayCommand]
    private void ToggleAutoSaved() => IsAutoSavedOpen = !IsAutoSavedOpen;

    [RelayCommand]
    private void ToggleArchive() => IsArchiveOpen = !IsArchiveOpen;

    [RelayCommand]
    private void DismissNotice() => Notice = string.Empty;

    /// <summary>
    /// The add row's Enter: a task of this session while it has fewer than three (the filter's category if one is chosen),
    /// else a Later item. A running session never gets a 4th task this way.
    /// </summary>
    [RelayCommand]
    private async Task AddTaskAsync()
    {
        string title = NewTaskText.Trim();
        if (title.Length == 0)
        {
            return;
        }

        NewTaskText = string.Empty;
        if (!SessionIsFull)
        {
            await _tasks.AddAsync(title, null, _categoryFilterId);
            return;
        }

        TaskCategory? category = _tasks.Categories.FirstOrDefault(item => item.Id == _categoryFilterId);
        SyncLaterItem? added = await _later.AddAsync(title, null, category?.Name, category?.Color, null, null, null);
        if (added is not null)
        {
            IsLaterOpen = true;
            Select(new TaskRowKey(TaskRowKind.Later, added.Id));
        }
    }

    /// <summary>Moves every unfinished task of the past sessions listed to Later; nothing moves by itself.</summary>
    [RelayCommand]
    private async Task MoveAllUnfinishedToLaterAsync()
    {
        IReadOnlySet<Guid> ids = _unfinishedIds.ToHashSet();
        if (ids.Count > 0)
        {
            await _tasks.MovePastToLaterAsync(ids);
            IsLaterOpen = true;
        }
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

    private void SelectRow(TaskListRowViewModel row) => Select(row.Key);

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
            _ => _tasks.IsInChecklist(key.Id) ? _tasks.RenameAsync(key.Id, title) : _tasks.EditHistoryAsync(key.Id, title),
        };
    }

    internal Task SetNoteAsync(TaskRowKey key, string note)
    {
        return key.Kind switch
        {
            TaskRowKind.Session => _tasks.SetNoteAsync(key.Id, note),
            TaskRowKind.Later => EditLaterAsync(key.Id, item => item with { Note = SessionChecklist.CleanNote(note) }),
            _ => Task.CompletedTask,
        };
    }

    internal Task SetEstimateAsync(TaskRowKey key, int? minutes)
    {
        return key.Kind switch
        {
            TaskRowKind.Session => _tasks.SetEstimateAsync(key.Id, minutes),
            TaskRowKind.Later => EditLaterAsync(key.Id, item => item with { EstimateMinutes = SessionChecklist.CleanEstimate(minutes) }),
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
            _ => Task.CompletedTask,
        };
    }

    internal Task RemoveStepAsync(TaskRowKey key, Guid stepId)
    {
        return key.Kind switch
        {
            TaskRowKind.Later => _tasks.DeleteLaterStepAsync(key.Id, stepId),
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
            _ => _tasks.IsInChecklist(stepId) ? _tasks.RenameAsync(stepId, title) : _tasks.EditHistoryAsync(stepId, title),
        };
    }

    internal Task AddStepAfterAsync(TaskRowKey key, Guid stepId, string title)
    {
        return key.Kind switch
        {
            TaskRowKind.Later => EditLaterAsync(key.Id, item => item with { Subtasks = InsertAfter(item.Subtasks ?? [], stepId, new SessionSubtask(Guid.NewGuid(), title, null)) }),
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

    /// <summary>Puts a task in a category (null: none), wherever it is.</summary>
    internal Task SetCategoryAsync(TaskRowKey key, Guid? categoryId)
    {
        TaskCategory? category = _tasks.Categories.FirstOrDefault(item => item.Id == categoryId);
        return key.Kind switch
        {
            TaskRowKind.Later => EditLaterAsync(key.Id, item => item with { Category = category?.Name, CategoryColor = category?.Color }),
            TaskRowKind.ComeBack => Task.CompletedTask,
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
            _ => null,
        };
        if (laterId is Guid id)
        {
            IsLaterOpen = true;
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
            _ => _tasks.IsInChecklist(key.Id) ? _tasks.RemoveAsync(key.Id) : _tasks.EditHistoryAsync(key.Id, null),
        };
    }

    internal Task TickComeBackAsync(Guid id) => _comebacks.TickAsync(id);

    // ---- The ⋯ menu ----

    /// <summary>
    /// The entries of a task's ⋯ menu: Rename, Add subtask, Label (a category or none), Move to… (only the moves that make sense
    /// for that row) and Delete. <paramref name="rename"/> and <paramref name="addStep"/> say how the place the row is shown
    /// edits in place (the Archive); without them the detail card's boxes take the keyboard.
    /// </summary>
    internal IReadOnlyList<TaskMenuEntry> MenuFor(TaskRowKey key, Action? rename = null, Action? addStep = null)
    {
        var entries = new List<TaskMenuEntry>
        {
            TaskMenuEntry.Do("Rename", rename ?? (() => BeginDetailEdit(key, "title"))),
        };
        if (key.Kind != TaskRowKind.ComeBack)
        {
            entries.Add(TaskMenuEntry.Do("Add subtask", addStep ?? (() => BeginDetailEdit(key, "step"))));
            entries.Add(TaskMenuEntry.Group("Label", LabelEntries(key)));
        }

        List<TaskMenuEntry> moves = MoveEntries(key);
        if (moves.Count > 0)
        {
            entries.Add(TaskMenuEntry.Group("Move to…", moves));
        }

        entries.Add(TaskMenuEntry.Separator);
        entries.Add(TaskMenuEntry.Async("Delete", () => DeleteAsync(key)));
        return entries;
    }

    private void BeginDetailEdit(TaskRowKey key, string target)
    {
        Select(key);
        FocusRequested?.Invoke(this, target);
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
        bool isOpenComeBack = key.Kind == TaskRowKind.ComeBack && _comebacks.Open.Any(item => item.Id == key.Id);
        if (key.Kind is TaskRowKind.Later or TaskRowKind.Done or TaskRowKind.Past || isOpenComeBack)
        {
            moves.Add(TaskMenuEntry.Async("Add to Today", () => AddToTodayAsync(key)));
        }

        if (key.Kind != TaskRowKind.Later)
        {
            moves.Add(TaskMenuEntry.Async("Later", () => MoveToLaterAsync(key)));
        }

        if (key.Kind != TaskRowKind.ComeBack)
        {
            moves.Add(TaskMenuEntry.Async("Come back to", () => MoveToComebackAsync(key)));
        }

        // Put in plan is for the next session's tasks and Later; a running session's task can't be planned for later.
        if (key.Kind == TaskRowKind.Later || isOpenSessionTask && !_tasks.IsLive)
        {
            moves.Add(TaskMenuEntry.Group("Put in plan", PlanEntries(key)));
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

    private List<TaskMenuEntry> LabelEntries(TaskRowKey key)
    {
        string? current = CategoryNameOf(key);
        var entries = new List<TaskMenuEntry>();
        foreach (TaskCategory category in _tasks.Categories)
        {
            Guid id = category.Id;
            entries.Add(new TaskMenuEntry(
                category.Name,
                new AsyncRelayCommand(() => SetCategoryAsync(key, id)),
                IsChecked: string.Equals(current, category.Name, StringComparison.OrdinalIgnoreCase),
                Dot: TaskCategoryBrushes.Of(category.Color)));
        }

        entries.Add(new TaskMenuEntry("No category", new AsyncRelayCommand(() => SetCategoryAsync(key, null)), IsChecked: current is null));
        if (_tasks.Categories.Count == 0)
        {
            entries.Add(new TaskMenuEntry("Add categories with the tag button on Today"));
        }

        return entries;
    }

    private string? CategoryNameOf(TaskRowKey key)
    {
        switch (key.Kind)
        {
            case TaskRowKind.Later:
                return _later.Items.FirstOrDefault(item => item.Id == key.Id)?.Category;
            case TaskRowKind.ComeBack:
                return null;
            default:
                SessionTask? task = FindTask(key.Id);
                return task is null ? null : task.Category ?? _tasks.Categories.FirstOrDefault(category => category.Id == task.CategoryId)?.Name;
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
    private bool SessionIsFull => _tasks.IsAtTaskLimit && _tasks.Current.Tasks.Count >= _tasks.TaskAllowance;

    private void OnStoreChanged(object? sender, EventArgs e)
    {
        Refresh();
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
        string search = SearchText.Trim();
        DateOnly today = DateOnly.FromDateTime(LocalNow());

        // This session: the open tasks, numbered as the session runs them (the numbers don't change with a filter), under their categories.
        var sessionRows = new List<TaskListRowViewModel>();
        List<SessionTask> open = current.Tasks.Where(task => !task.IsDone).ToList();
        int total = open.Count;
        for (int index = 0; index < open.Count; index++)
        {
            SessionTask task = open[index];
            if (!InFilter(task.CategoryId, null) || !Matches(search, task.Title, task.Note, task.Subtasks.Select(step => step.Title)))
            {
                continue;
            }

            TaskCategory? category = _tasks.Categories.FirstOrDefault(item => item.Id == task.CategoryId);
            sessionRows.Add(MakeRow(
                new TaskRowKey(TaskRowKind.Session, task.Id), task.Title, SessionLine(task), category?.Name, category?.Color,
                number: index + 1, isNow: task.Id == runningId, categoryId: task.CategoryId, toggle: () => _tasks.SetTickedAsync(task.Id, true)));
        }

        _sessionRowCount = sessionRows.Count;
        Replace(SessionGroups, Group(sessionRows));

        // Come back to: open come-backs; they have no category, so a category filter leaves them out.
        ComeBackRows.Clear();
        if (_categoryFilterId is null)
        {
            foreach (Comeback comeback in _comebacks.Open.Where(item => Matches(search, item.Text, null, [])))
            {
                Guid id = comeback.Id;
                ComeBackRows.Add(MakeRow(
                    new TaskRowKey(TaskRowKind.ComeBack, id), comeback.Text, $"Parked {When(comeback.CreatedAt, today)}", null, null,
                    toggle: () => _comebacks.TickAsync(id)));
            }
        }

        // Later: every item, shown when its group is open (or while searching).
        var laterRows = new List<TaskListRowViewModel>();
        IReadOnlyList<SyncLaterItem> laterItems = _later.Items;
        foreach (SyncLaterItem item in laterItems.Where(item => InFilter(null, item.Category) && Matches(search, item.Title, item.Note, (item.Subtasks ?? []).Select(step => step.Title))))
        {
            Guid id = item.Id;
            laterRows.Add(MakeRow(
                new TaskRowKey(TaskRowKind.Later, id), item.Title, LaterLine(item, today), item.Category, item.CategoryColor,
                toggle: () => _tasks.CompleteLaterAsync(id)));
        }

        Replace(LaterGroups, Group(laterRows));

        // Done today: ticked tasks of the running checklist, and of sessions that ended today (and Later tasks ticked off).
        var doneRows = new List<TaskListRowViewModel>();
        foreach (SessionTask task in DoneToday(today).Where(task => InFilter(task.CategoryId, task.Category) && Matches(search, task.Title, task.Note, task.Subtasks.Select(step => step.Title))))
        {
            Guid id = task.Id;
            TaskCategory? category = _tasks.Categories.FirstOrDefault(item => item.Id == task.CategoryId);
            doneRows.Add(MakeRow(
                new TaskRowKey(TaskRowKind.Done, id), task.Title, $"Done {Clock(Local(task.DoneAt!.Value))}", task.Category ?? category?.Name,
                task.CategoryColor ?? category?.Color, categoryId: task.CategoryId, isTicked: true, toggle: () => _tasks.SetTickedAsync(id, false)));
        }

        _doneRowCount = DoneToday(today).Count();
        Replace(DoneGroups, Group(doneRows));

        // Unfinished from past sessions: offered to Later or Today, never moved on their own.
        var pastRows = new List<TaskListRowViewModel>();
        DateTimeOffset from = _time.GetUtcNow() - PastShown;
        List<PastTask> past = _tasks.PastUnfinished().Where(item => item.SessionStart >= from).ToList();
        _unfinishedIds = past.Select(item => item.Task.Id).ToList();
        foreach (PastTask item in past.Where(item => InFilter(item.Task.CategoryId, item.Task.Category) && Matches(search, item.Task.Title, item.Task.Note, [])))
        {
            Guid id = item.Task.Id;
            TaskCategory? category = _tasks.Categories.FirstOrDefault(c => c.Id == item.Task.CategoryId);
            pastRows.Add(MakeRow(
                new TaskRowKey(TaskRowKind.Past, id), item.Task.Title, $"Session of {When(item.SessionStart, today)}",
                item.Task.Category ?? category?.Name, item.Task.CategoryColor ?? category?.Color, categoryId: item.Task.CategoryId,
                toggle: () => _tasks.SetTickedAsync(id, true)));
        }

        Replace(UnfinishedGroups, Group(pastRows));

        // Auto-saved: a shutdown card's tomorrow task, kept when the card closed by itself; never moved on its own.
        AutoSavedRows.Clear();
        IReadOnlyList<AutoSavedTask> autoSavedItems = _autoSaved.Items;
        foreach (AutoSavedTask item in autoSavedItems.Where(item => Matches(search, item.Title, null, [])))
        {
            Guid id = item.Id;
            string title = item.Title;
            AutoSavedRows.Add(new PastTaskRowViewModel(
                id, title, $"Saved {When(item.SavedAt, today)}",
                async () =>
                {
                    if (await _later.AddAsync(title, null, null, null, null, null, null) is not null)
                    {
                        await _autoSaved.RemoveAsync(id);
                    }
                },
                () => _tasks.DeleteAutoSavedAsync(id),
                () => AddAutoSavedToTodayAsync(id, title)));
        }

        int comeBacks = _comebacks.Open.Count;
        string estimate = EstimateText.Total(open.Select(task => task.EstimateMinutes));
        CountsText = string.Join(" · ", new[]
        {
            $"{total} for this session",
            estimate.Length > 0 ? estimate : string.Empty,
            $"{comeBacks} to come back to",
            $"{laterItems.Count} later",
        }.Where(part => part.Length > 0));
        LaterHeader = $"Later · {laterItems.Count}";
        SyncLaterItem? nextDue = laterItems.Where(item => item.DueOn is not null).OrderBy(item => item.DueOn).FirstOrDefault();
        LaterNextDueText = nextDue?.DueOn is DateOnly due ? $"Next due: {nextDue.Title}, {DayLabel(due, today)}" : string.Empty;
        DoneHeader = $"Done today · {_doneRowCount}";
        UnfinishedHeader = $"Unfinished from past sessions · {_unfinishedIds.Count}";
        AutoSavedHeader = $"Auto-saved · {autoSavedItems.Count}";
        AddPlaceholder = SessionIsFull
            ? "This session is full: Enter adds it to Later"
            : "Add a task · Ctrl+Alt+N from anywhere";
        RebuildCategoryChips();

        OnPropertyChanged(nameof(HasSessionRows));
        OnPropertyChanged(nameof(HasComeBackRows));
        OnPropertyChanged(nameof(HasLater));
        OnPropertyChanged(nameof(HasDone));
        OnPropertyChanged(nameof(HasUnfinished));
        OnPropertyChanged(nameof(HasAutoSaved));
        OnPropertyChanged(nameof(HasLaterNextDue));
        OnPropertyChanged(nameof(IsSessionLive));
        OnPropertyChanged(nameof(CanStartSession));
    }

    private TaskListRowViewModel MakeRow(
        TaskRowKey key, string title, string subText, string? categoryName, string? categoryColor, int number = 0, bool isNow = false,
        bool isTicked = false, Guid? categoryId = null, Func<Task>? toggle = null)
    {
        return new TaskListRowViewModel(key, title, subText, SelectRow, number, isNow, toggle, isTicked, () => MenuFor(key))
        {
            CategoryId = categoryId,
            CategoryName = categoryName,
            CategoryColor = categoryColor,
            CategoryBrush = TaskCategoryBrushes.Of(categoryColor),
        };
    }

    private static void Replace(ObservableCollection<TaskGroupViewModel> target, IReadOnlyList<TaskGroupViewModel> groups)
    {
        target.Clear();
        foreach (TaskGroupViewModel group in groups)
        {
            target.Add(group);
        }
    }

    // The rows under their category: the categories in their order (a name this device doesn't have yet follows them), "No
    // category" last. A list with no category in it has no header; otherwise every group has one.
    private IReadOnlyList<TaskGroupViewModel> Group(IReadOnlyList<TaskListRowViewModel> rows)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        IReadOnlyList<TaskCategory> categories = _tasks.Categories;
        int Rank(string name)
        {
            if (name.Length == 0)
            {
                return int.MaxValue;
            }

            int at = categories.ToList().FindIndex(category => string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase));
            return at >= 0 ? at : categories.Count;
        }

        List<IGrouping<string, TaskListRowViewModel>> groups = rows
            .GroupBy(row => row.CategoryName?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => Rank(group.Key))
            .ToList();
        bool showsHeader = !(groups.Count == 1 && groups[0].Key.Length == 0);
        return groups.Select(group =>
        {
            string? color = group.Select(row => row.CategoryColor).FirstOrDefault(value => value is not null);
            return new TaskGroupViewModel(
                group.Key.Length == 0 ? "No category" : group.Key, TaskCategoryBrushes.Of(color), group.Key.Length > 0, showsHeader, group.ToList());
        }).ToList();
    }

    // The detail card follows the selected key; nothing selected (or gone) falls back to the first task of the session.
    private void ShowSelected()
    {
        TaskSnapshot? snapshot = _selectedKey is TaskRowKey key ? Read(key) : null;
        if (snapshot is null && SessionGroups.Count > 0)
        {
            _selectedKey = SessionGroups[0].Rows[0].Key;
            snapshot = Read(_selectedKey.Value);
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
        IEnumerable<TaskListRowViewModel> rows = SessionGroups.Concat(LaterGroups).Concat(DoneGroups).Concat(UnfinishedGroups)
            .SelectMany(group => group.Rows)
            .Concat(ComeBackRows);
        foreach (TaskListRowViewModel row in rows)
        {
            row.IsSelected = selected == row.Key;
        }
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

    private void RebuildCategoryChips()
    {
        var chips = new List<TaskChipViewModel> { new("All", _categoryFilterId is null, () => PickCategoryAsync(null)) };
        foreach (TaskCategory category in _tasks.Categories)
        {
            Guid id = category.Id;
            chips.Add(new TaskChipViewModel(category.Name, _categoryFilterId == id, () => PickCategoryAsync(id), TaskCategoryBrushes.Of(category.Color)));
        }

        // A deleted category can't stay the filter.
        if (_categoryFilterId is Guid chosen && _tasks.Categories.All(category => category.Id != chosen))
        {
            _categoryFilterId = null;
        }

        CategoryChips = chips;
    }

    private Task PickCategoryAsync(Guid? id)
    {
        _categoryFilterId = id;
        Refresh();
        return Task.CompletedTask;
    }

    // The chosen category (by id for tasks, by name for Later items, which carry only the name); All lets everything through.
    private bool InFilter(Guid? categoryId, string? categoryName)
    {
        if (_categoryFilterId is not Guid chosen)
        {
            return true;
        }

        if (categoryId is not null && categoryName is null)
        {
            return categoryId == chosen;
        }

        TaskCategory? category = _tasks.Categories.FirstOrDefault(item => item.Id == chosen);
        return category is not null && categoryName is not null && string.Equals(category.Name, categoryName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Matches(string search, string title, string? note, IEnumerable<string> steps)
    {
        return search.Length == 0
            || title.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (note?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
            || steps.Any(step => step.Contains(search, StringComparison.OrdinalIgnoreCase));
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
        if (item.DueOn is DateOnly due)
        {
            parts.Add($"Due {DayLabel(due, today)}");
        }

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
