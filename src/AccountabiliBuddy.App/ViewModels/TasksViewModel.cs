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
/// selected. Every list reads its store again when one changes. Nothing here loosens a rule of a running session: a task can
/// leave it only as "taken off open" (ADR 25), and a 4th task is never added to it.
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
        CountsText = LaterHeader = LaterNextDueText = DoneHeader = UnfinishedHeader = AutoSavedHeader = AddPlaceholder = SearchText = NewTaskText = string.Empty;
        _tasks.Changed += OnStoreChanged;
        _comebacks.Changed += OnStoreChanged;
        _autoSaved.Changed += OnStoreChanged;
        _session.PropertyChanged += OnSessionPropertyChanged;
        if (_session.Focus is not null)
        {
            _session.Focus.PropertyChanged += OnFocusPropertyChanged;
        }

        Refresh();
    }

    /// <summary>The Notebook, restyled as the Archive row at the bottom.</summary>
    public NotebookViewModel Archive { get; }

    public ObservableCollection<TaskListRowViewModel> SessionRows { get; } = new();

    public ObservableCollection<TaskListRowViewModel> ComeBackRows { get; } = new();

    public ObservableCollection<TaskListRowViewModel> LaterRows { get; } = new();

    public ObservableCollection<TaskListRowViewModel> DoneRows { get; } = new();

    public ObservableCollection<PastTaskRowViewModel> UnfinishedRows { get; } = new();

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

    public bool HasDetail => Detail is not null;

    public bool HasSessionRows => SessionRows.Count > 0;

    public bool HasComeBackRows => ComeBackRows.Count > 0;

    public bool HasLater => _later.Items.Count > 0;

    public bool HasDone => DoneRows.Count > 0;

    public bool HasUnfinished => UnfinishedRows.Count > 0;

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
        IReadOnlySet<Guid> ids = UnfinishedRows.Select(row => row.Id).ToHashSet();
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

    // ---- What the detail card does (it calls these) ----

    internal Task RenameAsync(TaskRowKey key, string title)
    {
        return key.Kind switch
        {
            TaskRowKind.Session => _tasks.RenameAsync(key.Id, title),
            TaskRowKind.Later => EditLaterAsync(key.Id, item => item with { Title = title }),
            TaskRowKind.ComeBack => _comebacks.EditAsync(key.Id, title),
            _ => Task.CompletedTask,
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
            TaskRowKind.Session => _tasks.RemoveAsync(stepId),
            TaskRowKind.Later => EditLaterAsync(key.Id, item => item with
            {
                Subtasks = (item.Subtasks ?? []).Where(step => step.Id != stepId).ToList() is { Count: > 0 } kept ? kept : null,
            }),
            _ => Task.CompletedTask,
        };
    }

    internal Task EditLaterAsync(Guid id, Func<SyncLaterItem, SyncLaterItem> change)
    {
        return _later.EditAsync(id, change);
    }

    /// <summary>"Move to Later": the task leaves this session (counted as taken off open while it runs) and shows on the Later list.</summary>
    internal async Task MoveToLaterAsync(TaskRowKey key)
    {
        if (key.Kind == TaskRowKind.Session)
        {
            Guid? laterId = await _tasks.MoveToLaterAsync(key.Id);
            if (laterId is Guid id)
            {
                IsLaterOpen = true;
                Select(new TaskRowKey(TaskRowKind.Later, id));
            }
        }
        else if (key.Kind == TaskRowKind.ComeBack && _comebacks.All.FirstOrDefault(item => item.Id == key.Id) is Comeback comeback)
        {
            SyncLaterItem? added = await _later.AddAsync(comeback.Text, null, null, null, null, null, null);
            await _comebacks.DeleteAsync(key.Id);
            if (added is not null)
            {
                IsLaterOpen = true;
                Select(new TaskRowKey(TaskRowKind.Later, added.Id));
            }
        }
    }

    /// <summary>"Move to this session": a Later item or a come-back becomes a task of this session, unless it already holds its limit.</summary>
    internal async Task MoveToSessionAsync(TaskRowKey key)
    {
        if (SessionIsFull)
        {
            Detail?.SetMessage($"This session already has {_tasks.TaskAllowance} tasks.");
            return;
        }

        if (key.Kind == TaskRowKind.Later)
        {
            (Guid? taskId, string? error) = await _tasks.MoveFromLaterAsync(key.Id);
            if (taskId is Guid id)
            {
                Select(new TaskRowKey(TaskRowKind.Session, id));
            }
            else
            {
                Detail?.SetMessage(error ?? string.Empty);
            }
        }
        else if (key.Kind == TaskRowKind.ComeBack && await _tasks.AddFromComebackAsync(key.Id))
        {
            Guid? added = _tasks.Current.Tasks.LastOrDefault()?.Id;
            if (added is Guid id)
            {
                Select(new TaskRowKey(TaskRowKind.Session, id));
            }
        }
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
            (Guid? movedId, string? error) = await _tasks.MoveFromLaterAsync(key.Id);
            if (movedId is not Guid id)
            {
                Detail?.SetMessage(error ?? string.Empty);
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

    internal async Task DeleteAsync(TaskRowKey key)
    {
        if (key.Kind == TaskRowKind.Later)
        {
            await _later.DeleteAsync(key.Id);
        }
        else if (key.Kind == TaskRowKind.ComeBack)
        {
            await _comebacks.DeleteAsync(key.Id);
        }
    }

    internal Task TickComeBackAsync(Guid id) => _comebacks.TickAsync(id);

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

        // This session: the open tasks, numbered as the session runs them (the numbers don't change with a filter).
        SessionRows.Clear();
        List<SessionTask> open = current.Tasks.Where(task => !task.IsDone).ToList();
        int total = open.Count;
        for (int index = 0; index < open.Count; index++)
        {
            SessionTask task = open[index];
            if (!InFilter(task.CategoryId, null) || !Matches(search, task.Title, task.Note, task.Subtasks.Select(step => step.Title)))
            {
                continue;
            }

            SessionRows.Add(new TaskListRowViewModel(
                new TaskRowKey(TaskRowKind.Session, task.Id), task.Title, SessionLine(task), SelectRow, index + 1, task.Id == runningId)
            {
                CategoryId = task.CategoryId,
            });
        }

        // Come back to: open come-backs; they have no category, so a category filter leaves them out.
        ComeBackRows.Clear();
        if (_categoryFilterId is null)
        {
            foreach (Comeback comeback in _comebacks.Open.Where(item => Matches(search, item.Text, null, [])))
            {
                Guid id = comeback.Id;
                ComeBackRows.Add(new TaskListRowViewModel(
                    new TaskRowKey(TaskRowKind.ComeBack, id), comeback.Text, $"Parked {When(comeback.CreatedAt, today)}", SelectRow, tick: () => _comebacks.TickAsync(id)));
            }
        }

        // Later: every item, shown when its group is open (or while searching).
        LaterRows.Clear();
        IReadOnlyList<SyncLaterItem> laterItems = _later.Items;
        foreach (SyncLaterItem item in laterItems.Where(item => InFilter(null, item.Category) && Matches(search, item.Title, item.Note, (item.Subtasks ?? []).Select(step => step.Title))))
        {
            LaterRows.Add(new TaskListRowViewModel(new TaskRowKey(TaskRowKind.Later, item.Id), item.Title, LaterLine(item, today), SelectRow) { CategoryName = item.Category });
        }

        // Done today: ticked tasks of the running checklist, and of sessions that ended today.
        DoneRows.Clear();
        foreach (SessionTask task in DoneToday(today).Where(task => InFilter(task.CategoryId, task.Category) && Matches(search, task.Title, task.Note, task.Subtasks.Select(step => step.Title))))
        {
            DoneRows.Add(new TaskListRowViewModel(
                new TaskRowKey(TaskRowKind.Done, task.Id), task.Title, $"Done {Clock(Local(task.DoneAt!.Value))}", SelectRow));
        }

        // Unfinished from past sessions: offered to Later, never moved on their own.
        UnfinishedRows.Clear();
        DateTimeOffset from = _time.GetUtcNow() - PastShown;
        foreach (PastTask past in _tasks.PastUnfinished().Where(past => past.SessionStart >= from && Matches(search, past.Task.Title, past.Task.Note, [])))
        {
            Guid id = past.Task.Id;
            UnfinishedRows.Add(new PastTaskRowViewModel(
                id, past.Task.Title, $"Session of {When(past.SessionStart, today)}",
                () => _tasks.MovePastToLaterAsync(new HashSet<Guid> { id }),
                () => _tasks.EditHistoryAsync(id, null)));
        }

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
                () => _autoSaved.RemoveAsync(id)));
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
        DoneHeader = $"Done today · {DoneToday(today).Count()}";
        UnfinishedHeader = $"Unfinished from past sessions · {_tasks.PastUnfinished().Count(past => past.SessionStart >= from)}";
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

    // The detail card follows the selected key; nothing selected (or gone) falls back to the first task of the session.
    private void ShowSelected()
    {
        TaskSnapshot? snapshot = _selectedKey is TaskRowKey key ? Read(key) : null;
        if (snapshot is null && SessionRows.Count > 0)
        {
            _selectedKey = SessionRows[0].Key;
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
        foreach (TaskListRowViewModel row in SessionRows.Concat(ComeBackRows).Concat(LaterRows).Concat(DoneRows))
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
                SessionTask? task = DoneToday(DateOnly.FromDateTime(LocalNow())).FirstOrDefault(candidate => candidate.Id == key.Id);
                return task is null
                    ? null
                    : new TaskSnapshot(key, "Done today", task.Title, task.Subtasks, task.EstimateMinutes, task.Note, null, null, false, true, null);
            }
        }
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
            chips.Add(new TaskChipViewModel(category.Name, _categoryFilterId == id, () => PickCategoryAsync(id)));
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

        if (categoryId is not null)
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
