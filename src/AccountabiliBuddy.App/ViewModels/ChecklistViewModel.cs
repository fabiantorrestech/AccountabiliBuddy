using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A guided session's checklist (ADR 24, 25) as the quick-session popup, the Session tab and the on-screen window show it:
/// open tasks with their subtasks, grouped under their categories (in each one's colour, "No category" last), the done
/// ones under a collapsed "Done (n)", and "End session · all tasks done" when the session may end at once. All views of
/// it share <see cref="SessionTasksService"/>.
/// </summary>
public sealed partial class ChecklistViewModel : ObservableObject, IDisposable
{
    // A category's name on the on-screen window's dark panel: white like its other text, for contrast.
    private static readonly IBrush CompactHeaderBrush = new SolidColorBrush(Color.Parse("#E6FFFFFF"));

    private readonly SessionTasksService _tasks;
    private readonly TimeProvider _time;
    private IReadOnlyList<TaskCategory>? _shownCategories;

    // The 4th task waiting on the "Keep it to 3" prompt.
    private string _heldTitle = string.Empty;
    private Guid? _heldCategoryId;

    // The category the "Add a task" box puts new tasks in (null: none); kept until changed or the category is deleted.
    private Guid? _newTaskCategoryId;

    public ChecklistViewModel(SessionTasksService tasks, ChecklistMode mode, TimeProvider time)
    {
        _tasks = tasks;
        _time = time;
        Mode = mode;
        NewTaskTitle = Heading = DoneHeader = ProgressText = CurrentTaskText = CurrentSubtaskText = NewCategoryName = NewTaskCategoryTip = string.Empty;
        NewTaskCategoryChoices = [];
        NewTaskCategoryBrush = CurrentCategoryBrush = Brushes.Transparent;
        _tasks.Changed += OnChanged;
        Refresh();
    }

    public ChecklistMode Mode { get; }

    private UndoBarViewModel? _undo;

    /// <summary>The bar after a delete, "Deleted “…” · Undo" (made when a view first asks for it).</summary>
    public UndoBarViewModel Undo => _undo ??= new UndoBarViewModel(_tasks);

    public bool IsDraft => Mode == ChecklistMode.Draft;

    public bool IsCompact => Mode == ChecklistMode.Compact;

    /// <summary>Ticks show once a session runs.</summary>
    public bool CanTick => Mode != ChecklistMode.Draft;

    /// <summary>Adding, renaming, moving, removing and subtasks: not in the on-screen window (it can't take typing).</summary>
    public bool CanEdit => Mode != ChecklistMode.Compact;

    /// <summary>A task's ⚑ parks it as a come-back: where tasks are edited, once the come-backs exist.</summary>
    public bool CanPark => CanEdit && _tasks.Comebacks is not null;

    /// <summary>The open tasks grouped by category, in the categories' order, "No category" last.</summary>
    public ObservableCollection<ChecklistGroupViewModel> OpenGroups { get; } = new();

    public ObservableCollection<TaskRowViewModel> DoneTasks { get; } = new();

    /// <summary>The open tasks in the order shown, flat: the numbered rows on Today (1, 2, 3...).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TaskRowViewModel> OpenTasks { get; set; } = [];

    /// <summary>The number of the row the "Add a task" box stands in (the open tasks plus one).</summary>
    [ObservableProperty]
    public partial int AddRowNumber { get; set; } = 1;

    /// <summary>The add row's placeholder: "Type a third, or press Enter to skip".</summary>
    [ObservableProperty]
    public partial string AddPlaceholderText { get; set; } = string.Empty;

    /// <summary>The "Categories" panel's rows (rename, recolour, delete).</summary>
    public ObservableCollection<TaskCategoryRowViewModel> CategoryRows { get; } = new();

    /// <summary>Any task category exists: the "Add a task" box's category dot shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsCategoryPickers))]
    public partial bool HasCategories { get; set; }

    /// <summary>The dot on each open task that changes its category: where tasks are edited, once a category exists.</summary>
    public bool ShowsCategoryPickers => CanEdit && HasCategories;

    /// <summary>The "Categories" panel is open (add, rename, recolour, delete).</summary>
    [ObservableProperty]
    public partial bool IsManagingCategories { get; set; }

    [ObservableProperty]
    public partial string NewCategoryName { get; set; }

    /// <summary>The menu by the "Add a task" box: which category new tasks go in.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TaskCategoryChoiceViewModel> NewTaskCategoryChoices { get; set; }

    /// <summary>The colour of the category new tasks go in; transparent for no category.</summary>
    [ObservableProperty]
    public partial IBrush NewTaskCategoryBrush { get; set; }

