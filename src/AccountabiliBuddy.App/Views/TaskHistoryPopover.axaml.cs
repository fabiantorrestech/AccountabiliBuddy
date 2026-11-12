using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace AccountabiliBuddy.App.Views;

/// <summary>The History popover's content. <see cref="ShowAt"/> opens it in a popover that follows the shared rules (<see cref="TaskPopoverFlyout"/>).</summary>
public partial class TaskHistoryPopover : UserControl
{
    /// <summary>The popover's width.</summary>
    public const double PopoverWidth = 340;

    public TaskHistoryPopover()
    {
        InitializeComponent();
    }

    public static void ShowAt(Control anchor, TaskHistoryViewModel history, PlacementMode placement)
    {
        var flyout = new TaskPopoverFlyout(PopoverWidth) { Placement = placement, Content = new TaskHistoryPopover { DataContext = history } };
        flyout.ShowAt(anchor);
    }
}
