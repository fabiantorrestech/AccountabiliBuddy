using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The on-screen window with the session timer and the all-day timeline. Clicks pass through it to the app underneath; while Ctrl+Alt is held it
/// takes the mouse, so it can be dragged, and where it is dropped is reported through <see cref="PositionChosen"/>.
/// With the mouse over it (and Ctrl+Alt not held) it fades almost away. By default it sits in a chosen corner of the
/// main screen (<see cref="SetCorner"/>): in a bottom corner it lifts above the taskbar while that is showing (also an
/// auto-hiding one), and while a maximized app on that screen is in front it slides in from the corner so the app's
/// buttons stay clickable, then back after. Dropped (or saved) within a short distance of the corner, it snaps back
/// into it and counts as the default spot. Any other drag (Ctrl+Alt) is only for now: the panel then offers Keep (for
/// the maximized app in front: its own distances from the corner; otherwise a saved position) and ↺. A temporary spot
/// made over a maximized app ends when that app is no longer maximized in front; any other lasts until ↺ or a restart.
/// The window is anchored by a 3×3 zone of its monitor's work area (<see cref="OverlayAnchorZone"/>): a corner for the
/// corner cells, the middle of an edge for the edge cells, the centre for the middle cell. The Ctrl+Alt details open
/// from the bar's corner (turned around when they would run off the screen), so the bar never moves, and every spot is
/// measured from the bar.
/// </summary>
public partial class SessionOverlayWindow : Window
{
    private static readonly TimeSpan KeyPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MoveSettleDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan SlideStep = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan HintDelay = TimeSpan.FromMilliseconds(400);
    private const int EdgeMarginPixels = 16;

    // A drop this close to the default corner (display pixels) snaps back into it.
    private const double SnapDips = 48;

    // Size assumed before the window has been laid out (display pixels).
    private const double TypicalWidthDips = 230;
    private const double TypicalHeightDips = 60;

    private readonly IOverlayWindowControl? _control;
    private readonly DispatcherTimer _keyPoll;
    private readonly DispatcherTimer _moveSettled;
    private readonly DispatcherTimer _slide;
    private readonly DispatcherTimer _hintDelay;
    private bool _dragKeysHeld;
    private bool _movedByUser;

    // Ctrl+Alt + double-click opens the app. The drag waits until the mouse moves, since starting it on the press
    // swallows the second click.
    private const double DragStartDistance = 4;
    private DoubleClickDetector? _doubleClick;
    private PointerPressedEventArgs? _pressToDrag;
    private Point _pressPoint;
    private OverlayCorner _corner = OverlayCorner.BottomRight;

    // True while no saved position is in use, so the window keeps to its corner as its size changes.
    private bool _inDefaultCorner = true;

    // How far in from the corner it sits now (physical pixels: vertically, sideways, and lifted above a showing
    // taskbar), and where it is sliding to.
    private int _inV;
    private int _targetInV;
    private int _inH;
    private int _targetInH;
    private int _lift;
    private int _targetLift;

    // The maximized app it is moved for (on its screen, in the default corner); null when none.
    private string? _avoidedApp;

    // A drag that hasn't been kept: where it sits for now, and the maximized app it was dragged over (null = none).
    private bool _isTemporary;
    private string? _temporaryForApp;

    // The saved position it came from (null = the corner), to go back to on ↺.
    private PixelPoint? _savedPosition;

    // Where the folded window (just the bar, no Ctrl+Alt details) sits: a 3x3 zone of its monitor's work area and the
    // zone's anchor point on the bar (physical pixels). Every placement, drag, distance and saved position is measured
    // from these, and a change of the bar's size grows it away from the anchor point.
    private OverlayAnchorZone _zone = new(2, 2);
    private PixelPoint _anchorPoint;
    private bool _expanded;

    // While the Ctrl+Alt details are open the window grows away from its own anchor point, in the zone the bar was in
    // when they opened (the layout of the details follows it). A drag moves that point with the window.
    private OverlayAnchorZone _layoutZone = new(2, 2);
    private PixelPoint _layoutPoint;

    // True while Windows runs its move loop for a drag (the mouse button is still down).
    private bool _dragging;

    // A saved spot (top-left of the folded bar) waiting for the first layout, so its zone is worked out from the real size.
    private PixelPoint? _pendingOrigin;
    private double _foldedWidthDips;
    private double _foldedHeightDips;

