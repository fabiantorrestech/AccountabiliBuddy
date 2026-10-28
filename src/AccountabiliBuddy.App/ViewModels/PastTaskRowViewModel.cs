using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A task an ended session left open, in "Unfinished from past sessions": nothing moves by itself, so each row offers its
/// own "Later" and "Delete".
/// </summary>
public sealed class PastTaskRowViewModel
{
    public PastTaskRowViewModel(Guid id, string title, string whenText, Func<Task> moveToLater, Func<Task> delete)
    {
        Id = id;
        Title = title;
        WhenText = whenText;
        MoveToLaterCommand = new AsyncRelayCommand(moveToLater);
        DeleteCommand = new AsyncRelayCommand(delete);
    }

    public Guid Id { get; }

    public string Title { get; }

    /// <summary>E.g. "Session of Thu 10/23 9:00 AM".</summary>
    public string WhenText { get; }

    public ICommand MoveToLaterCommand { get; }

    public ICommand DeleteCommand { get; }
}
