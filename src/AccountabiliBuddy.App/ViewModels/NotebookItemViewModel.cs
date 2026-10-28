using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One line in the Archive: a come-back, a note, or a task or subtask (unfinished or done), with its times. Tasks, subtasks and
/// come-backs have a check box (tick it, or untick it to open it again) and a ⋯ menu (rename, add a subtask, label, move, delete);
/// a note can be edited (✎, Enter keeps it, Esc cancels) or deleted (✕). A task's category shows as a dot.
/// </summary>
public sealed partial class NotebookItemViewModel : ObservableObject, ITaskMenuSource
{
    private readonly Func<string, Task>? _rename;
    private readonly Func<Task>? _delete;
    private readonly Func<string, Task>? _addStep;
    private readonly Func<NotebookItemViewModel, IReadOnlyList<TaskMenuEntry>>? _menu;

    /// <param name="Kind">"Come-back", "Note", "Unfinished task", "Done task", "Unfinished subtask" or "Done subtask".</param>
    /// <param name="whenText">E.g. "Parked Thu 10/23 9:41 AM · ticked 2:05 PM" or "Session of Thu 10/23 9:00 AM · done 9:47 AM".</param>
    /// <param name="sort">For ordering, newest first.</param>
    /// <param name="toggle">Ticks the line, or opens it again when <paramref name="isTicked"/> (null: no check box).</param>
    /// <param name="rename">Renames a task or subtask, or edits a come-back's or note's text (null: can't be edited).</param>
    /// <param name="delete">Deletes the item (null: can't be deleted).</param>
    /// <param name="addStep">Adds a step to a task (null: no steps).</param>
    /// <param name="menu">The ⋯ menu's entries, given the line (null: the ✎ ✕ buttons).</param>
    public NotebookItemViewModel(
        string kind, string text, string whenText, DateTimeOffset sort, Func<Task>? toggle = null,
        Func<string, Task>? rename = null, Func<Task>? delete = null, bool isSubtask = false, bool isTicked = false,
        Func<string, Task>? addStep = null, Func<NotebookItemViewModel, IReadOnlyList<TaskMenuEntry>>? menu = null,
        string? categoryColor = null)
    {
        Kind = kind;
        Text = text;
        WhenText = whenText;
        Sort = sort;
        ToggleCommand = toggle is null ? null : new AsyncRelayCommand(toggle);
        _rename = rename;
        _delete = delete;
        _addStep = addStep;
        _menu = menu;
        IsSubtask = isSubtask;
        IsTicked = isTicked;
        EditText = text;
        NewStepText = string.Empty;
        CategoryBrush = TaskCategoryBrushes.Of(categoryColor);
        HasCategory = categoryColor is not null;
    }

    public string Kind { get; }

    public string Text { get; }

    public string WhenText { get; }

    public DateTimeOffset Sort { get; }

    /// <summary>The check box: ticks it, or opens it again when it is ticked.</summary>
    public ICommand? ToggleCommand { get; }

    public bool CanTick => ToggleCommand is not null;

    public bool IsTicked { get; }

    public bool CanEdit => _rename is not null;

    /// <summary>It has a ⋯ menu (tasks, subtasks and come-backs); a note keeps its ✎ and ✕.</summary>
    public bool HasMenu => _menu is not null;

    public bool HasEditButtons => CanEdit && !HasMenu;

    public bool CanAddStep => _addStep is not null;

    /// <summary>The task's category colour, as a dot.</summary>
    public IBrush CategoryBrush { get; }

    public bool HasCategory { get; }

    /// <summary>The ✕ tooltip: a task takes its subtasks with it.</summary>
    public string DeleteTip => Kind is "Unfinished task" or "Done task" ? "Delete (a task goes with its subtasks)" : "Delete";

    /// <summary>Drawn set in under its task.</summary>
    public bool IsSubtask { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))]
    public partial bool IsEditing { get; set; }

    public bool IsNotEditing => !IsEditing;

    [ObservableProperty]
    public partial string EditText { get; set; }

    /// <summary>"Add subtask": the box under the line is open.</summary>
    [ObservableProperty]
    public partial bool IsAddingStep { get; set; }

    [ObservableProperty]
    public partial string NewStepText { get; set; }

    public IReadOnlyList<TaskMenuEntry> BuildMenu()
    {
        return _menu?.Invoke(this) ?? [];
    }

    /// <summary>Starts renaming it in place (from the ⋯ menu).</summary>
    public void BeginRename()
    {
        Edit();
    }

    /// <summary>Opens the box for a new step under it (from the ⋯ menu).</summary>
    public void BeginAddStep()
    {
        NewStepText = string.Empty;
        IsAddingStep = true;
    }

    [RelayCommand]
    private void Edit()
    {
        EditText = Text;
        IsEditing = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        IsEditing = false;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsEditing = false;
        string title = EditText.Trim();
        if (_rename is not null && title.Length > 0 && title != Text)
        {
            await _rename(title);
        }
    }

    [RelayCommand]
    private async Task SaveStepAsync()
    {
        IsAddingStep = false;
        string title = NewStepText.Trim();
        if (_addStep is not null && title.Length > 0)
        {
            await _addStep(title);
        }
    }

    [RelayCommand]
    private void CancelStep()
    {
        IsAddingStep = false;
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_delete is not null)
        {
            await _delete();
        }
    }
}