    /// <summary>The dot's tip, e.g. "New tasks go in Work (click to change)".</summary>
    [ObservableProperty]
    public partial string NewTaskCategoryTip { get; set; }

    /// <summary>The colour of the task the on-screen bar shows (its dot there); transparent with no category.</summary>
    [ObservableProperty]
    public partial IBrush CurrentCategoryBrush { get; set; }

    [ObservableProperty]
    public partial bool HasCurrentCategory { get; set; }

    [ObservableProperty]
    public partial string Heading { get; set; }

    /// <summary>E.g. "Done (2)".</summary>
    [ObservableProperty]
    public partial string DoneHeader { get; set; }

    [ObservableProperty]
    public partial bool HasDone { get; set; }

    [ObservableProperty]
    public partial bool HasTasks { get; set; }

    /// <summary>E.g. "2/5".</summary>
    [ObservableProperty]
    public partial string ProgressText { get; set; }

    /// <summary>The task the on-screen window's bar shows (the picked one, else the first open task), e.g. "Fix tests".</summary>
    [ObservableProperty]
    public partial string CurrentTaskText { get; set; }

    /// <summary>The shown task's number among the open tasks (as on its row); 0 = none.</summary>
    [ObservableProperty]
    public partial int CurrentTaskNumber { get; set; }

    /// <summary>The open task the on-screen bar shows (or one of whose steps it shows), for Today's steps list; null when there is none.</summary>
    [ObservableProperty]
    public partial TaskRowViewModel? CurrentTask { get; set; }

    /// <summary>The open task after the current one, for "Next: ..."; empty when there is none.</summary>
    [ObservableProperty]
    public partial string NextTaskTitle { get; set; } = string.Empty;

    /// <summary>Its subtask shown under it (the picked one, else its first open one); empty when it has none.</summary>
    [ObservableProperty]
    public partial string CurrentSubtaskText { get; set; }

    [ObservableProperty]
    public partial bool CanEndAtOnce { get; set; }

    [ObservableProperty]
    public partial string NewTaskTitle { get; set; }

    /// <summary>The "Keep it to 3: park this for later?" prompt is open.</summary>
    [ObservableProperty]
    public partial bool IsOverLimitPrompt { get; set; }

    /// <summary>The prompt's title line: the held task.</summary>
    public string OverLimitText => $"Keep it to {SessionTasksService.MaxTasks}: park \"{_heldTitle}\" for later?";

    /// <summary>Why the 4th task is added anyway.</summary>
    [ObservableProperty]
    public partial string OverLimitReason { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? OverLimitError { get; set; }

    partial void OnIsOverLimitPromptChanged(bool value)
    {
        OnPropertyChanged(nameof(OverLimitText));
    }

    public bool NotifyAllDone
    {
        get => _tasks.NotifyAllDone;
        set
        {
            _ = _tasks.SetNotifyAllDoneAsync(value);
            OnPropertyChanged();
        }
    }

    /// <summary>The "Tell me when all are done" choice: on the Session tab while a session runs.</summary>
    public bool ShowsNotifyChoice => Mode == ChecklistMode.Live;

    /// <summary>Called by the views every few seconds so "End session" appears once the 10 minutes have passed.</summary>
    public void RefreshEndAtOnce()
    {
        CanEndAtOnce = Mode != ChecklistMode.Draft && _tasks.CanEndAtOnce;
    }

    /// <summary>Raised to put the keyboard in the "Add a task" box (the start nudge).</summary>
    public event EventHandler? FocusEntryRequested;

    /// <summary>Raised just before the rows are rebuilt, so a row's open flyout can close first (a flyout open inside a row that goes away crashes Avalonia).</summary>
    public event EventHandler? RowsRebuilding;

    public void RequestFocus()
    {
        FocusEntryRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _tasks.Changed -= OnChanged;
        _undo?.Dispose();
    }

    /// <summary>
    /// A row dropped by its drag handle: a task before or after another task (taking that task's category, so it can
    /// join another group), or a subtask before or after another subtask of its task.
    /// </summary>
    public Task MoveToAsync(Guid id, Guid targetId, bool after)
    {
        return _tasks.MoveToAsync(id, targetId, after);
    }

    [RelayCommand]
    private async Task AddTaskAsync()
    {
        string title = NewTaskTitle;
        if (title.Trim().Length == 0 || IsOverLimitPrompt)
        {
            return;
        }

        if (_tasks.IsAtTaskLimit)
        {
            // A 4th task: hold it and ask (park it, or add it anyway with a reason).
            _heldTitle = title;
            _heldCategoryId = _newTaskCategoryId;
            NewTaskTitle = string.Empty;
            OverLimitReason = string.Empty;
            OverLimitError = null;
            IsOverLimitPrompt = true;
            return;
        }

        NewTaskTitle = string.Empty;
        await _tasks.AddAsync(title, null, _newTaskCategoryId);
    }

    /// <summary>"Keep it to 3": the held task goes to Come back to.</summary>
    [RelayCommand]
    private async Task ParkHeldTaskAsync()
    {
        string title = _heldTitle;
        CloseOverLimitPrompt();
        await _tasks.ParkTitleAsync(title);
    }

    /// <summary>"Add anyway": needs a reason of at least 10 characters; it stays on this PC and is counted for the review.</summary>
    [RelayCommand]
    private async Task AddHeldTaskAnywayAsync()
    {
        if (!await _tasks.AddOverLimitAsync(_heldTitle, _heldCategoryId, OverLimitReason))
        {
            OverLimitError = $"Write why, at least {SessionTasksService.MinOverLimitReasonLength} characters.";
            return;
        }

        CloseOverLimitPrompt();
    }

    /// <summary>Drops the held task without adding or parking it.</summary>
    [RelayCommand]
    private void CancelHeldTask()
    {
        CloseOverLimitPrompt();
    }

    private void CloseOverLimitPrompt()
    {
        IsOverLimitPrompt = false;
        OverLimitReason = string.Empty;
        OverLimitError = null;
        _heldTitle = string.Empty;
    }

    [RelayCommand]
    private void ToggleManageCategories()
    {
        IsManagingCategories = !IsManagingCategories;
    }

    /// <summary>Enter in the panel's "New category" box: added in the next colour no category has yet.</summary>
    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        string name = NewCategoryName;
        NewCategoryName = string.Empty;
        await _tasks.AddCategoryAsync(name);
    }

