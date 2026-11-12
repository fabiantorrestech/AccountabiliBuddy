using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The History popover's content. <see cref="ShowAt"/> opens it in a popover that follows the shared rules (<see cref="TaskPopoverFlyout"/>):
/// <see cref="PopoverWidth"/> wide, inside the window, never taller than the window less 16, scrolling up and down only and opening at
/// its top-left. Each row's ⋯ opens its menu (the shared menu look) beside it; the popover stays open.
/// </summary>
public partial class TaskHistoryPopover : UserControl
{
    /// <summary>The popover's width: it fits the smallest window (901 wide) with room at both sides.</summary>
    public const double PopoverWidth = 460;

    public TaskHistoryPopover()
    {
        InitializeComponent();
    }

    public static void ShowAt(Control anchor, TaskHistoryViewModel history, PlacementMode placement)
    {
        var content = new TaskHistoryPopover { DataContext = history };
        var flyout = new TaskPopoverFlyout(PopoverWidth) { Placement = placement, Content = content };
        // It opens at the top-left whatever takes the keyboard first (a focused control asks to be brought into view).
        flyout.Opened += (_, _) => Dispatcher.UIThread.Post(content.ScrollToStart, DispatcherPriority.Loaded);
        flyout.ShowAt(anchor);
    }

    private void ScrollToStart()
    {
        if (this.FindAncestorOfType<ScrollViewer>() is { } scroll)
        {
            scroll.Offset = default(Vector);
        }
    }

    // ⋯: the row's actions (Do again, or Bring back) in the shared menu, under the button and aligned to its right edge.
    private void OnRowMoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control button && button.DataContext is TaskHistoryRowViewModel row)
        {
            TaskMenus.ShowEntries(button, row.Menu(), PlacementMode.BottomEdgeAlignedRight);
            e.Handled = true;
        }
    }
}
