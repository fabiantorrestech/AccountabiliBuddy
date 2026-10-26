using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One open come-back: its text and age, ticking it, editing its text in place (✎, Enter or click away saves, Esc
/// cancels) and bringing it into the checklist as a task.
/// </summary>
public sealed partial class ComebackRowViewModel : ObservableObject
{
    private readonly Comeback _comeback;
    private readonly ComebackStore _store;
    private readonly SessionTasksService? _tasks;

    /// <param name="tasks">The checklist "Add to tasks" puts it in; null hides that button.</param>
    public ComebackRowViewModel(Comeback comeback, ComebackStore store, SessionTasksService? tasks)
    {
        _comeback = comeback;
        _store = store;
        _tasks = tasks;
        EditText = string.Empty;
    }

    public string Text => _comeback.Text;

    /// <summary>E.g. "Thu 10:42 AM".</summary>
    public string When => $"{_comeback.CreatedAt.ToLocalTime():ddd h:mm tt}";

    public bool CanAddToTasks => _tasks is not null;

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditText { get; set; }

    [RelayCommand]
    private Task TickAsync()
    {
        return _store.TickAsync(_comeback.Id);
    }

    /// <summary>It becomes a task at the end of the checklist and leaves the come-backs (ticked).</summary>
    [RelayCommand]
    private Task AddToTasksAsync()
    {
        return _tasks?.AddFromComebackAsync(_comeback.Id) ?? Task.CompletedTask;
    }

    [RelayCommand]
    private void BeginEdit()
    {
        EditText = Text;
        IsEditing = true;
    }

    /// <summary>Enter or clicking away: the new text is kept (a blank one keeps the old text).</summary>
    [RelayCommand]
    private async Task SaveEditAsync()
    {
        IsEditing = false;
        await _store.EditAsync(_comeback.Id, EditText);
    }

    /// <summary>Escape: the text stays as it was.</summary>
    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
    }
}
