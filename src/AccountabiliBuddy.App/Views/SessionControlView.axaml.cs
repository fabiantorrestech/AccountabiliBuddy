using System.ComponentModel;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The title bar's session control. The running pill opens its extras (Step away, the end time, the session menu with End) while
/// the pointer is on it, while the keyboard focus is in it (Tab, not a click), while its menu is open, and always while away
/// (I'm back). Motion "Off": it opens at once and the ended target doesn't fade.
/// </summary>
public partial class SessionControlView : UserControl
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

    // Owner 2026-11-27 ("cemented"): the control is a fixed slot of the title bar (TitleBarLayout gives it a width that depends on the
    // window's width only) and always takes that whole width, so Start session, the running pill, Ended or another device's session
    // never moves the noise control, the menu or the back / forward arrows; the part of the slot a state doesn't use is left empty
    // (and drags the window). Its content is right-aligned. The running pill's extras (Step away, the end time) open to the left of the
    // ring into the slot's free room, so opening them moves nothing and they are hit-tested as this control's buttons. In a slot too
    // narrow for them, the end time is left out, then Step away too: each shows whole or not at all, never trimmed (owner 2026-11-27;
    // the ⋯ menu has both). In a slot too narrow even for the ring, the time and the ⋯, the muted words after the time trim.

    // The open style's MaxWidth on Border#Extras (SessionControlView.axaml).
    private const double ExtrasOpenMaxWidth = 360;

    protected override Size MeasureOverride(Size availableSize)
    {
        bool bounded = !double.IsInfinity(availableSize.Width);
        // The Elsewhere pill never asks for more than it is given: its device name trims instead (the time and the dot stay).
        ElsewherePill.MaxWidth = ElsewherePill.IsVisible && bounded ? Math.Max(0, availableSize.Width) : double.PositiveInfinity;
        Size full = base.MeasureOverride(new Size(double.PositiveInfinity, availableSize.Height));

        // The extras' natural width is read from their row, which measures its children unconstrained whatever width it is given
        // (closed, the Border's animated MaxWidth is 0); the part always shown is what's left once the extras' current width is out.
        double shownExtras = Pill.IsVisible ? Extras.DesiredSize.Width : 0;
        double resting = full.Width - shownExtras;
        double naturalExtras = Pill.IsVisible ? Math.Min(ExtrasRow.NaturalWidth, ExtrasOpenMaxWidth) : 0;
        double room = bounded ? availableSize.Width : Math.Max(full.Width, resting + naturalExtras);
        double free = room - resting;
        double stepAway = Math.Min(StepAwayPill.DesiredSize.Width, naturalExtras);
        double openExtras = !Pill.IsVisible ? 0
            : free >= naturalExtras - 0.5 ? naturalExtras
            : free >= stepAway ? stepAway
            : 0;

        // Cap the extras' row at what fits: the end time drops out (HideBelow), or the row closes to nothing; never a trimmed "ends …".
        double cap = Pill.IsVisible && openExtras < naturalExtras ? openExtras : double.PositiveInfinity;
        if (!SameWidth(ExtrasRow.MaxWidth, cap))
        {
            ExtrasRow.MaxWidth = cap;
        }

        Size given = base.MeasureOverride(new Size(room, availableSize.Height));
        return new Size(room, given.Height);
    }

    private static bool SameWidth(double a, double b) =>
        (double.IsPositiveInfinity(a) && double.IsPositiveInfinity(b)) || Math.Abs(a - b) < 0.5;

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