    // Our own hints (OverlayHint.Text), above this window: Windows tooltips end up behind it, as it's put on top every
    // tick. The control under the pointer that has one, where the pointer is (physical pixels), and whether a press put
    // the hint away until the pointer moves to another control.
    private OverlayHintWindow? _hint;
    private Control? _hintSource;
    private PixelPoint _hintCursor;
    private bool _hintPressed;

    // Used by the XAML previewer.
    public SessionOverlayWindow()
        : this(null)
    {
    }

    public SessionOverlayWindow(IOverlayWindowControl? control)
    {
        _control = control;
        InitializeComponent();

        _keyPoll = new DispatcherTimer { Interval = KeyPollInterval };
        _keyPoll.Tick += (_, _) =>
        {
            PollDragKeys();
            UpdateTaskbarLift();
        };
        _slide = new DispatcherTimer { Interval = SlideStep };
        _slide.Tick += (_, _) => SlideTowardTarget();
        _hintDelay = new DispatcherTimer { Interval = HintDelay };
        _hintDelay.Tick += (_, _) =>
        {
            _hintDelay.Stop();
            ShowHint();
        };
        _moveSettled = new DispatcherTimer { Interval = MoveSettleDelay };
        _moveSettled.Tick += (_, _) =>
        {
            if (_dragging)
            {
                // The mouse is still down (it only paused): the drop is not here yet.
                return;
            }

            FinishDrag();
        };

        Opened += (_, _) =>
        {
            SetClickThrough(true);
            _keyPoll.Start();
        };
        Closed += (_, _) =>
        {
            _slide.Stop();
            _keyPoll.Stop();
            _moveSettled.Stop();
            _hintDelay.Stop();
            _hint?.Close();
            _hint = null;
        };
        SizeChanged += (_, _) =>
        {
            if (!_expanded)
            {
                bool firstLayout = _foldedWidthDips <= 0;
                _foldedWidthDips = Bounds.Width;
                _foldedHeightDips = Bounds.Height;
                if (firstLayout && _pendingOrigin is PixelPoint origin)
                {
                    // A saved spot: its zone is worked out now that the bar's real size is known.
                    _pendingOrigin = null;
                    StoreFromOrigin(origin);
                }
            }

            if (_movedByUser)
            {
                // Mid-drag, or just dropped and not settled yet: the window sits where the mouse put it.
                return;
            }

            FitLayout();
            if (_inDefaultCorner && !_isTemporary)
            {
                MoveToDefaultCorner();
            }
            else
            {
                ApplyAnchor();
            }
        };
        PositionChanged += (_, _) =>
        {
            if (_movedByUser)
            {
                _moveSettled.Stop();
                _moveSettled.Start();
            }
        };
        ScalingChanged += (_, _) =>
        {
            // The pixel size of the window and of the folded bar follow the monitor's scaling, which a right or bottom
            // anchor depends on. (Mid-drag the drop settles it with the new scaling.)
            if (_movedByUser)
            {
                return;
            }

            if (_expanded)
            {
                SyncLayoutToBar();
            }

            ApplyAnchor();
        };
    }

    /// <summary>
    /// Raised with the new top-left corner (pixels) after the user drags the window somewhere, or null when it was
    /// dropped near the default corner and snapped back into it.
    /// </summary>
    public event EventHandler<PixelPoint?>? PositionChosen;

    /// <summary>Raised with an app and its new distances in from the corner (vertical, sideways; display pixels) after
    /// dragging the window while that app is maximized.</summary>
    public event EventHandler<(string App, double InVDips, double InHDips)>? AppOffsetChosen;

    /// <summary>Raised when a temporary spot starts or ends (to show or hide Keep and ↺).</summary>
    public event EventHandler? TemporaryChanged;

    /// <summary>True in the default corner (not a position the user dragged it to and kept).</summary>
    public bool IsInDefaultCorner => _inDefaultCorner && !_isTemporary;

