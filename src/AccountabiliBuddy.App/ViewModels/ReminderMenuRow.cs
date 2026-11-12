using System.Windows.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One of a task's reminders in the Reminders &amp; due date menu: "10 min before", when it rings, and × to remove it.</summary>
/// <param name="Text">The rule: "At start time", "10 min before", "Thu 11/12 9:00 AM".</param>
/// <param name="Sub">"Rings Fri 11/13 4:50 PM", or "Needs a Done by date" for a rule that can't ring yet.</param>
public sealed record ReminderMenuRow(string Text, string Sub, ICommand RemoveCommand)
{
    public bool HasSub => Sub.Length > 0;
}
