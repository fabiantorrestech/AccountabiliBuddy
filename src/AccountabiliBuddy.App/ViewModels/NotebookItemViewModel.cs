using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One line in the Notebook: a come-back, a note, or a task or subtask (unfinished or done), with its times. Tasks and
/// subtasks can be renamed (✎, Enter keeps it, Esc cancels) or deleted (✕).
/// </summary>
public sealed partial class NotebookItemViewModel : ObservableObject
{
    private readonly Func<string, Task>? _rename;
    private readonly Func<Task>? _delete;

    /// <param name="Kind">"Come-back", "Note", "Unfinished task", "Done task", "Unfinished subtask" or "Done subtask".</param>
    /// <param name="whenText">E.g. "Parked Thu 10/23 9:41 AM · ticked 2:05 PM" or "Session of Thu 10/23 9:00 AM · done 9:47 AM".</param>
    /// <param name="sort">For ordering, newest first.</param>
    /// <param name="tick">Ticks an open come-back off (null for everything else).</param>
    /// <param name="rename">Renames a task or subtask (null: can't be renamed).</param>
    /// <param name="delete">Deletes a task or subtask (null: can't be deleted).</param>
    public NotebookItemViewModel(
        string kind, string text, string whenText, DateTimeOffset sort, IAsyncRelayCommand? tick = null,
        Func<string, Task>? rename = null, Func<Task>? delete = null, bool isSubtask = false)
    {
        Kind = kind;
        Text = text;
        WhenText = whenText;
        Sort = sort;
        Tick = tick;
        _rename = rename;
        _delete = delete;
        IsSubtask = isSubtask;
        EditText = text;
    }

    public string Kind { get; }

    public string Text { get; }

    public string WhenText { get; }

    public DateTimeOffset Sort { get; }

    public IAsyncRelayCommand? Tick { get; }

    public bool CanTick => Tick is not null;

    public bool CanEdit => _rename is not null;

    /// <summary>Drawn set in under its task.</summary>
    public bool IsSubtask { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))]
    public partial bool IsEditing { get; set; }

    public bool IsNotEditing => !IsEditing;

    [ObservableProperty]
    public partial string EditText { get; set; }

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
    private async Task DeleteAsync()
    {
        if (_delete is not null)
        {
            await _delete();
        }
    }
}
