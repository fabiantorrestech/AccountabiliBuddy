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
/// The Ctrl+Alt details open toward the middle of the screen, so the bar never moves and every spot is measured from the
/// bar alone.
/// </summary>
public partial class SessionOverlayWindow : Window
{
    private static readonly TimeSpan KeyPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MoveSettleDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan SlideStep = TimeSpan.FromMilliseconds(15);
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

    // Where the folded window (just the bar, no Ctrl+Alt details) sits: the one spot every placement, drag, distance and
    // saved position is measured from. While the details are open the window grows away from it (toward the middle of
    // the screen), so the bar itself never moves.
    private PixelPoint _anchor;
    private bool _expanded;
    private bool _opensUp;
    private bool _opensLeft;
    private double _foldedWidthDips;
    private double _foldedHeightDips;

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
        _moveSettled = new DispatcherTimer { Interval = MoveSettleDelay };
        _moveSettled.Tick += (_, _) =>
        {
            _moveSettled.Stop();
            _movedByUser = false;
            _anchor = AnchorOfWindow();
            if (_avoidedApp is null && IsNearDefaultCorner(_anchor))
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
            TemporaryChanged?.Invoke(this, EventArgs.Empty);
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
        };
        SizeChanged += (_, _) =>
        {
            if (!_expanded)
            {
                _foldedWidthDips = Bounds.Width;
                _foldedHeightDips = Bounds.Height;
            }

            if (_movedByUser)
            {
                // Mid-drag: only folding up (Ctrl+Alt let go) moves it, so the bar stays where it was dropped.
                if (!_expanded)
                {
                    ApplyAnchor();
                }

                return;
            }

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
            _savedPosition = _anchor;
            PositionChosen?.Invoke(this, _anchor);
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
            _anchor = saved;
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
        if (_inDefaultCorner)
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
            && !IsNearDefaultCorner(new PixelPoint(left, top)))
        {
            _inDefaultCorner = false;
            _savedPosition = new PixelPoint(left, top);
            _anchor = new PixelPoint(left, top);
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
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
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
            BeginMoveDrag(press);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _pressToDrag = null;
    }

    private void SlideTowardTarget()
    {
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
        _anchor = AnchorOfWindow();
        if (held)
        {
            ChooseOpenDirection();
            viewModel?.SetDetailsDirection(_opensUp, _opensLeft);
        }

        _expanded = held;
        viewModel?.SetDragReady(held);
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

        (int inV, int inH) = DistancesFromCorner(screen, _anchor);
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

    /// <summary>True when a top-left position puts the window within a short distance of the default corner.</summary>
    private bool IsNearDefaultCorner(PixelPoint position)
    {
        Screen? screen = DefaultScreen();
        if (screen is null)
        {
            return false;
        }

        (int inV, int inH) = DistancesFromCorner(screen, position);
        double snap = SnapDips * screen.Scaling;
        return Math.Abs(inV) <= snap && Math.Abs(inH) <= snap;
    }

    /// <summary>How far a top-left position is in from the corner spot (vertically, sideways), in physical pixels.</summary>
    private (int InV, int InH) DistancesFromCorner(Screen screen, PixelPoint position)
    {
        (int width, int height) = SizeOn(screen);
        PixelRect area = screen.WorkingArea;
        int inH = IsRight ? area.Right - EdgeMarginPixels - width - position.X : position.X - area.X - EdgeMarginPixels;
        int inV = IsTop ? position.Y - area.Y - EdgeMarginPixels : area.Bottom - EdgeMarginPixels - _lift - height - position.Y;
        return (inV, inH);
    }

    /// <summary>The folded size (without the Ctrl+Alt details) in physical pixels.</summary>
    private (int Width, int Height) SizeOn(Screen screen)
    {
        double width = _foldedWidthDips > 0 ? _foldedWidthDips : Bounds.Width > 0 ? Bounds.Width : TypicalWidthDips;
        double height = _foldedHeightDips > 0 ? _foldedHeightDips : Bounds.Height > 0 ? Bounds.Height : TypicalHeightDips;
        return ((int)Math.Ceiling(width * screen.Scaling), (int)Math.Ceiling(height * screen.Scaling));
    }

    private Screen? DefaultScreen()
    {
        return Screens.Primary ?? Screens.All.FirstOrDefault();
    }

    private void MoveToDefaultCorner()
    {
        Screen? screen = DefaultScreen();
        if (screen is null)
        {
            return;
        }

        (int width, int height) = SizeOn(screen);
        PixelRect area = screen.WorkingArea;
        int x = IsRight ? area.Right - width - EdgeMarginPixels - _inH : area.X + EdgeMarginPixels + _inH;
        int y = IsTop ? area.Y + EdgeMarginPixels + _inV : area.Bottom - height - EdgeMarginPixels - _lift - _inV;
        _anchor = new PixelPoint(x, y);
        ApplyAnchor();
    }

    /// <summary>
    /// The details open toward the middle of the screen the bar is on: above it in the lower half, below it in the
    /// upper half (and leftward in the right half, if wider than the bar), so they never run off the edge.
    /// </summary>
    private void ChooseOpenDirection()
    {
        Screen? screen = Screens.ScreenFromPoint(_anchor) ?? DefaultScreen();
        if (screen is null)
        {
            _opensUp = _opensLeft = false;
            return;
        }

        (int width, int height) = SizeOn(screen);
        PixelRect area = screen.WorkingArea;
        _opensUp = _anchor.Y + (height / 2) > area.Y + (area.Height / 2);
        _opensLeft = _anchor.X + (width / 2) > area.X + (area.Width / 2);
    }

    /// <summary>How much bigger than folded the window is now (physical pixels); nothing while folded.</summary>
    private (int Width, int Height) Growth()
    {
        if (!_expanded || _foldedWidthDips <= 0 || _foldedHeightDips <= 0)
        {
            return (0, 0);
        }

        double scaling = RenderScaling;
        int width = (int)Math.Ceiling(Bounds.Width * scaling) - (int)Math.Ceiling(_foldedWidthDips * scaling);
        int height = (int)Math.Ceiling(Bounds.Height * scaling) - (int)Math.Ceiling(_foldedHeightDips * scaling);
        return (Math.Max(0, width), Math.Max(0, height));
    }

    /// <summary>Puts the window where the bar stays at <see cref="_anchor"/>.</summary>
    private void ApplyAnchor()
    {
        (int width, int height) = Growth();
        Position = new PixelPoint(_anchor.X - (_opensLeft ? width : 0), _anchor.Y - (_opensUp ? height : 0));
    }

    /// <summary>Where the bar is, from where the window is now (after the user dragged it).</summary>
    private PixelPoint AnchorOfWindow()
    {
        (int width, int height) = Growth();
        return new PixelPoint(Position.X + (_opensLeft ? width : 0), Position.Y + (_opensUp ? height : 0));
    }
}
