using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Ports;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The session overlay's tile (ADR 51, boards 13a, 14a, 15b): a borderless, topmost tool window that never takes focus
/// (WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW: never in Alt+Tab), laid over the taskbar's free stretch or floating at the bottom of the work
/// area, or standing at the side beside a side taskbar. <see cref="Apply"/> says where; the window picks its content from the settings
/// (what shows and in what order) and puts itself against the band it was given once its width is known. A click on an empty spot opens the
/// Ctrl+Alt panel; the buttons work as they are.
/// </summary>
public partial class TaskbarOverlayWindow : Window
{
    private const double DragStartPixels = 4;

    private readonly IOverlayWindowControl _control;
    private readonly TaskbarOverlayViewModel _model;
    private OverlayTarget _target = OverlayTarget.Hidden;
    private OverlaySettings _settings = OverlaySettings.Default;
    private OverlayAppRule? _rule;
    private string? _layoutKey;
    private PointerPressedEventArgs? _press;
    private Point _pressPoint;
    private bool _dragging;
    private int _pressWindowY;
    private bool? _clickThrough;

    // Used by the XAML previewer.
    public TaskbarOverlayWindow()
        : this(null!, null!)
    {
    }

    public TaskbarOverlayWindow(IOverlayWindowControl control, TaskbarOverlayViewModel model)
    {
        _control = control;
        _model = model;
        InitializeComponent();
        DataContext = model;
        Opened += (_, _) =>
        {
            _clickThrough = null;
            ApplyClickThrough();
            Reposition();
        };
        SizeChanged += (_, _) => Reposition();
        ScalingChanged += (_, _) => Reposition();
    }

    /// <summary>A click on an empty spot of the tile: the app opens the panel.</summary>
    public event EventHandler? Clicked;

    /// <summary>The vertical tile was dragged along the screen edge: its new top (physical pixels).</summary>
    public event EventHandler<int>? SideTileMoved;

    /// <summary>Where the tile is now (physical pixels).</summary>
    public PixelRect Box => new(Position, new PixelSize((int)Math.Ceiling(Bounds.Width * DesktopScaling), (int)Math.Ceiling(Bounds.Height * DesktopScaling)));

    public OverlayTarget Target => _target;

    /// <summary>
    /// A Windows flyout (hidden icons, Quick Settings, notifications, calendar) or a tray app's menu is in front: the tile stays where it is in
    /// the stack, under it, and climbs back when this is cleared (the service clears it when the flyout or menu closes).
    /// </summary>
    public bool YieldsToShellFlyout { get; set; }

    /// <summary>Back in front of the taskbar and other always-on-top windows, without taking focus; not over a Windows flyout.</summary>
    public void KeepOnTop()
    {
        if (YieldsToShellFlyout)
        {
            return;
        }

        if (TryGetPlatformHandle() is IPlatformHandle handle)
        {
            _control.KeepOnTop(handle.Handle);
        }
    }

    /// <summary>How solid the tile looks (Settings › Overlay). Its content only: clicks and the panel are not affected. The time and date may stay solid.</summary>
    public void SetTileOpacity(double opacity, bool solidTime, bool solidDate)
    {
        TileView.SetSolidity(opacity, solidTime, solidDate);
    }

    public void Apply(OverlayTarget target, OverlaySettings settings, OverlayAppRule? rule)
    {
        bool monitorChanged = _target.MonitorId != target.MonitorId || Math.Abs(_target.Scale - target.Scale) > 0.001;
        _target = target;
        _settings = settings;
        _rule = rule;
        _model.ApplyLayout(settings);
        ApplyClickThrough();

        string key = $"{target.Kind}|{target.CoversTray}|{target.AlignRight}|{settings.Size}|{settings.Progress}|{settings.Shape}|{string.Join(',', settings.Items)}";
        double heightDips = OverlayTileGeometry.HeightFor(target, settings);
        if (key != _layoutKey)
        {
            _layoutKey = key;
            TileView.Apply(target, settings, heightDips);
        }

        Height = heightDips;
        MaxWidth = MaxWidthFor(target);
        if (monitorChanged || !IsVisible)
        {
            // Start on the target monitor so the window gets that monitor's scaling from the beginning.
            Position = new PixelPoint(target.Band.Left + 1, target.Band.Top + 1);
        }

        Reposition();
    }

