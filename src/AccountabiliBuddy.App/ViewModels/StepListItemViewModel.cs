using System.Windows.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One step in the step list popover (P3): its check box (a tick strikes it through), letter and title, "in focus" / "done" at the right,
/// and the target that brings an open step into focus.
/// </summary>
public sealed class StepListItemViewModel
{
    public StepListItemViewModel(string letter, string title, bool isDone, bool isFocused, ICommand toggle, ICommand? focus)
    {
        Letter = letter;
        Title = title;
        IsDone = isDone;
        IsFocused = isFocused;
        ToggleCommand = toggle;
        FocusCommand = focus;
    }

    /// <summary>"b."</summary>
    public string Letter { get; }

    public string Title { get; }

    public bool IsDone { get; }

    public bool IsFocused { get; }

    /// <summary>"in focus", "done" or nothing.</summary>
    public string StateText => IsDone ? "done" : IsFocused ? "in focus" : string.Empty;

    public bool HasStateText => StateText.Length > 0;

    public ICommand ToggleCommand { get; }

    /// <summary>The target: brings this open step into focus; null for a done or focused one.</summary>
    public ICommand? FocusCommand { get; }

    public bool CanFocus => FocusCommand is not null;
}
