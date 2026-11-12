using System.Windows.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One of the picker's three shortcut chips ("Tonight 6:00 PM"); off when its moment has already passed today.</summary>
public sealed record SnoozeShortcutViewModel(string Text, bool IsEnabled, ICommand Pick)
{
    public string Tip => IsEnabled ? "Snooze until " + Text : "Already passed today";
}
