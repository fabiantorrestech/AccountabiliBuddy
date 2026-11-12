using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// Ctrl+G's prompt (<see cref="GoToTaskViewModel"/>). <see cref="ShowAt"/> opens it in a popover that follows the shared rules
/// (<see cref="TaskPopoverFlyout"/>) under the title bar's search; the box takes the keyboard at once.
/// </summary>
public partial class GoToTaskPopover : UserControl
{
    private const double PopoverWidth = 280;

    public GoToTaskPopover()
    {
        InitializeComponent();
    }

    /// <summary>Opens the prompt under <paramref name="anchor"/>; it closes once the task opened, or on Esc or a click away.</summary>
    public static void ShowAt(Control anchor, Func<string, string?> go)
    {
        var model = new GoToTaskViewModel(go);
        var flyout = new TaskPopoverFlyout(PopoverWidth)
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = new GoToTaskPopover { DataContext = model },
        };
        model.Done += (_, _) => flyout.Hide();
        flyout.ShowAt(anchor);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => Box.Focus(), DispatcherPriority.Loaded);
    }
}