    // The room the tile has: its band when docked, the work area less the side margins otherwise (nothing cuts it off at a fixed width).
    private double MaxWidthFor(OverlayTarget target)
    {
        double max = OverlayTileGeometry.MaxWidthFor(target);
        if (!double.IsInfinity(max))
        {
            return max;
        }

        // No taskbar known: the main monitor's work area.
        double scale = DesktopScaling;
        (int side, _) = OverlayPlacementCalculator.FloatingMargins(scale);
        return Math.Max(120, (PrimaryArea().Width - (2 * side)) / scale);
    }

    private void Reposition()
    {
        if (_target.Kind == OverlayTargetKind.Hidden || Bounds.Width <= 0)
        {
            return;
        }

        double scale = DesktopScaling;
        int width = (int)Math.Ceiling(Bounds.Width * scale);
        // The height the window really has: Windows keeps a window above a minimum height (about 56 px at 150%), so a short tile (Small) can come
        // out taller than asked, and it must be placed by what it is, or its bottom edge ends up off the spot it was meant for.
        // The small subtraction keeps a pixel count that went to dips and back (at 175%, say) from rounding up one pixel.
        int height = (int)Math.Ceiling((Math.Max(Height, (FrameSize ?? ClientSize).Height) * scale) - 1e-6);
        PixelBox fallbackArea = _target.MonitorId is null ? PrimaryArea() : _target.Band;
        (int x, int y) = OverlayTileGeometry.PositionFor(_target, _settings, _rule, width, height, scale, fallbackArea);
        var next = new PixelPoint(x, y);
        if (Position != next)
        {
            Position = next;
        }

        KeepOnTop();
    }

    private PixelBox PrimaryArea()
    {
        PixelRect area = (Screens.Primary ?? Screens.All.First()).WorkingArea;
        return new PixelBox(area.X, area.Y, area.Right, area.Bottom);
    }

    // Overlay settings' "Click through": the window's extended style, set again only when the choice changes.
    private void ApplyClickThrough()
    {
        bool wanted = _settings.ClickThrough;
        if (_clickThrough == wanted || TryGetPlatformHandle() is not IPlatformHandle handle)
        {
            return;
        }

        _control.SetClickThrough(handle.Handle, wanted);
        _clickThrough = wanted;
        if (wanted)
        {
            // A press that started before the switch must not finish as a click or a drag.
            _press = null;
            _dragging = false;
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        // The user looked: the unread-nudge dot goes (18d).
        _model.HasNudgeDot = false;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _press = e;
        _pressPoint = e.GetPosition(this);
        _dragging = false;
        _pressWindowY = Position.Y;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_press is null || _target.Kind != OverlayTargetKind.VerticalTile)
        {
            return;
        }

        double dy = (e.GetPosition(this).Y - _pressPoint.Y) * DesktopScaling;
        if (!_dragging && Math.Abs(dy) < DragStartPixels)
        {
            return;
        }

        // The vertical tile rides the screen edge: it only moves up and down, within the work area.
        _dragging = true;
        PixelBox band = _target.Band;
        int top = Math.Clamp(_pressWindowY + (int)dy, band.Top, Math.Max(band.Top, band.Bottom - (int)Math.Ceiling(Height * DesktopScaling)));
        Position = new PixelPoint(Position.X, top);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_press is null)
        {
            return;
        }

        _press = null;
        if (_dragging)
        {
            _dragging = false;
            SideTileMoved?.Invoke(this, Position.Y);
            return;
        }

        Clicked?.Invoke(this, EventArgs.Empty);
    }
}