    private Task PickNewTaskCategory(Guid? categoryId)
    {
        _newTaskCategoryId = categoryId;
        RefreshNewTaskCategory();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void EndAtOnce()
    {
        _tasks.EndAtOnce();
    }

    /// <summary>The on-screen bar's box by the task: ticks the task it shows (and its subtasks).</summary>
    [RelayCommand]
    private Task TickCurrentTaskAsync()
    {
        return _tasks.Current.Shown is { } shown ? _tasks.ToggleAsync(shown.Task.Id) : Task.CompletedTask;
    }

    /// <summary>The on-screen bar's box by the subtask: ticks the subtask it shows.</summary>
    [RelayCommand]
    private Task TickCurrentSubtaskAsync()
    {
        return _tasks.Current.Shown?.Subtask is { } subtask ? _tasks.ToggleAsync(subtask.Id) : Task.CompletedTask;
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        RowsRebuilding?.Invoke(this, EventArgs.Empty);
        SessionChecklist checklist = _tasks.Current;
        IReadOnlyList<TaskCategory> categories = _tasks.Categories;
        RefreshCategories(categories);

        var openTasks = new List<TaskRowViewModel>();
        DoneTasks.Clear();
        foreach (SessionTask task in checklist.Tasks)
        {
            var row = new TaskRowViewModel(task, DoneText(task, checklist), checklist.Shown, categories, _tasks);
            if (task.IsDone && CanTick)
            {
                DoneTasks.Add(row);
            }
            else
            {
                openTasks.Add(row);
            }
        }

        // The service keeps the tasks grouped in the categories' order, so the groups come out in that order.
        bool showsHeaders = openTasks.Any(row => row.HasCategory);
        OpenGroups.Clear();
        foreach (IGrouping<Guid?, TaskRowViewModel> group in openTasks.GroupBy(row => row.CategoryId))
        {
            TaskRowViewModel first = group.First();
            IBrush nameBrush = IsCompact ? CompactHeaderBrush : first.CategoryBrush;
            OpenGroups.Add(new ChecklistGroupViewModel(first.CategoryName, first.CategoryBrush, nameBrush, first.HasCategory, showsHeaders, group.ToList()));
        }

        // One sequence over the open tasks in the order shown, across the groups; done tasks and subtasks aren't numbered.
        int number = 0;
        CurrentTaskNumber = 0;
        foreach (TaskRowViewModel row in OpenGroups.SelectMany(group => group.Tasks).Where(row => !row.IsDone))
        {
            number++;
            row.Number = number;
            int letter = 0;
            foreach (SubtaskRowViewModel subtask in row.Subtasks.Where(subtask => !subtask.IsDone))
            {
                subtask.Letter = ++letter;
            }

            if (checklist.Shown?.Task.Id == row.Id)
            {
                CurrentTaskNumber = number;
            }
        }

        List<TaskRowViewModel> openInOrder = OpenGroups.SelectMany(group => group.Tasks).Where(row => !row.IsDone).ToList();
        int currentIndex = openInOrder.FindIndex(row => row.IsCurrent || row.Subtasks.Any(subtask => subtask.IsCurrent));
        CurrentTask = currentIndex >= 0 ? openInOrder[currentIndex] : null;
        OpenTasks = openInOrder;
        AddRowNumber = openInOrder.Count + 1;
        AddPlaceholderText = openInOrder.Count switch
        {
            0 => "Type the first one, then press Enter",
            1 => "Type a second, or press Enter to skip",
            2 => "Type a third, or press Enter to skip",
            _ => "Add another (a fourth needs a reason)",
        };
        NextTaskTitle = currentIndex >= 0 && currentIndex + 1 < openInOrder.Count ? openInOrder[currentIndex + 1].Title : string.Empty;

        TaskCategory? currentCategory = categories.FirstOrDefault(category => category.Id == checklist.Shown?.Task.CategoryId);
        CurrentCategoryBrush = TaskCategoryBrushes.Of(currentCategory?.Color);
        HasCurrentCategory = currentCategory is not null;

        HasTasks = checklist.HasTasks;
        HasDone = DoneTasks.Count > 0;
        DoneHeader = $"Done ({DoneTasks.Count})";
        ProgressText = checklist.HasTasks ? $"{checklist.DoneCount}/{checklist.Tasks.Count}" : string.Empty;
        CurrentTaskText = checklist.Shown?.Task.Title ?? (checklist.AllDone ? "All tasks done" : string.Empty);
        CurrentSubtaskText = checklist.Shown?.Subtask?.Title ?? string.Empty;
        Heading = Mode == ChecklistMode.Draft
            ? "What are you doing first? (optional)"
            : checklist.HasTasks ? $"Tasks · {ProgressText}" : "Tasks";
        RefreshEndAtOnce();
    }

    // The "Categories" panel is rebuilt only when the categories changed, so a name being typed there isn't lost when a
    // task is ticked meanwhile (on the phone, say).
    private void RefreshCategories(IReadOnlyList<TaskCategory> categories)
    {
        if (ReferenceEquals(categories, _shownCategories))
        {
            return;
        }

        _shownCategories = categories;
        HasCategories = categories.Count > 0;
        CategoryRows.Clear();
        foreach (TaskCategory category in categories)
        {
            CategoryRows.Add(new TaskCategoryRowViewModel(category, _tasks));
        }

        if (_newTaskCategoryId is Guid id && categories.All(category => category.Id != id))
        {
            _newTaskCategoryId = null;
        }

        RefreshNewTaskCategory();
    }

    private void RefreshNewTaskCategory()
    {
        IReadOnlyList<TaskCategory> categories = _tasks.Categories;
        TaskCategory? chosen = categories.FirstOrDefault(category => category.Id == _newTaskCategoryId);
        NewTaskCategoryBrush = TaskCategoryBrushes.Of(chosen?.Color);
        NewTaskCategoryTip = chosen is null ? "New tasks have no category (click to choose one)" : $"New tasks go in {chosen.Name} (click to change)";
        NewTaskCategoryChoices =
        [
            .. categories.Select(category => new TaskCategoryChoiceViewModel(
                category.Id, category.Name, TaskCategoryBrushes.Of(category.Color), category.Id == _newTaskCategoryId, PickNewTaskCategory)),
            new TaskCategoryChoiceViewModel(null, "No category", Brushes.Transparent, _newTaskCategoryId is null, PickNewTaskCategory),
        ];
    }

    // "Done 10:42 AM · 25m": when, and how long since the previous tick (or the session's start).
    private string? DoneText(SessionTask task, SessionChecklist checklist)
    {
        if (task.DoneAt is not DateTimeOffset doneAt)
        {
            return null;
        }

        DateTimeOffset? before = checklist.Tasks
            .Where(other => other.DoneAt is DateTimeOffset at && at < doneAt)
            .Select(other => other.DoneAt)
            .Max() ?? checklist.SessionStart;
        string when = TimeZoneInfo.ConvertTime(doneAt, _time.LocalTimeZone).ToString("t");
        if (before is not DateTimeOffset from)
        {
            return $"Done {when}";
        }

        int minutes = (int)Math.Round((doneAt - from).TotalMinutes);
        return $"Done {when} · {(minutes >= 60 ? $"{minutes / 60}h {minutes % 60:00}m" : $"{minutes}m")}";
    }
}
