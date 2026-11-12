using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The Tasks keyboard map's window part (spec 03 §E): Ctrl+G go to a task by its ID (any page), and on Tasks and Today: Ctrl+Z undo
/// (the toast's, else the last 10 task actions), Del delete, F2 rename, H highlight, and Esc's last steps (selection, then the open
/// task). Del, F2 and H act on the selection, else the row with the keyboard, else the open task, by running the entry its menu shows
/// (the same rules as the menus: a running session's tasks included, nothing around the session's cap). Nothing fires while a text box
/// has the keyboard or a popover is open. The rest of the map lives where it acts: Ctrl+Shift+S, Ctrl+\, Ctrl+J, Ctrl+F and Ctrl+R in
/// the window, the rows' ↑/↓, Space, Enter and Alt+↑/↓ in the row, Ctrl+A in the section.
/// </summary>
public sealed class TaskShortcuts
{
    private readonly Action<TasksViewModel> _openGoTo;

    /// <param name="openGoTo">Opens Ctrl+G's prompt for the page.</param>
    public TaskShortcuts(Action<TasksViewModel> openGoTo)
    {
        _openGoTo = openGoTo;
    }

    /// <summary>Acts on <paramref name="e"/> when it is one of these keys and applies here; true when it did.</summary>
    public bool Handle(KeyEventArgs e, MainWindowViewModel window, IInputElement? focused)
    {
        if (window.Tasks is not { } tasks || InPopover(focused))
        {
            return false;
        }

        bool typing = focused is TextBox;
        if (e.Key == Key.G && e.KeyModifiers == KeyModifiers.Control)
        {
            _openGoTo(tasks);
            return true;
        }

        bool onTasks = window.IsTasksSelected;
        if (typing || !(onTasks || window.SelectedTab == MainWindowViewModel.TodayTab))
        {
            return false;
        }

        if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control)
        {
            _ = tasks.UndoLatestAsync();
            return true;
        }

        if (e.KeyModifiers != KeyModifiers.None)
        {
            return false;
        }

        return e.Key switch
        {
            Key.Escape => tasks.Escape(onTasks),
            Key.Delete => Run(tasks.ShortcutMenu(RowOf(focused), onTasks), "Del", header => header == "Delete" || header.StartsWith("Delete ", StringComparison.Ordinal)),
            Key.F2 => Run(tasks.ShortcutMenu(RowOf(focused), onTasks), "F2", header => header is "Rename" or "Rename one by one"),
            Key.H => Run(tasks.ShortcutMenu(RowOf(focused), onTasks), "H", header => header is "Highlight" or "Remove highlight"),
            _ => false,
        };
    }

    // The entry the menu labels with this key ("Del", "F2", "H"), or the older menus' entry of that name; run when it can.
    private static bool Run(IReadOnlyList<TaskMenuEntry> menu, string hint, Func<string, bool> isNamed)
    {
        TaskMenuEntry? entry = menu.FirstOrDefault(item => item.Hint == hint && item.Command is not null)
            ?? menu.FirstOrDefault(item => item.Command is not null && !item.IsHeading && isNamed(item.Header));
        if (entry?.Command is not { } command || !command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
    }

    // The task row holding the keyboard (its row button, or a button inside it after a click).
    private static TaskListRowViewModel? RowOf(IInputElement? focused)
    {
        for (Visual? visual = focused as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is StyledElement { DataContext: TaskListRowViewModel row })
            {
                return row;
            }
        }

        return null;
    }

    // A menu, popover or picker in the window's overlay (or a popup of its own) has the keyboard: its keys are its own.
    private static bool InPopover(IInputElement? focused)
    {
        return focused is Visual visual && (visual.FindAncestorOfType<OverlayLayer>() is not null || TopLevel.GetTopLevel(visual) is PopupRoot);
    }
}
