using System.Windows.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A choice in the Reminders &amp; due date menu: a reminder to add ("10 min before") or a "Done by" day ("Tomorrow"), with its icon
/// and a ✓ on the one the task has.
/// </summary>
public sealed record ReminderMenuChoice(string Text, MaterialIconKind Icon, ICommand Command, bool IsChecked = false, bool IsEnabled = true);
