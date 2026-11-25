using AccountabiliBuddy.App.Controls;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The grouped label picker (<see cref="LabelPicker"/>, ADR 93) in a popover under a control, for the places outside the Tasks page that pick
/// one label (Tasks redesign item 10: the session checklist, Today's session rows, Goals' "tasks with label"). A pick ("No label" or a label)
/// applies and closes it; a second click on the picked label only takes the pick back. The keyboard starts in its search.
/// </summary>
public static class LabelPickerPopover
{
    // The same size as the task editor's label pill picker: the rail and the labels, each scrolling on its own.
    private const double PickerWidth = 440;
    private const double PickerHeight = 320;

    /// <param name="anchor">What it opens under.</param>
    /// <param name="picker">The picker (its labels, categories and the tags "Current" / "N have it").</param>
    /// <param name="apply">Called once with the pick, as the popover closes.</param>
    /// <param name="showsNoLabel">Whether "No label" is offered (Goals count a label, so they leave it out).</param>
    public static void Show(Control anchor, LabelPickerViewModel picker, Action<LabelPick> apply, bool showsNoLabel = true,
        PlacementMode placement = PlacementMode.BottomEdgeAlignedLeft)
    {
        var view = new LabelPicker { DataContext = picker, Height = PickerHeight, ShowsNoLabel = showsNoLabel };
        var flyout = new TaskPopoverFlyout(PickerWidth)
        {
            Placement = placement,
            Content = new Border { Padding = new Thickness(12), Child = view },
        };
        picker.PickChanged += (_, _) =>
        {
            if (!picker.Pick.IsNothing)
            {
                LabelPick pick = picker.Pick;
                flyout.Hide();
                apply(pick);
            }
        };
        flyout.Opened += (_, _) => Dispatcher.UIThread.Post(view.FocusSearch, DispatcherPriority.Input);
        flyout.ShowAt(anchor);
    }
}
