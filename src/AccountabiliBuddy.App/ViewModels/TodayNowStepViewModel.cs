using System.Windows.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One step of the running task on Today's This session list (round 2, brief §4.2: the current task's steps open, with letters a–d;
/// a done step struck through; "done" / "in focus" at the right). Its box ticks or unticks the step, the same as the step list's.
/// </summary>
public sealed class TodayNowStepViewModel
{
    public TodayNowStepViewModel(string letter, string title, bool isDone, bool inFocus, ICommand toggle)
    {
        Letter = letter;
        Title = title;
        IsDone = isDone;
        InFocus = inFocus;
        ToggleCommand = toggle;
    }

    public string Letter { get; }

    public string Title { get; }

    public bool IsDone { get; }

    public bool InFocus { get; }

    /// <summary>"done", "in focus", or empty.</summary>
    public string Note => IsDone ? "done" : InFocus ? "in focus" : string.Empty;

    public bool HasNote => Note.Length > 0;

    public ICommand ToggleCommand { get; }

    /// <summary>What it shows, to tell whether a row built again draws the same.</summary>
    public string Signature => $"{Letter}\u0002{Title}\u0002{IsDone}\u0002{InFocus}";
}
