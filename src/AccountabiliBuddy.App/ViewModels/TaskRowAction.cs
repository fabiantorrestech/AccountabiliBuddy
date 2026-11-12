using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A quick action shown on a task row while the pointer is on it (spec 03 §B): ← "Queue it for today", the target "Put it in this
/// session", → "Move to the Shelf". Its command goes through the page's usual paths (the session's cap for This session).
/// </summary>
public sealed class TaskRowAction
{
    public TaskRowAction(MaterialIconKind icon, string tip, Func<Task> run)
    {
        Icon = icon;
        Tip = tip;
        Command = new AsyncRelayCommand(run);
    }

    public MaterialIconKind Icon { get; }

    public string Tip { get; }

    public ICommand Command { get; }

    /// <summary>Draws the same (its command acts on the same row, so a kept row may keep it).</summary>
    public static bool SameAs(TaskRowAction? one, TaskRowAction? other)
    {
        return one is null ? other is null : other is not null && one.Icon == other.Icon && one.Tip == other.Tip;
    }
}
