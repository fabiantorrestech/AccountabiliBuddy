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
/// With the mouse over it (and Ctrl+Alt not held) it fades almost away. In its default corner it slides down while a
/// maximized app on that screen is in front, so the app's top-right buttons stay clickable, and back up after.
/// </summary>
public partial class SessionOverlayWindow : Window
{
    private static readonly TimeSpan KeyPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MoveSettleDelay = TimeSpan.FromMilliseconds(400);
    private const int EdgeMarginPixels = 16;
    private static readonly TimeSpan SlideStep = TimeSpan.FromMilliseconds(15);

    private readonly IOverlayWindowControl? _control;
    private readonly DispatcherTimer _keyPoll;
    private readonly DispatcherTimer _moveSettled;
    private bool _dragKeysHeld;
    private bool _movedByUser;

    // True while no saved position is in use, so the timer keeps to the top-right corner as its size changes.
    private bool _inDefaultCorner = true;

    // How far below the corner it sits now (physical pixels), and where it is sliding to.
    private int _dropPixels;
    private int _targetDropPixels;
    private readonly DispatcherTimer _slide;

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
        _keyPoll.Tick += (_, _) => PollDragKeys();
        _slide = new DispatcherTimer { Interval = SlideStep };
        _slide.Tick += (_, _) => SlideTowardTarget();
        _moveSettled = new DispatcherTimer { Interval = MoveSettleDelay };
        _moveSettled.Tick += (_, _) =>
        {
            _moveSettled.Stop();
            _movedByUser = false;
            _inDefaultCorner = false;
            PositionChosen?.Invoke(this, Position);
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
            if (_inDefaultCorner)
            {
                MoveToDefaultCorner();
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

    /// <summary>Raised with the new top-left corner (pixels) after the user drags the timer somewhere.</summary>
    public event EventHandler<PixelPoint>? PositionChosen;

    /// <summary>Puts the timer at a saved spot if it is still on a screen, otherwise in the top-right corner.</summary>
    public void PlaceAt(int? x, int? y)
    {
        if (x is int left && y is int top
            && Screens.All.Any(screen => screen.WorkingArea.Contains(new PixelPoint(left + 1, top + 1))))
        {
            _inDefaultCorner = false;
            Position = new PixelPoint(left, top);
            return;
        }

        _inDefaultCorner = true;
        MoveToDefaultCorner();
    }

    /// <summary>
    /// Slides down by <paramref name="dropDips"/> while a maximized app fills the screen the default corner is on
    /// (<paramref name="maximizedScreen"/>), and back up when there's none. Only in the default corner: a spot the
    /// user dragged it to is never changed.
    /// </summary>
    public void AvoidMaximizedApp(ScreenBox? maximizedScreen, double dropDips)
    {
        Screen? screen = DefaultScreen();
        bool onThatScreen = screen is not null && maximizedScreen is ScreenBox box
            && box.Left == screen.Bounds.X && box.Top == screen.Bounds.Y
            && box.Right == screen.Bounds.Right && box.Bottom == screen.Bounds.Bottom;
        int target = _inDefaultCorner && onThatScreen ? (int)Math.Round(dropDips * screen!.Scaling) : 0;
        if (target == _targetDropPixels)
        {
            return;
        }

        _targetDropPixels = target;
        _slide.Start();
    }

    private void SlideTowardTarget()
    {
        int remaining = _targetDropPixels - _dropPixels;
        if (Math.Abs(remaining) <= 2 || !_inDefaultCorner)
        {
            _dropPixels = _inDefaultCorner ? _targetDropPixels : 0;
            _slide.Stop();
        }
        else
        {
            // Eases out: a quarter of the way each step.
            _dropPixels += (int)Math.Round(remaining / 4.0, MidpointRounding.AwayFromZero);
        }

        if (_inDefaultCorner)
        {
            MoveToDefaultCorner();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_dragKeysHeld && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _movedByUser = true;
            BeginMoveDrag(e);
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
        viewModel?.SetDragReady(held);
    }

    private void SetClickThrough(bool clickThrough)
    {
        if (_control is not null && TryGetPlatformHandle() is IPlatformHandle handle)
        {
            _control.SetClickThrough(handle.Handle, clickThrough);
        }
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

        PixelRect area = screen.WorkingArea;
        int width = (int)Math.Ceiling(Bounds.Width * screen.Scaling);
        Position = new PixelPoint(area.Right - width - EdgeMarginPixels, area.Y + EdgeMarginPixels + _dropPixels);
    }
}
