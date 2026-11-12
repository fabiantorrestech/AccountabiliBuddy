using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace AccountabiliBuddy.App.Views;

/// <summary>A running task's live step list (P3). <see cref="ShowAt"/> opens it in a popover that follows the shared rules (<see cref="TaskPopoverFlyout"/>).</summary>
public partial class StepListPopover : UserControl
{
    /// <summary>The popover's width.</summary>
    public const double PopoverWidth = 340;

    public StepListPopover()
    {
        InitializeComponent();
    }

    public static void ShowAt(Control anchor, StepListViewModel steps)
    {
        var flyout = new TaskPopoverFlyout(PopoverWidth) { Placement = PlacementMode.BottomEdgeAlignedLeft, Content = new StepListPopover { DataContext = steps } };
        flyout.ShowAt(anchor);
    }
}
