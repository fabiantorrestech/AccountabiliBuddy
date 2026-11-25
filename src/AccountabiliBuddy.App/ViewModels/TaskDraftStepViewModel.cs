using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A step typed in the task editor's new-task popup, before the task exists: its title and a remove ×. The title is the user's text.</summary>
public sealed partial class TaskDraftStepViewModel
{
    private readonly Action<TaskDraftStepViewModel> _remove;

    public TaskDraftStepViewModel(string title, string letter, Action<TaskDraftStepViewModel> remove)
    {
        Title = title;
        Letter = letter;
        _remove = remove;
    }

    public string Title { get; }

    /// <summary>The step's letter ("a", "b"…), as the task will number it.</summary>
    public string Letter { get; }

    [RelayCommand]
    private void Remove() => _remove(this);
}
