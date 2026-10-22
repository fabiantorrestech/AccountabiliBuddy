using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// What a task row and a subtask row of the checklist share: tick it, pick it as the one the on-screen bar shows,
/// rename it in place, move it up or down, remove it.
/// </summary>
public abstract partial class ChecklistRowViewModel : ObservableObject
{
    private readonly SessionTasksService _tasks;
    private bool _isDone;

    protected ChecklistRowViewModel(Guid id, string title, bool isDone, bool isCurrent, SessionTasksService tasks)
    {
        Id = id;
        Title = title;
        _isDone = isDone;
        IsCurrent = isCurrent;
        _tasks = tasks;
        EditTitle = string.Empty;
    }

    public Guid Id { get; }

    public string Title { get; }

    /// <summary>The one the on-screen bar shows (marked ▸).</summary>
    public bool IsCurrent { get; }

    /// <summary>Ticking it here ticks it in the checklist.</summary>
    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (value != _isDone)
            {
                _isDone = value;
                _ = _tasks.ToggleAsync(Id);
            }
        }
    }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditTitle { get; set; }

    protected SessionTasksService Tasks => _tasks;

    /// <summary>Clicking its name: the on-screen bar shows it; clicking the current one again goes back to the first open task.</summary>
    [RelayCommand]
    private Task PickAsync()
    {
        return _tasks.PickAsync(Id);
    }

    [RelayCommand]
    private void BeginEdit()
    {
        EditTitle = Title;
        IsEditing = true;
    }

    /// <summary>Enter: the new name is kept (a blank one keeps the old name).</summary>
    [RelayCommand]
    private async Task SaveEditAsync()
    {
        IsEditing = false;
        if (EditTitle.Trim() != Title)
        {
            await _tasks.RenameAsync(Id, EditTitle);
        }
    }

    /// <summary>Escape: the name stays as it was.</summary>
    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
    }

    [RelayCommand]
    private Task MoveUpAsync()
    {
        return _tasks.MoveAsync(Id, -1);
    }

    [RelayCommand]
    private Task MoveDownAsync()
    {
        return _tasks.MoveAsync(Id, 1);
    }

    [RelayCommand]
    private Task RemoveAsync()
    {
        return _tasks.RemoveAsync(Id);
    }
}
