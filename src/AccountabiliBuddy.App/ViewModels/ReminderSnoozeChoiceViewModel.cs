using System.Windows.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One moment of a reminder card's Snooze menu: "10 min", or a shortcut ("Tomorrow" with "9:00 AM" at the right). Off when its moment has passed.</summary>
public sealed class ReminderSnoozeChoiceViewModel
{
    public ReminderSnoozeChoiceViewModel(string text, string hint, bool isEnabled, ICommand command)
    {
        Text = text;
        Hint = hint;
        IsEnabled = isEnabled;
        Command = command;
    }

    public string Text { get; }

    /// <summary>Muted text at the right; empty for none.</summary>
    public string Hint { get; }

    public bool IsEnabled { get; }

    public ICommand Command { get; }
}
