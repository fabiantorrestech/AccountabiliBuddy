using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Ports;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The corner clock (11b) shown while an app is full screen: top right of that monitor, 26 px tall, one rounded inner corner, about 86%
/// opaque, clicks passing through (WS_EX_TRANSPARENT). It fades to 25% while the pointer is near it; since a click-through window gets no
/// pointer events, that is checked about four times a second, and only while this window is on screen. In a game it shows the time only
/// and is fainter.
/// </summary>
public partial class CornerClockWindow : Window
{
    private const double Near = 90;
    private const double NormalOpacity = 0.86;
    private const double GameOpacity = 0.6;
    private const double NearOpacity = 0.25;
    private static readonly TimeSpan NearCheck = TimeSpan.FromMilliseconds(250);

    private readonly IOverlayWindowControl _control;
    private readonly TaskbarOverlayViewModel _model;
    private readonly DispatcherTimer _nearTimer = new() { Interval = NearCheck };
    private PixelBox _monitor = new(0, 0, 0, 0);
    private bool _game;

    // Used by the XAML previewer.
    public CornerClockWindow()
        : this(null!, null!)
    {
    }

    public CornerClockWindow(IOverlayWindowControl control, TaskbarOverlayViewModel model)
    {
        _control = control;
        _model = model;
        InitializeComponent();
        DataContext = model;
        Opened += (_, _) =>
        {
            if (TryGetPlatformHandle() is IPlatformHandle handle)
            {
                _control.SetClickThrough(handle.Handle, true);
            }

            _nearTimer.Start();
        };
        Closed += (_, _) => _nearTimer.Stop();
        _nearTimer.Tick += (_, _) => FadeWhenNear();
        SizeChanged += (_, _) => Reposition();
    }

    /// <summary>Shows or updates the clock on a monitor.</summary>
    public void Apply(PixelBox monitor, OverlaySettings settings, bool game)
    {
        _monitor = monitor;
        _game = game;
        CornerClockParts parts = game ? CornerClockParts.Time : settings.CornerClockParts;
        SessionPart.IsVisible = parts.HasFlag(CornerClockParts.Session) && _model.IsSession;
        TimePart.IsVisible = parts.HasFlag(CornerClockParts.Time);
        DatePart.IsVisible = parts.HasFlag(CornerClockParts.Date);
        bool battery = parts.HasFlag(CornerClockParts.Battery) && _model.Batteries.Count > 0;
        BatteryPart.IsVisible = battery;
        BatteryText.Text = battery ? _model.Batteries[0].Text : string.Empty;
        Divider1.IsVisible = SessionPart.IsVisible && (TimePart.IsVisible || DatePart.IsVisible || battery);
        Pill.Opacity = game ? GameOpacity : NormalOpacity;
        if (!IsVisible)
        {
            Position = new PixelPoint(monitor.Right - 200, monitor.Top + 1);
        }

        Reposition();
    }

    public void KeepOnTop()
    {
        if (TryGetPlatformHandle() is IPlatformHandle handle)
        {
            _control.KeepOnTop(handle.Handle);
        }
    }

    private void Reposition()
    {
        if (_monitor.Width == 0 || Bounds.Width <= 0)
        {
            return;
        }

        int width = (int)Math.Ceiling(Bounds.Width * DesktopScaling);
        Position = new PixelPoint(_monitor.Right - width, _monitor.Top);
        KeepOnTop();
    }

    private void FadeWhenNear()
    {
        Pill.Opacity = IsPointerNear() ? NearOpacity : _game ? GameOpacity : NormalOpacity;
    }

    private bool IsPointerNear()
    {
        if (TryGetPlatformHandle() is not IPlatformHandle handle)
        {
            return false;
        }

        return _control.IsCursorNear(handle.Handle, (int)(Near * DesktopScaling));
    }
}
