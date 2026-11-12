using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// A popover with its own content (Reminders &amp; due date, History): a <see cref="Flyout"/> that follows the shared popover rules
/// (<see cref="TaskPopover"/>) at a fixed width, inside the window and never taller than the window less 16 (it scrolls).
/// </summary>
public sealed class TaskPopoverFlyout : Flyout
{
    private readonly double _width;
    private FlyoutPresenter? _presenter;

    public TaskPopoverFlyout(double width)
    {
        _width = width;
        Popup.ShouldUseOverlayLayer = true;
        Opening += (_, _) =>
        {
            if (_presenter is not null)
            {
                _presenter.MaxHeight = TaskPopover.MaxHeightFor(Target);
            }
        };
    }

    protected override Control CreatePresenter()
    {
        Control presenter = base.CreatePresenter();
        if (presenter is FlyoutPresenter flyout)
        {
            _presenter = flyout;
            flyout.Width = _width;
            flyout.MinWidth = _width;
            flyout.MaxWidth = _width;
            flyout.Padding = new Thickness(0);
            TaskPopover.StylePresenter(flyout);
        }

        return presenter;
    }
}
