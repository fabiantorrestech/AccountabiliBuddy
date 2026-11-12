using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One step of the task shown in the Tasks page's detail card: a check box, its title and a ⋯ menu (rename it, add a step after
/// it, delete it). A step has no category of its own: it goes with its task's.
/// </summary>
public sealed partial class TaskStepViewModel : ObservableObject, ITaskMenuSource
{
    private readonly Func<Guid, Task> _toggle;
    private readonly Func<Guid, Task> _remove;
    private readonly Func<Guid, string, Task> _rename;
    private readonly Func<Guid, string, Task> _addAfter;

    public TaskStepViewModel(
        Guid id, string title, bool isDone, bool canEdit, Func<Guid, Task> toggle, Func<Guid, Task> remove,
        Func<Guid, string, Task> rename, Func<Guid, string, Task> addAfter)
    {
        Id = id;
        Title = title;
        IsDone = isDone;
        CanEdit = canEdit;
        _toggle = toggle;
        _remove = remove;
        _rename = rename;
        _addAfter = addAfter;
        EditText = title;
        NewAfterText = string.Empty;
        ToggleCommand = new AsyncRelayCommand(() => _toggle(Id));
        RemoveCommand = new AsyncRelayCommand(() => _remove(Id));
    }

    public Guid Id { get; }

    public string Title { get; }

    public bool IsDone { get; }

    /// <summary>Its letter chip, "a", "b" … (its place among the task's steps).</summary>
    public string Letter { get; init; } = string.Empty;

    public bool HasLetter => Letter.Length > 0;

    /// <summary>The chip's tooltip: the step's ID, "142b", or "Step b" when the task has no number yet.</summary>
    public string LetterTip { get; init; } = string.Empty;

    /// <summary>One of the session's actions: an "in focus" tag and a Surface2 background.</summary>
    public bool IsFocused { get; init; }

    /// <summary>Its time limit, e.g. "10 min"; empty for none.</summary>
    public string TimeText { get; init; } = string.Empty;

    public bool HasTime => TimeText.Length > 0;

    /// <summary>The target: brings this open step into the running session's focus (the session's cap decides); null where it can't.</summary>
    public ICommand? FocusCommand { get; init; }

    public bool CanFocus => FocusCommand is not null;

    /// <summary>It can be dragged by its handle (or moved with Alt+↑/↓) among its task's steps.</summary>
    public bool CanDrag { get; init; }

    /// <summary>Where a dragged step would land relative to this one: -1 above, 1 below, 0 none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsLineAbove), nameof(ShowsLineBelow))]
    public partial int DropMark { get; set; }

    public bool ShowsLineAbove => DropMark < 0;

    public bool ShowsLineBelow => DropMark > 0;

    /// <summary>False for a task that is already done: its steps are shown, not ticked.</summary>
    public bool CanEdit { get; }

    /// <summary>Ticks or unticks the step (the check box).</summary>
    public ICommand ToggleCommand { get; }

    public ICommand RemoveCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))]
    public partial bool IsEditing { get; set; }

    public bool IsNotEditing => !IsEditing;

    [ObservableProperty]
    public partial string EditText { get; set; }

    /// <summary>"Add a step after": the box under this step is open.</summary>
    [ObservableProperty]
    public partial bool IsAddingAfter { get; set; }

    [ObservableProperty]
    public partial string NewAfterText { get; set; }

    public IReadOnlyList<TaskMenuEntry> BuildMenu()
    {
        return
        [
            TaskMenuEntry.Do("Rename", BeginEdit, MaterialIconKind.PencilOutline),
            TaskMenuEntry.Do("Add a step after", BeginAddAfter, MaterialIconKind.PlaylistPlus),
            TaskMenuEntry.Separator,
            TaskMenuEntry.Async("Delete", () => _remove(Id), MaterialIconKind.DeleteOutline),
        ];
    }

    [RelayCommand]
    private void BeginEdit()
    {
        EditText = Title;
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveEditAsync()
    {
        IsEditing = false;
        string text = EditText.Trim();
        if (text.Length > 0 && text != Title)
        {
            await _rename(Id, text);
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
    }

    [RelayCommand]
    private void BeginAddAfter()
    {
        NewAfterText = string.Empty;
        IsAddingAfter = true;
    }

    [RelayCommand]
    private async Task SaveAddAfterAsync()
    {
        string text = NewAfterText.Trim();
        IsAddingAfter = false;
        if (text.Length > 0)
        {
            await _addAfter(Id, text);
        }
    }

    [RelayCommand]
    private void CancelAddAfter()
    {
        IsAddingAfter = false;
    }
}