    /// <summary>
    /// True when something outside hid it: minimized (Win+D, Win+M) or on no screen at all (a monitor unplugged or a
    /// tool moved it). The service then makes a new one in its place.
    /// </summary>
    public bool IsLost
    {
        get
        {
            if (WindowState == WindowState.Minimized)
            {
                return true;
            }

            if (_movedByUser)
            {
                return false; // Being dragged; it may pass between screens.
            }

            var box = new PixelRect(Position, PixelSize.FromSize(Bounds.Size, DesktopScaling));
            return !Screens.All.Any(screen => screen.Bounds.Intersects(box));
        }
    }

    /// <summary>True while it sits where it was dragged but not kept.</summary>
    public bool IsTemporary => _isTemporary;

    /// <summary>The maximized app a temporary spot was made over (Keep then saves it for that app), or null.</summary>
    public string? TemporaryForApp => _temporaryForApp;

    /// <summary>Keeps the temporary spot: as the app's own distances when made over a maximized app, otherwise as the saved position.</summary>
    public void KeepTemporary()
    {
        if (!_isTemporary)
        {
            return;
        }

        _isTemporary = false;
        if (_temporaryForApp is string app && TryAppOffset(out double inVDips, out double inHDips))
        {
            _temporaryForApp = null;
            AppOffsetChosen?.Invoke(this, (app, inVDips, inHDips));
        }
        else
        {
            _temporaryForApp = null;
            _inDefaultCorner = false;
            _savedPosition = FoldedOrigin();
            PositionChosen?.Invoke(this, _savedPosition);
        }

        TemporaryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ends a temporary spot: back to the saved position, or the corner (and any app's distances).</summary>
    public void UndoTemporary()
    {
        if (!_isTemporary)
        {
            return;
        }

        _isTemporary = false;
        _temporaryForApp = null;
        if (_savedPosition is PixelPoint saved && !_inDefaultCorner)
        {
            StoreFromOrigin(saved);
            if (_expanded)
            {
                SyncLayoutToBar();
            }

            ApplyAnchor();
        }
        else
        {
            _inDefaultCorner = true;
            _slide.Start();
            MoveToDefaultCorner();
        }

        TemporaryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The maximized app it is moved for, or null.</summary>
    public string? AvoidedApp => _avoidedApp;

    private bool IsTop => _corner is OverlayCorner.TopRight or OverlayCorner.TopLeft;

    private bool IsRight => _corner is OverlayCorner.TopRight or OverlayCorner.BottomRight;

    /// <summary>The default corner; moves there now if it is in its default spot.</summary>
    public void SetCorner(OverlayCorner corner)
    {
        if (corner == _corner)
        {
            return;
        }

        _corner = corner;
        _inV = _targetInV = _inH = _targetInH = 0;
        UpdateTaskbarLift();
        _lift = _targetLift;
        if (_inDefaultCorner && !_movedByUser)
        {
            MoveToDefaultCorner();
        }
    }

    /// <summary>Puts the window at a saved spot if it is still on a screen, otherwise in its corner.</summary>
    public void PlaceAt(int? x, int? y)
    {
        _isTemporary = false;
        _temporaryForApp = null;
        if (x is int left && y is int top
            && Screens.All.Any(screen => screen.WorkingArea.Contains(new PixelPoint(left + 1, top + 1)))
            && !IsNearDefaultCorner(new PixelRect(new PixelPoint(left, top), SizeOn(ScreenAtOrigin(new PixelPoint(left, top))!))))
        {
            _inDefaultCorner = false;
            _savedPosition = new PixelPoint(left, top);
            _pendingOrigin = _foldedWidthDips <= 0 ? _savedPosition : null;
            StoreFromOrigin(new PixelPoint(left, top));
            if (_expanded)
            {
                SyncLayoutToBar();
            }

            ApplyAnchor();
            return;
        }

        _inDefaultCorner = true;
        _savedPosition = null;
        UpdateTaskbarLift();
        _lift = _targetLift;
        MoveToDefaultCorner();
    }

    /// <summary>
    /// Slides in from the corner (<paramref name="inVDips"/> vertically, <paramref name="inHDips"/> sideways) while a
    /// maximized app fills the screen the corner is on (<paramref name="maximizedScreen"/>), and back when there's
    /// none. Only in the default corner: a spot the user dragged it to is never changed.
    /// </summary>
    /// <param name="app">The maximized app's process name, for <see cref="AppOffsetChosen"/>.</param>
    public void AvoidMaximizedApp(ScreenBox? maximizedScreen, double inVDips, double inHDips = 0, string? app = null)
    {
        Screen? screen = DefaultScreen();
        bool onThatScreen = screen is not null && maximizedScreen is ScreenBox box
            && box.Left == screen.Bounds.X && box.Top == screen.Bounds.Y
            && box.Right == screen.Bounds.Right && box.Bottom == screen.Bounds.Bottom;
        _avoidedApp = _inDefaultCorner && onThatScreen ? app : null;
        if (_movedByUser)
        {
            return; // Being dragged; don't fight the mouse.
        }

        if (_isTemporary)
        {
            // A spot made for now over a maximized app lasts while that app stays maximized in front.
            if (_temporaryForApp is not null && _temporaryForApp != _avoidedApp)
            {
                UndoTemporary();
            }
            else
            {
                return;
            }
        }

        bool avoid = _inDefaultCorner && onThatScreen;
        int targetV = avoid ? (int)Math.Round(inVDips * screen!.Scaling) : 0;
        int targetH = avoid ? (int)Math.Round(inHDips * screen!.Scaling) : 0;
        if (targetV == _targetInV && targetH == _targetInH)
        {
            return;
        }

        // Switching apps: straight there, no slide, so it never sits over the new app's buttons for a moment.
        _inV = _targetInV = targetV;
        _inH = _targetInH = targetH;
        MoveToDefaultCorner();
    }

    private void OnKeepClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        KeepTemporary();
    }

    private void OnUndoMoveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        UndoTemporary();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // A size change while Windows is moving the window (a Ctrl+Alt drag) counts as the user resizing it, which turns
        // off sizing to content and leaves it stuck at the open details' size. It can't be resized by hand, so keep it.
        if (change.Property == SizeToContentProperty && SizeToContent != SizeToContent.WidthAndHeight)
        {
            Dispatcher.UIThread.Post(() => SizeToContent = SizeToContent.WidthAndHeight);
        }

        if (change.Property == IsVisibleProperty && !IsVisible)
        {
            HideHint();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        HideHint();
        _hintPressed = _hintSource is not null;
        // A press on the noise volume slider (or another control) works that control instead of moving the window.
        bool onControl = e.Source is Avalonia.Visual visual
                         && (Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Slider>(visual, includeSelf: true) is not null
                             || Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Button>(visual, includeSelf: true) is not null);
        if (!_dragKeysHeld || onControl || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Point position = e.GetPosition(this);
        _doubleClick ??= new DoubleClickDetector(_control?.DoubleClickTime() ?? TimeSpan.FromMilliseconds(500), DragStartDistance);
        if (_doubleClick.Press(TimeSpan.FromMilliseconds(e.Timestamp), position))
        {
            _pressToDrag = null;
            (DataContext as OnScreenOverlayViewModel)?.OpenAppCommand.Execute(null);
            return;
        }

        _pressToDrag = e;
        _pressPoint = position;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        TrackHint(e);
        if (_pressToDrag is not PointerPressedEventArgs press)
        {
            return;
        }

        Point position = e.GetPosition(this);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pressToDrag = null;
            return;
        }

        double dx = position.X - _pressPoint.X;
        double dy = position.Y - _pressPoint.Y;
        if ((dx * dx) + (dy * dy) > DragStartDistance * DragStartDistance)
        {
            _pressToDrag = null;
            _doubleClick?.Reset();
            _movedByUser = true;
            _dragging = true;
            try
            {
                // Windows runs its own move loop in here until the mouse button is let go.
                BeginMoveDrag(press);
            }
            finally
            {
                _dragging = false;
            }

            // The drop settles a moment after the last move (or at once if Ctrl+Alt is let go first).
            _moveSettled.Stop();
            _moveSettled.Start();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _pressToDrag = null;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ForgetHint();
    }

    /// <summary>Keeps a showing hint in front of this window (call after <see cref="KeepOnTop"/>).</summary>
    public void KeepHintOnTop()
    {
        _hint?.KeepOnTop();
    }

    /// <summary>
    /// The pointer moved (only while Ctrl+Alt is held; otherwise clicks pass through): a hint shows once it rests a
    /// moment over a control that has one, and follows it while it stays over that control.
    /// </summary>
    private void TrackHint(PointerEventArgs e)
    {
        _hintCursor = this.PointToScreen(e.GetPosition(this));
        Control? source = HintSourceOf(e.Source as Visual);
        if (source != _hintSource)
        {
            HideHint();
            _hintSource = source;
            _hintPressed = false;
        }

        if (source is null || _hintPressed)
        {
            return;
        }

        if (_hint is { IsVisible: true })
        {
            ShowHint();
        }
        else
        {
            // It has to rest a moment first: each move starts the wait again.
            _hintDelay.Stop();
            _hintDelay.Start();
        }
    }

    /// <summary>The nearest control with a hint, from the one under the pointer up; null when none.</summary>
    private Control? HintSourceOf(Visual? visual)
    {
        for (Visual? current = visual; current is not null && current != this; current = Avalonia.VisualTree.VisualExtensions.GetVisualParent(current))
        {
            if (current is Control control && !string.IsNullOrEmpty(OverlayHint.GetText(control)))
            {
                return control;
            }
        }

        return null;
    }

    private void ShowHint()
    {
        if (!_expanded || !IsVisible || _hintSource is not Control source || OverlayHint.GetText(source) is not { Length: > 0 } text)
        {
            return;
        }

        _hint ??= new OverlayHintWindow(_control);
        _hint.ShowAt(text, _hintCursor);
    }

    private void HideHint()
    {
        _hintDelay.Stop();
        _hint?.Hide();
    }

    /// <summary>Hides the hint and forgets the control it was for (the pointer left, or Ctrl+Alt was let go).</summary>
    private void ForgetHint()
    {
        HideHint();
        _hintSource = null;
        _hintPressed = false;
    }

    private void SlideTowardTarget()
    {
        if (_movedByUser)
        {
            // Being dragged (or just dropped): moving it to the corner now would pull it out of the mouse's hand.
            return;
        }

        if (_isTemporary)
        {
            _slide.Stop();
            return;
        }

        if (!_inDefaultCorner)
        {
            _inV = _inH = 0;
            _slide.Stop();
            return;
        }

        int remainingV = _targetInV - _inV;
        int remainingH = _targetInH - _inH;
        int remainingLift = _targetLift - _lift;
        if (Math.Abs(remainingV) <= 2 && Math.Abs(remainingH) <= 2 && Math.Abs(remainingLift) <= 2)
        {
            _inV = _targetInV;
            _inH = _targetInH;
            _lift = _targetLift;
            _slide.Stop();
        }
        else
        {
            _inV += Step(remainingV);
            _inH += Step(remainingH);
            _lift += Step(remainingLift);
        }

        MoveToDefaultCorner();
    }

    private static int Step(int remaining)
    {
        // Eases out quickly: half of the way each step (the taskbar slides in about as fast).
        return Math.Abs(remaining) <= 2 ? remaining : (int)Math.Round(remaining / 2.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// In a bottom corner: how far the taskbar reaches up into the screen right now, above the usual bottom (an
    /// auto-hiding taskbar is mostly off screen until it is shown, and a fixed one is already outside the work area).
    /// </summary>
    private void UpdateTaskbarLift()
    {
        Screen? screen = DefaultScreen();
        int lift = 0;
        if (!IsTop && screen is not null && _control?.TaskbarBox() is ScreenBox taskbar)
        {
            PixelRect bounds = screen.Bounds;
            bool atBottom = taskbar.Top > bounds.Y + (bounds.Height / 2) && taskbar.Left < bounds.Right && taskbar.Right > bounds.X;
            if (atBottom)
            {
                lift = Math.Max(0, screen.WorkingArea.Bottom - Math.Max(taskbar.Top, bounds.Y));
            }
        }

        if (lift != _targetLift)
        {
            _targetLift = lift;
            if (_inDefaultCorner && !_movedByUser && !_isTemporary)
            {
                _slide.Start();
            }
        }
    }

    private void PollDragKeys()
    {
        bool held = _control?.AreDragKeysHeld() == true;
        var viewModel = DataContext as OnScreenOverlayViewModel;
        bool hovered = !held && _control is not null && TryGetPlatformHandle() is IPlatformHandle handle
                       && _control.IsCursorOver(handle.Handle);
        viewModel?.SetHovered(hovered);

        if (held == _dragKeysHeld)
        {
            return;
        }

        _dragKeysHeld = held;
        SetClickThrough(!held);
        if (held)
        {
            // The bar stays put: the details open from its corner, to the right of its left edge and below its top edge,
            // or to the left of its right edge and above its bottom edge in the right column and the bottom row. (Middle
            // column and row open right and down, like the left and top.) FitLayout turns them around if they would not fit.
            _layoutZone = new OverlayAnchorZone(_zone.AnchorsRight ? 2 : 0, _zone.AnchorsBottom ? 2 : 0);
            _layoutPoint = _layoutZone.PointOn(FoldedBar());
            viewModel?.SetDetailsDirection(_layoutZone.AnchorsBottom, _layoutZone.AnchorsRight);
        }
        else if (_movedByUser && !_dragging)
        {
            // Let go of Ctrl+Alt before the drop was settled: settle it now, so folding up keeps the dropped spot.
            FinishDrag();
        }

        _expanded = held;
        if (!held)
        {
            ForgetHint();
        }

        viewModel?.SetDragReady(held);
    }

    /// <summary>
    /// Back in front of other always-on-top windows (a meeting app's, a tool's) that came up after it: Windows keeps
    /// the newest of those in front, and Topmost alone never takes the spot back.
    /// </summary>
    public void KeepOnTop()
    {
        if (_control is not null && TryGetPlatformHandle() is IPlatformHandle handle)
        {
            _control.KeepOnTop(handle.Handle);
        }
    }

    private void SetClickThrough(bool clickThrough)
    {
        if (_control is not null && TryGetPlatformHandle() is IPlatformHandle handle)
        {
            _control.SetClickThrough(handle.Handle, clickThrough);
        }
    }

    /// <summary>
    /// After a drag while moved for a maximized app: where it was dropped (in from the corner) becomes that app's
    /// distances, and it stays tied to the corner.
    /// </summary>
    private bool TryAppOffset(out double inVDips, out double inHDips)
    {
        inVDips = inHDips = 0;
        Screen? screen = DefaultScreen();
        if (screen is null)
        {
            return false;
        }

        (int inV, int inH) = DistancesFromCorner(screen, FoldedBar());
        inV = Math.Max(0, inV);
        inH = Math.Max(0, inH);
        inVDips = Math.Round(inV / screen.Scaling);
        inHDips = Math.Round(inH / screen.Scaling);
        _inDefaultCorner = true;
        _inV = _targetInV = inV;
        _inH = _targetInH = inH;
        MoveToDefaultCorner();
        return true;
    }

    /// <summary>True when the folded bar's rectangle puts it within a short distance of the default corner spot.</summary>
    private bool IsNearDefaultCorner(PixelRect bar)
    {
        Screen? screen = DefaultScreen();
        if (screen is null)
        {
            return false;
        }

        (int inV, int inH) = DistancesFromCorner(screen, bar);
        double snap = SnapDips * screen.Scaling;
        return Math.Abs(inV) <= snap && Math.Abs(inH) <= snap;
    }

    /// <summary>How far the folded bar's corner (the default corner's) is in from the corner spot (vertically, sideways), in physical pixels.</summary>
    private (int InV, int InH) DistancesFromCorner(Screen screen, PixelRect bar)
    {
        PixelRect area = screen.WorkingArea;
        int inH = IsRight ? area.Right - EdgeMarginPixels - bar.Right : bar.X - area.X - EdgeMarginPixels;
        int inV = IsTop ? bar.Y - area.Y - EdgeMarginPixels : area.Bottom - EdgeMarginPixels - _lift - bar.Bottom;
        return (inV, inH);
    }

    /// <summary>The folded size (without the Ctrl+Alt details) in physical pixels.</summary>
    private PixelSize SizeOn(Screen screen)
    {
        double width = _foldedWidthDips > 0 ? _foldedWidthDips : Bounds.Width > 0 ? Bounds.Width : TypicalWidthDips;
        double height = _foldedHeightDips > 0 ? _foldedHeightDips : Bounds.Height > 0 ? Bounds.Height : TypicalHeightDips;
        return new PixelSize((int)Math.Ceiling(width * screen.Scaling), (int)Math.Ceiling(height * screen.Scaling));
    }

    /// <summary>The window's size now in physical pixels; the folded size before it has been laid out.</summary>
    private PixelSize WindowSize(Screen screen)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return SizeOn(screen);
        }

        return new PixelSize((int)Math.Ceiling(Bounds.Width * RenderScaling), (int)Math.Ceiling(Bounds.Height * RenderScaling));
    }

    private Screen? DefaultScreen()
    {
        return Screens.Primary ?? Screens.All.FirstOrDefault();
    }

    /// <summary>The monitor a saved top-left of the folded bar is on (just inside the corner, so an edge belongs to the monitor it borders).</summary>
    private Screen? ScreenAtOrigin(PixelPoint origin)
    {
        return Screens.ScreenFromPoint(new PixelPoint(origin.X + 1, origin.Y + 1)) ?? DefaultScreen();
    }

    /// <summary>The monitor the folded bar is on now (by its centre).</summary>
    private Screen? ScreenOfBar()
    {
        Screen? primary = DefaultScreen();
        if (primary is null)
        {
            return null;
        }

        PixelRect rough = _zone.RectAt(_anchorPoint, SizeOn(primary));
        return Screens.ScreenFromPoint(rough.Center) ?? primary;
    }

    /// <summary>The folded bar's rectangle (physical pixels) from its zone and anchor point.</summary>
    private PixelRect FoldedBar()
    {
        Screen? screen = ScreenOfBar();
        return screen is null ? new PixelRect(_anchorPoint, new PixelSize(0, 0)) : _zone.RectAt(_anchorPoint, SizeOn(screen));
    }

    /// <summary>The top-left of the folded bar: what is saved, and what <see cref="PositionChosen"/> reports.</summary>
    private PixelPoint FoldedOrigin()
    {
        return FoldedBar().Position;
    }

    /// <summary>Works out the zone and anchor point from a folded bar's top-left (a saved spot).</summary>
    private void StoreFromOrigin(PixelPoint origin)
    {
        Screen? screen = ScreenAtOrigin(origin);
        if (screen is not null)
        {
            StoreFromBar(new PixelRect(origin, SizeOn(screen)), screen);
        }
    }

    /// <summary>The bar is in the zone of its centre on that monitor; the anchor point is that zone's point on the bar.</summary>
    private void StoreFromBar(PixelRect bar, Screen screen)
    {
        _zone = OverlayAnchorZone.Of(screen.WorkingArea, bar.Center);
        _anchorPoint = _zone.PointOn(bar);
    }

    /// <summary>
    /// Moves the open window's anchor point to where the bar is stored (after the spot changed while the details are
    /// open: back to the corner, undo), so the window follows the bar.
    /// </summary>
    private void SyncLayoutToBar()
    {
        Screen? screen = ScreenOfBar();
        if (screen is null)
        {
            return;
        }

        PixelRect window = _layoutZone.WindowFor(FoldedBar(), WindowSize(screen));
        _layoutPoint = _layoutZone.PointOn(window);
    }

    private void MoveToDefaultCorner()
    {
        Screen? screen = DefaultScreen();
        if (screen is null)
        {
            return;
        }

        PixelRect area = screen.WorkingArea;
        _zone = OverlayAnchorZone.ForCorner(_corner);
        _anchorPoint = new PixelPoint(
            IsRight ? area.Right - EdgeMarginPixels - _inH : area.X + EdgeMarginPixels + _inH,
            IsTop ? area.Y + EdgeMarginPixels + _inV : area.Bottom - EdgeMarginPixels - _lift - _inV);
        _pendingOrigin = null;
        if (_expanded)
        {
            SyncLayoutToBar();
        }

        ApplyAnchor();
    }

    /// <summary>
    /// The drop of a drag, settled. The bar is put in the 3×3 zone its centre is in on the monitor it is on (and kept
    /// inside that monitor's work area), the open window keeps growing from where it was dropped, and a drop near the
    /// default corner snaps back into it.
    /// </summary>
    private void FinishDrag()
    {
        _moveSettled.Stop();
        if (!_movedByUser)
        {
            return;
        }

        _movedByUser = false;
        _pendingOrigin = null;
        Screen? windowScreen = DefaultScreen();
        if (windowScreen is null)
        {
            return;
        }

        PixelRect window = new(Position, WindowSize(windowScreen));
        windowScreen = Screens.ScreenFromPoint(window.Center) ?? windowScreen;
        PixelRect bar = _layoutZone.BarIn(window, SizeOn(windowScreen));

        // The bar's monitor decides the zone, the size in pixels (its scaling) and the work area it is kept in.
        Screen screen = Screens.ScreenFromPoint(bar.Center) ?? windowScreen;
        bar = _layoutZone.BarIn(window, SizeOn(screen));
        PixelRect inside = OverlayAnchorZone.Clamp(bar, screen.WorkingArea);
        _layoutPoint = _layoutZone.PointOn(new PixelRect(
            window.X + (inside.X - bar.X), window.Y + (inside.Y - bar.Y), window.Width, window.Height));
        StoreFromBar(inside, screen);
        FitLayout();

        if (_avoidedApp is null && IsNearDefaultCorner(inside))
        {
            // Dropped back at the corner: that's the default spot again, kept.
            _isTemporary = false;
            _inDefaultCorner = true;
            _savedPosition = null;
            MoveToDefaultCorner();
            PositionChosen?.Invoke(this, null);
            TemporaryChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Only for now, until Keep.
        _temporaryForApp = _isTemporary ? _temporaryForApp : _avoidedApp;
        _isTemporary = true;
        ApplyAnchor();
        TemporaryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// While the details are open: turns them around (right/left, below/above the bar) when they would run off the
    /// monitor's work area and the other way fits, so the bar stays put and only the details move. If neither way fits,
    /// <see cref="ApplyAnchor"/> keeps the window inside and the bar moves with it.
    /// </summary>
    private void FitLayout()
    {
        Screen? screen = _expanded ? ScreenOfBar() : null;
        if (screen is null)
        {
            return;
        }

        PixelRect area = screen.WorkingArea;
        PixelSize size = WindowSize(screen);
        PixelRect bar = FoldedBar();
        bool right = _layoutZone.AnchorsRight;
        bool bottom = _layoutZone.AnchorsBottom;
        if (!FitsHorizontally(right, bar, size, area) && FitsHorizontally(!right, bar, size, area))
        {
            right = !right;
        }

        if (!FitsVertically(bottom, bar, size, area) && FitsVertically(!bottom, bar, size, area))
        {
            bottom = !bottom;
        }

        if (right == _layoutZone.AnchorsRight && bottom == _layoutZone.AnchorsBottom)
        {
            return;
        }

        _layoutZone = new OverlayAnchorZone(right ? 2 : 0, bottom ? 2 : 0);
        SyncLayoutToBar();
        (DataContext as OnScreenOverlayViewModel)?.SetDetailsDirection(_layoutZone.AnchorsBottom, _layoutZone.AnchorsRight);
    }

    private static bool FitsHorizontally(bool right, PixelRect bar, PixelSize size, PixelRect area)
    {
        int left = right ? bar.Right - size.Width : bar.X;
        return left >= area.X && left + size.Width <= area.Right;
    }

    private static bool FitsVertically(bool bottom, PixelRect bar, PixelSize size, PixelRect area)
    {
        int top = bottom ? bar.Bottom - size.Height : bar.Y;
        return top >= area.Y && top + size.Height <= area.Bottom;
    }

    /// <summary>
    /// Puts the window where its anchor point stays put: the bar's while folded, the open window's while the details
    /// are open. The bar is first kept inside the work area of the monitor it is on (the stored spot moves with it, so
    /// nothing off screen is ever saved), then the window.
    /// </summary>
    private void ApplyAnchor()
    {
        Screen? screen = ScreenOfBar();
        if (screen is null)
        {
            return;
        }

        PixelRect area = screen.WorkingArea;
        PixelRect bar = _zone.RectAt(_anchorPoint, SizeOn(screen));
        PixelRect insideBar = OverlayAnchorZone.Clamp(bar, area);
        if (insideBar.X != bar.X || insideBar.Y != bar.Y)
        {
            _anchorPoint = new PixelPoint(_anchorPoint.X + (insideBar.X - bar.X), _anchorPoint.Y + (insideBar.Y - bar.Y));
            if (_expanded)
            {
                SyncLayoutToBar();
            }
        }

        PixelSize size = WindowSize(screen);
        PixelRect window = _expanded ? _layoutZone.RectAt(_layoutPoint, size) : _zone.RectAt(_anchorPoint, size);
        Position = OverlayAnchorZone.Clamp(window, area).Position;
    }
}
