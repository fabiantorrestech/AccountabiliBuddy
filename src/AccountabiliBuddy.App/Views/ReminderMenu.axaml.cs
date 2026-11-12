using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Reminders &amp; due date menu's content. <see cref="ShowAt"/> opens it in a 290-wide popover that follows the shared rules
/// (<see cref="TaskPopoverFlyout"/>), from the open task's reminders line or a row's menu.
/// </summary>
public partial class ReminderMenu : UserControl
{
    /// <summary>The menu's width (spec 03 §C).</summary>
    public const double MenuWidth = 290;

    public ReminderMenu()
    {
        InitializeComponent();
    }

    /// <summary>Opens the menu of <paramref name="menu"/>'s task at <paramref name="anchor"/>; it closes on Esc, a click outside, or Ring it now.</summary>
    public static void ShowAt(Control anchor, ReminderMenuViewModel menu, PlacementMode placement)
    {
        var flyout = new TaskPopoverFlyout(MenuWidth) { Placement = placement, Content = new ReminderMenu { DataContext = menu } };
        EventHandler close = (_, _) => flyout.Hide();
        menu.CloseRequested += close;
        flyout.Closed += (_, _) => menu.CloseRequested -= close;
        flyout.ShowAt(anchor);
    }

    // Enter in the time box sets the picked day and time.
    private void OnTimeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ReminderMenuViewModel { CanApplyPick: true } menu)
        {
            menu.ApplyPickCommand.Execute(null);
            e.Handled = true;
        }
    }
}
