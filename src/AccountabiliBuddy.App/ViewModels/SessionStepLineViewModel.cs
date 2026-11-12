using System.Globalization;
using System.Windows.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One line under a task in This session's Grouped view (Tasks redesign `05` §2): a focused step (its place's number, a check box, the
/// letter and title), a step passed over ("skipped", with the target to bring it into focus), one of the next open steps (greyed, with
/// the target), or a summary ("done (a–b, d)", "+3 more steps"). Its commands go through the session's own rules (the cap decides).
/// </summary>
public sealed class SessionStepLineViewModel
{
    private SessionStepLineViewModel(string text, string letter, int number, bool isFocused, bool isSkipped, bool isUpcoming, ICommand? toggle, ICommand? focus)
    {
        Text = text;
        Letter = letter;
        Number = number;
        IsFocused = isFocused;
        IsSkipped = isSkipped;
        IsUpcoming = isUpcoming;
        ToggleCommand = toggle;
        FocusCommand = focus;
    }

    /// <summary>The step's title, or the summary's words.</summary>
    public string Text { get; }

    /// <summary>The step's letter ("b"); empty on a summary line.</summary>
    public string Letter { get; }

    public bool HasLetter => Letter.Length > 0;

    /// <summary>The place's number in This session (focused steps); 0 otherwise.</summary>
    public int Number { get; }

    public bool IsNumbered => Number > 0;

    public bool IsFocused { get; }

    /// <summary>Passed over: open, not in focus, before a step that is done or in focus.</summary>
    public bool IsSkipped { get; }

    /// <summary>One of the next open steps: greyed.</summary>
    public bool IsUpcoming { get; }

    public bool IsSummary => !HasLetter;

    /// <summary>Greyed: an upcoming step or a summary.</summary>
    public double LineOpacity => IsFocused || IsSkipped ? 1 : 0.55;

    /// <summary>Ticks the focused step (the next step takes its place, per the setting).</summary>
    public ICommand? ToggleCommand { get; }

    public bool CanToggle => ToggleCommand is not null;

    /// <summary>Brings the step into focus (the cap decides; full opens "This session is full").</summary>
    public ICommand? FocusCommand { get; }

    public bool CanFocus => FocusCommand is not null;

    /// <summary>What the line draws, to tell whether a row built again draws the same.</summary>
    public string Signature => string.Join("|", Text, Letter, Number.ToString(CultureInfo.InvariantCulture), IsFocused, IsSkipped, IsUpcoming);

    public static SessionStepLineViewModel Focused(string letter, string title, int number, ICommand toggle)
    {
        return new SessionStepLineViewModel(title, letter, number, true, false, false, toggle, null);
    }

    public static SessionStepLineViewModel Skipped(string letter, string title, ICommand focus)
    {
        return new SessionStepLineViewModel(title, letter, 0, false, true, false, null, focus);
    }

    public static SessionStepLineViewModel Upcoming(string letter, string title, ICommand focus)
    {
        return new SessionStepLineViewModel(title, letter, 0, false, false, true, null, focus);
    }

    public static SessionStepLineViewModel Summary(string text)
    {
        return new SessionStepLineViewModel(text, string.Empty, 0, false, false, false, null, null);
    }
}
