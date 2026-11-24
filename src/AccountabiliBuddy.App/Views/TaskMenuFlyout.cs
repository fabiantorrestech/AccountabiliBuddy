using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// A task menu (rows, selections, sections, the Shelf's View menu, page Options): a <see cref="MenuFlyout"/> that follows the shared
/// popover rules (<see cref="TaskPopover"/>): the theme's popover look, inside the window, never taller than the window less 16.
/// Keyboard (round 2, brief §3.6): the menu takes the keyboard when it opens (the first entry too when it was opened with the Menu
/// key or Shift+F10); ↑/↓ move, Enter picks, → opens a submenu as a side flyout, ← goes back, Esc closes (Avalonia's menu keys);
/// → also opens an entry that leads to a panel or page (<see cref="TaskMenuEntry.OpensMore"/>), the same as Enter.
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
                _presenter.MaxHeight = PanelLook ? RoomBelow(Target) : TaskPopover.MaxHeightFor(Target);
            }
        };
        Opened += (_, _) => Dispatcher.UIThread.Post(TakeKeyboard, DispatcherPriority.Input);
    }

    /// <summary>Answers the entries only the view can (a row's Reminders &amp; due date, Copy ID); also used by → on such an entry.</summary>
    public Action<TaskMenuEntry>? ViewAction { get; init; }

    /// <summary>The first entry takes the keyboard on opening (opened from the keyboard), not just the menu.</summary>
    public bool FocusFirstItem { get; init; }

    /// <summary>
    /// The Tasks redesign's panel look (board 09, the Tasks page Options): 290 wide, padding 8, Surface2 with a 1 px Line2 border, radius 14
    /// and the dialogs' shadow (0 18 50). Its rows' sizes are styled under <c>MenuFlyoutPresenter.abPanelMenu</c> (Themes/AbStyles.axaml).
    /// </summary>
    public bool PanelLook { get; init; }

    private const double PanelWidth = 290;

    // The panel opens 6 below its button and keeps 8 clear of the window's bottom edge; a shorter room than this still opens this tall.
    private const double PanelGap = 6;
    private const double PanelBottomMargin = 8;
    private const double PanelMinHeight = 160;

    /// <summary>
    /// The panel look stays under its button (4a.34): never taller than the room between the button and the window's bottom edge, so it
    /// scrolls rather than moving up over the header.
    /// </summary>
    private double RoomBelow(Control? anchor)
    {
        if (anchor is null || TopLevel.GetTopLevel(anchor) is not { } top || anchor.TranslatePoint(new Point(0, anchor.Bounds.Height), top) is not Point bottom)
        {
            return TaskPopover.MaxHeightFor(anchor);
        }

        return Math.Max(PanelMinHeight, top.ClientSize.Height - bottom.Y - PanelGap - PanelBottomMargin);
    }

    protected override Control CreatePresenter()
    {
        Control presenter = base.CreatePresenter();
        if (presenter is MenuFlyoutPresenter menu)
        {
            _presenter = menu;
            if (PanelLook)
            {
                StylePanel(menu);
            }
            else
            {
                TaskPopover.StylePresenter(menu);
            }

            menu.AddHandler(InputElement.KeyDownEvent, OnPresenterKeyDown, RoutingStrategies.Tunnel);
        }

        return presenter;
    }

    private static void StylePanel(MenuFlyoutPresenter menu)
    {
        menu.Classes.Add("abPanelMenu");
        menu.Bind(Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, menu.GetResourceObservable("Ab.Surface2"));
        menu.Bind(Avalonia.Controls.Primitives.TemplatedControl.BorderBrushProperty, menu.GetResourceObservable("Ab.Line2"));
        menu.BorderThickness = new Thickness(1);
        menu.CornerRadius = new CornerRadius(14);
        menu.Padding = new Thickness(8);
        menu.Width = PanelWidth;
        menu.MinWidth = PanelWidth;
        menu.MaxWidth = PanelWidth;
        // Its rows take the panel's width (notes wrap) rather than scrolling sideways past it.
        ScrollViewer.SetHorizontalScrollBarVisibility(menu, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        menu.TemplateApplied += (_, e) =>
        {
            // The shadow is the outer border's own (no template binding carries it).
            Avalonia.Controls.Border? root = e.NameScope.Find<Avalonia.Controls.Border>("LayoutRoot")
                ?? menu.GetVisualChildren().OfType<Avalonia.Controls.Border>().FirstOrDefault();
            if (root is not null)
            {
                root.Bind(Avalonia.Controls.Border.BoxShadowProperty, root.GetResourceObservable("Ab.DialogShadow"));
                root.Padding = new Thickness(8);
            }

            foreach (ScrollViewer scroller in menu.GetVisualDescendants().OfType<ScrollViewer>())
            {
                scroller.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
            }
        };
    }

    // The keyboard goes into the menu, so ↑/↓ work at once: onto the first entry when opened from the keyboard, else onto the menu
    // itself (↓ then picks the first entry). Nothing changes if something inside already has it.
    private void TakeKeyboard()
    {
        if (_presenter is null || !IsOpen)
        {
            return;
        }

        if (TopLevel.GetTopLevel(_presenter)?.FocusManager?.GetFocusedElement() is Visual focused && _presenter.IsVisualAncestorOf(focused))
        {
            return;
        }

        MenuItem? first = _presenter.GetVisualDescendants().OfType<MenuItem>().FirstOrDefault(item => item.IsEnabled && item.IsEffectivelyVisible);
        if (FocusFirstItem && first is not null)
        {
            first.Focus(NavigationMethod.Directional);
            return;
        }

        _presenter.Focusable = true;
        if (!_presenter.Focus(NavigationMethod.Directional) && first is not null)
        {
            first.Focus(NavigationMethod.Directional);
        }
    }

    // → on an entry that opens a panel or a page: the same as Enter (a submenu's → stays Avalonia's own).
    private void OnPresenterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Right || e.KeyModifiers != KeyModifiers.None || e.Source is not Visual source)
        {
            return;
        }

        MenuItem? item = source as MenuItem ?? source.FindAncestorOfType<MenuItem>();
        if (item?.Tag is not TaskMenuEntry { OpensMore: true, Children: null } entry || !item.IsEnabled)
        {
            return;
        }

        e.Handled = true;
        Hide();
        if (entry.Command is { } command)
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }
        }
        else if (entry.ViewAction is not null)
        {
            ViewAction?.Invoke(entry);
        }
    }
}
