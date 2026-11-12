using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// A task menu (rows, selections, sections, the Shelf's View menu): a <see cref="MenuFlyout"/> that follows the shared popover rules
/// (<see cref="TaskPopover"/>): the theme's popover look, inside the window, never taller than the window less 16.
/// </summary>
public sealed class TaskMenuFlyout : MenuFlyout
{
    private MenuFlyoutPresenter? _presenter;

    public TaskMenuFlyout()
    {
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
        if (presenter is MenuFlyoutPresenter menu)
        {
            _presenter = menu;
            TaskPopover.StylePresenter(menu);
        }

        return presenter;
    }
}
