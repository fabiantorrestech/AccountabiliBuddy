using System.ComponentModel;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The title bar's session control. The running pill opens its extras (Step away, the end time, the session menu with End) while
/// the pointer is on it, while the keyboard focus is in it (Tab, not a click), while its menu is open, and always while away
/// (I'm back). Motion "Off": it opens at once and the ended target doesn't fade.
/// </summary>
public partial class SessionControlView : UserControl, AccountabiliBuddy.App.Controls.ITitleBarLeftReserve
{
    private const string OpenClass = "open";
    private const string FadeClass = "fade";

    private Transitions? _extrasTransitions;
    private SessionControlViewModel? _viewModel;
    private bool _pointerOver;
    private bool _keyboardFocus;
    private bool _menuOpen;

    public SessionControlView()
    {
        InitializeComponent();
        Pill.PointerEntered += (_, _) =>
        {
            _pointerOver = true;
            UpdateOpen();
        };
        Pill.PointerExited += (_, _) =>
        {
            _pointerOver = false;
            UpdateOpen();
        };
        Pill.AddHandler(GotFocusEvent, OnPillGotFocus);
        Pill.AddHandler(LostFocusEvent, OnPillLostFocus);
        _menu.Opened += (_, _) =>
        {
            _menuOpen = true;
            UpdateOpen();
        };
        _menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            UpdateOpen();
        };
    }

    // Round 2 (brief §3.6, §4.2): the Session menu in the shared menu look, one flyout refilled each time it opens (so the title bar's
    // one-pop-up rule can hold it): "Session · ends 1:23 PM", Step away, Pause timer, Side task, Add 10 minutes │ End and mark the rest
    // unfinished │ End session (red, last).
    private readonly TaskMenuFlyout _menu = new() { Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight };

    /// <summary>The ⋯ session menu, for the title bar's one-pop-up-at-a-time rule.</summary>
    public Avalonia.Controls.Primitives.FlyoutBase? SessionMenu => _menu;

    // The ⋯ after the time: the menu, filled now so it matches the session as it is.
    private void OnMoreClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        TaskMenus.Fill(_menu, _viewModel.MenuEntries());
        _menu.ShowAt(MoreButton);
        e.Handled = true;
    }

    // Round 2 (brief §3.2, CN §1 rules 2 and 3; R1 review): the extras (Step away, the end time) open to the left of the ring. Their
    // full open width is reserved inside this control's own bounds while the pill shows, so opening them moves nothing, they never
    // cover the noise control, and they are hit-tested as this control's buttons (never as the title bar's drag area). The title bar
    // centres the resting part (ring, time, ⋯) and keeps the reserve clear of the left group (TitleBarLayout, LeftReserve).
    private double _openExtrasWidth;

    /// <summary>The width kept free left of the ring for the extras when open; 0 while idle or ended.</summary>
    public double LeftReserve => Pill.IsVisible ? _openExtrasWidth : 0;

    protected override Size MeasureOverride(Size availableSize)
    {
        // The open width is the extras' content at its natural size (capped like the open style's MaxWidth), not the animated
        // MaxWidth; the content is measured unconstrained first, and the normal pass below measures it again under the MaxWidth.
        if (Pill.IsVisible && Extras.Child is Layoutable content)
        {
            content.Measure(Size.Infinity);
            _openExtrasWidth = Math.Min(content.DesiredSize.Width + Extras.Padding.Left + Extras.Padding.Right, ExtrasOpenMaxWidth);
        }
        else
        {
            _openExtrasWidth = 0;
        }

        Size full = base.MeasureOverride(availableSize);
        double shownExtras = Pill.IsVisible ? Extras.DesiredSize.Width : 0;
        return new Size(full.Width - shownExtras + LeftReserve, full.Height);
    }

    // The open style's MaxWidth on Border#Extras (SessionControlView.axaml).
    private const double ExtrasOpenMaxWidth = 360;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as SessionControlViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateOpen();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyMotion();
        Services.Motion.SpeedChanged += OnMotionChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Services.Motion.SpeedChanged -= OnMotionChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnMotionChanged(object? sender, EventArgs e)
    {
        ApplyMotion();
    }

    private void ApplyMotion()
    {
        _extrasTransitions = Services.Motion.Gate(Extras, _extrasTransitions);
        EndedTarget.Classes.Set(FadeClass, Services.Motion.Current is not { IsOn: false });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionControlViewModel.ForcesOpen) or nameof(SessionControlViewModel.IsRunningOrAway))
        {
            UpdateOpen();
        }
    }

    // Only keyboard focus opens it: a click leaves the focus on the button, which must not keep End in view after the pointer leaves.
    private void OnPillGotFocus(object? sender, FocusChangedEventArgs e)
    {
        _keyboardFocus = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
        UpdateOpen();
    }

    private void OnPillLostFocus(object? sender, FocusChangedEventArgs e)
    {
        // Focus moving between the pill's own buttons raises lost then got; read where it ended up once both have run.
        Dispatcher.UIThread.Post(() =>
        {
            if (!Pill.IsKeyboardFocusWithin)
            {
                _keyboardFocus = false;
            }

            UpdateOpen();
        });
    }

    private void UpdateOpen()
    {
        bool open = _viewModel is { IsRunningOrAway: true }
            && (_viewModel.ForcesOpen || _pointerOver || _menuOpen || (_keyboardFocus && Pill.IsKeyboardFocusWithin));
        Pill.Classes.Set(OpenClass, open);
    }
}
