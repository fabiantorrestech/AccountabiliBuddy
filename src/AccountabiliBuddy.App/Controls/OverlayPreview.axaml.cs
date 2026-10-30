using System.ComponentModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Ports;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Material.Icons;
using Material.Icons.Avalonia;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The live preview beside the overlay settings (see <see cref="OverlayPreviewViewModel"/>): a small drawn desktop for the chosen scene, in the
/// theme's own colours. The tile in it is the real <see cref="OverlayTileView"/> with sample text, put where <see cref="OverlayPlacementCalculator"/>
/// puts it for a mock taskbar, so the preview obeys the same rules as the real overlay. Everything is drawn in a 2560 x 1440 mock screen at 100%
/// and cropped to the part that matters; it redraws only when a setting, a tab or the page's visibility changes (no timers, no polling).
/// </summary>
public partial class OverlayPreview : UserControl
{
    public static readonly StyledProperty<bool> IsStripProperty = AvaloniaProperty.Register<OverlayPreview, bool>(nameof(IsStrip));

    // The mock screen (device-independent pixels at 100%): a 2560 x 1440 desktop with a bottom taskbar, wide enough that the free stretch
    // between the app icons and the tray holds a tile the way a real taskbar does.
    private const double ScreenWidth = 2560;
    private const double ScreenHeight = 1440;
    private const double BarHeight = 48;
    private const double TrayWidth = 200;
    private const double IconSlot = 44;
    private const int IconCount = 5;
    private const double MaxCropHeight = 170;
    private const double MinCropHeight = 84;
    private const double MinCropWidth = 480;

    // Mirrors TaskbarOverlayWindow.HeightFor and Reposition (heights, the rise of the Large size, the floating margins).
    private const double SmallHeight = 36;
    private const double FloatingHeight = 56;
    private const double LargeRise = 36;

    private OverlayPreviewViewModel? _model;
    private bool _redrawing;
    private bool _wasVisible;

    static OverlayPreview()
    {
        IsStripProperty.Changed.AddClassHandler<OverlayPreview>((preview, _) => preview.Redraw());
    }

    public OverlayPreview()
    {
        InitializeComponent();
        FoldButton.Click += (_, _) =>
        {
            if (_model is not null)
            {
                _model.IsCollapsed = !_model.IsCollapsed;
            }
        };
        DataContextChanged += (_, _) => Attach(DataContext as OverlayPreviewViewModel);
        AttachedToVisualTree += (_, _) => Redraw();
        // The page was hidden while settings changed (or a theme was picked meanwhile): draw it again the moment it shows. A layout pass is the
        // first place that sees an ancestor become visible; this only compares a flag, and passes only happen when something changed.
        LayoutUpdated += (_, _) =>
        {
            bool visible = IsEffectivelyVisible;
            if (visible != _wasVisible)
            {
                _wasVisible = visible;
                if (visible)
                {
                    Redraw();
                }
            }
        };
    }

    /// <summary>The narrow layout: a short strip above the settings (tabs and scene, with a fold arrow) instead of the side pane.</summary>
    public bool IsStrip
    {
        get => GetValue(IsStripProperty);
        set => SetValue(IsStripProperty, value);
    }

    private void Attach(OverlayPreviewViewModel? model)
    {
        if (_model is not null)
        {
            _model.Changed -= OnModelChanged;
            _model.PropertyChanged -= OnModelPropertyChanged;
        }

        _model = model;
        if (_model is not null)
        {
            _model.Changed += OnModelChanged;
            _model.PropertyChanged += OnModelPropertyChanged;
        }

        Redraw();
    }

    private void OnModelChanged(object? sender, EventArgs e) => Redraw();

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayPreviewViewModel.IsCollapsed))
        {
            Redraw();
        }
    }

    private void Redraw()
    {
        if (_model is null || !IsEffectivelyVisible || _redrawing)
        {
            return;
        }

        _redrawing = true;
        try
        {
            UpdateChrome(_model);
            Scene.Children.Clear();
            if (IsStrip && _model.IsCollapsed)
            {
                return;
            }

            _model.SessionTile.RefreshPreviewBrushes();
            _model.IdleTile.RefreshPreviewBrushes();
            switch (_model.Scene)
            {
                case OverlayPreviewScene.Taskbar:
                    DrawTaskbarScene(_model, hidden: false);
                    break;
                case OverlayPreviewScene.AutoHide:
                    DrawTaskbarScene(_model, hidden: true);
                    break;
                case OverlayPreviewScene.FullScreen:
                    DrawFullScreenScene(_model);
                    break;
                default:
                    DrawPanelScene(_model);
                    break;
            }

            Caption.Text = CaptionFor(_model);
        }
        finally
        {
            _redrawing = false;
        }
    }

    private void UpdateChrome(OverlayPreviewViewModel model)
    {
        bool folded = IsStrip && model.IsCollapsed;
        TitleText.IsVisible = !IsStrip || folded;
        Controls.IsVisible = !folded;
        FoldButton.IsVisible = IsStrip;
        FoldIcon.Kind = folded ? MaterialIconKind.ChevronRight : MaterialIconKind.ChevronDown;
        IconsLeftBox.IsVisible = !IsStrip && model.Scene is OverlayPreviewScene.Taskbar or OverlayPreviewScene.AutoHide;
        StageFrame.IsVisible = !folded;
        StageBox.MaxHeight = IsStrip ? 76 : 260;
        Caption.IsVisible = !IsStrip;
    }

    // ---- Scenes ----

    /// <summary>The tile on the mock taskbar (or what the auto-hide choice does while the taskbar is hidden).</summary>
    private void DrawTaskbarScene(OverlayPreviewViewModel model, bool hidden)
    {
        OverlaySettings settings = model.Settings;
        TaskbarInfo info = MockTaskbar(model.IconsLeft, hidden);
        OverlayTarget target = OverlayPlacementCalculator.Resolve(settings, [info]);
        DrawDesktop();
        if (hidden)
        {
            // The taskbar is slid away: only a hairline at the bottom edge shows where it is.
            Fill(0, ScreenHeight - 2, ScreenWidth, 2, "Ab.Line");
        }
        else
        {
            DrawTaskbar(model);
        }

        switch (target.Kind)
        {
            case OverlayTargetKind.SlimLine:
                DrawSlimLine(model);
                SetCrop(MinCropWidth, anchorRight: true, atTop: false, height: MinCropHeight);
                break;
            case OverlayTargetKind.Hidden:
                SetCrop(MinCropWidth, anchorRight: true, atTop: false, height: MinCropHeight);
                break;
            default:
                DrawTile(model, target, settings, model.IconsLeft);
                break;
        }
    }

    private void DrawTile(OverlayPreviewViewModel model, OverlayTarget target, OverlaySettings settings, bool iconsLeft)
    {
        double height = TileHeight(target, settings);
        var tile = new OverlayTileView
        {
            DataContext = model.Tile,
            Height = height,
            MaxWidth = target.Kind == OverlayTargetKind.Docked ? Math.Max(120, target.Band.Width / target.Scale) : 420,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsHitTestVisible = false,
            Focusable = false,
            Opacity = 0,
        };
        tile.Apply(target, settings, height);
        // The tile is always dark, as the real overlay window is, whatever theme the page is in.
        var scope = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark, Child = tile, IsHitTestVisible = false };
        Scene.Children.Add(scope);
        SetCrop(MinCropWidth, anchorRight: target.AlignRight, atTop: false, height: MinCropHeight);
        // Its width is known after layout; then it goes where the calculator says and the crop widens to take it in.
        tile.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width <= 0)
            {
                return;
            }

            (double x, double y) = TilePosition(target, settings, e.NewSize.Width, height);
            Canvas.SetLeft(scope, x);
            Canvas.SetTop(scope, y);
            double extent = target.AlignRight ? ScreenWidth - x : x + e.NewSize.Width;
            double rightInset = 0;
            if (target.Kind == OverlayTargetKind.Docked)
            {
                if (target.AlignRight && !target.CoversTray)
                {
                    // Against the tray: the crop ends after the first of its icons instead of at the screen edge.
                    rightInset = TrayWidth - 110;
                    extent -= rightInset;
                }
                else if (!target.AlignRight && !iconsLeft)
                {
                    // At the left end of a taskbar with centred icons: take the first icons in too, when that doesn't make the picture tiny.
                    double withIcons = FirstIconLeft(iconsLeft) + (IconSlot * 2);
                    if (withIcons <= 900)
                    {
                        extent = Math.Max(extent, withIcons);
                    }
                }
            }

            extent += 30;
            // Only as tall as the tile needs above the taskbar.
            SetCrop(extent, target.AlignRight, atTop: false, height: ScreenHeight - y + 14, rightInset: rightInset);
            tile.Opacity = 1;
        };
    }

    private void DrawSlimLine(OverlayPreviewViewModel model)
    {
        // A 3 px progress line along the bottom edge with the minutes at the right (SlimLineWindow). Drawn over the width the crop shows,
        // as if the screen were that wide, so the line and its fill can be read.
        double left = ScreenWidth - MinCropWidth;
        Fill(left, ScreenHeight - 3, MinCropWidth, 3, "Ab.Surface2");
        if (!model.ShowsSession)
        {
            return;
        }

        Fill(left, ScreenHeight - 3, MinCropWidth * (1 - model.Tile.Fraction), 3, "Ab.Accent");
        var pill = new Border { Padding = new Thickness(8, 2), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = Label(model.Tile.MinutesText, 12, "Ab.Ink", bold: true) };
        Themed(pill, Border.BackgroundProperty, "Ab.Surface");
        Themed(pill, Border.BorderBrushProperty, "Ab.Line");
        Scene.Children.Add(Placed(pill, ScreenWidth - 80, ScreenHeight - 33));
    }

    private void DrawFullScreenScene(OverlayPreviewViewModel model)
    {
        OverlaySettings settings = model.Settings;
        // A dark video frame, whatever theme the page is in.
        Scene.Children.Add(Placed(new Border { Width = ScreenWidth, Height = ScreenHeight, Background = new SolidColorBrush(Color.Parse("#0C0E11")) }, 0, 0));
        SetCrop(MinCropWidth, anchorRight: true, atTop: true, height: MinCropHeight);
        if (!settings.CornerClock || settings.CornerClockParts == CornerClockParts.None)
        {
            return;
        }

        var parts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        TaskbarOverlayViewModel tile = model.Tile;
        bool first = true;
        void Part(Control part)
        {
            if (!first)
            {
                var divider = new Border { Width = 1, Height = 12, VerticalAlignment = VerticalAlignment.Center };
                Themed(divider, Border.BackgroundProperty, "Ab.Line");
                parts.Children.Add(divider);
            }

            parts.Children.Add(part);
            first = false;
        }

        if (settings.CornerClockParts.HasFlag(CornerClockParts.Session) && model.ShowsSession)
        {
            var session = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var ring = new ProgressRing { Width = 14, Height = 14, Value = tile.RingValue, Thickness = 2.5, TrackBrush = OverlayBrushes.Get("Surface2"), ArcBrush = tile.RingBrush };
            session.Children.Add(ring);
            session.Children.Add(Label(tile.MinutesText, 12, "Ab.Ink", bold: true));
            Part(session);
        }

        if (settings.CornerClockParts.HasFlag(CornerClockParts.Time))
        {
            Part(Label(tile.ClockText, 12, "Ab.Ink", bold: false));
        }

        if (settings.CornerClockParts.HasFlag(CornerClockParts.Date))
        {
            Part(Label(tile.DateText, 12, "Ab.Muted", bold: false));
        }

        if (settings.CornerClockParts.HasFlag(CornerClockParts.Battery))
        {
            var battery = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            var icon = new MaterialIcon { Kind = MaterialIconKind.Battery, Width = 12, Height = 12 };
            Themed(icon, TemplatedControl.ForegroundProperty, "Ab.Muted");
            battery.Children.Add(icon);
            battery.Children.Add(Label("82%", 12, "Ab.Ink", bold: false));
            Part(battery);
        }

        if (parts.Children.Count == 0)
        {
            return;
        }

        // 11b: top right, 26 px tall, one rounded inner corner (bottom left), about 86% opaque.
        var pill = new Border
        {
            Height = 26,
            Padding = new Thickness(10, 0),
            CornerRadius = new CornerRadius(0, 0, 0, 8),
            BorderThickness = new Thickness(1, 0, 0, 1),
            Opacity = 0.86,
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = parts,
        };
        // The pill is always dark, as the real corner clock window is.
        pill.Background = OverlayBrushes.Get("Surface");
        pill.BorderBrush = OverlayBrushes.Get("Line");
        var row = new Grid { Width = ScreenWidth, Height = 26, Children = { pill } };
        Scene.Children.Add(Placed(row, 0, 0));
    }

    private void DrawPanelScene(OverlayPreviewViewModel model)
    {
        OverlaySettings settings = model.Settings;
        TaskbarOverlayViewModel tile = model.Tile;
        double width = settings.PanelSize switch { PanelSize.Compact => 320, PanelSize.Large => 760, _ => 400 };
        const double height = 300;
        Fill(0, 0, width, height, "Ab.Bg");
        var card = new Border { Width = width - 16, Height = height - 16, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(18, 16) };
        Themed(card, Border.BackgroundProperty, "Ab.Surface");
        Themed(card, Border.BorderBrushProperty, "Ab.Line");

        var body = new StackPanel { Spacing = 12 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        header.Children.Add(new ProgressRing { Width = 40, Height = 40, Value = tile.RingValue, Thickness = 5, TrackBrush = OverlayBrushes.Get("Surface2"), ArcBrush = tile.RingBrush });
        var headerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        headerText.Children.Add(Label(model.ShowsSession ? "18 min left · " + tile.EndsText : "Nothing running", 15, "Ab.Ink", bold: true));
        headerText.Children.Add(Label(model.ShowsSession ? "QS - Default" : tile.Line2, 12, "Ab.Muted", bold: false));
        header.Children.Add(headerText);
        body.Children.Add(header);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (string text in model.ShowsSession ? new[] { "Done", "Pause", "+ 10 min" } : new[] { "Start", "Lunch" })
        {
            var button = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 6), Child = Label(text, 12, "Ab.Ink", bold: false) };
            Themed(button, Border.BackgroundProperty, "Ab.Surface2");
            buttons.Children.Add(button);
        }

        body.Children.Add(buttons);
        body.Children.Add(Label("Goal · 2m of 1h", 12, "Ab.Muted", bold: false));
        body.Children.Add(Label("Park a thought…", 12, "Ab.Muted", bold: false));

        var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var lockIcon = new MaterialIcon { Kind = settings.PeekKeysLocked ? MaterialIconKind.LockOutline : MaterialIconKind.LockOpenVariantOutline, Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        Themed(lockIcon, TemplatedControl.ForegroundProperty, "Ab.Muted");
        keys.Children.Add(lockIcon);
        keys.Children.Add(Label(settings.PeekKeysLocked ? "A peek takes no keys" : "A peek takes hide, +10 min, up and down", 12, "Ab.Muted", bold: false));
        body.Children.Add(keys);
        body.Children.Add(Label(settings.HoldToPeek ? "Hold Ctrl+Alt to peek · opens after " + PeekDelayText(settings.PeekDelayMs) : "Hold-to-peek is off", 12, "Ab.Muted", bold: false));
        body.Children.Add(Label(settings.PanelSticky ? "Ctrl+Alt+Space keeps it open" : "Ctrl+Alt+Space is off", 12, "Ab.Muted", bold: false));
        card.Child = body;
        Scene.Children.Add(Placed(card, 8, 8));
        SetCropBox(0, 0, width, height);
    }

    private static string PeekDelayText(int ms) => ms == 0 ? "no delay" : ms + " ms";

    // ---- Mock desktop pieces ----

    private void DrawDesktop()
    {
        Fill(0, 0, ScreenWidth, ScreenHeight, "Ab.Bg");
        // A faint app window, so the desktop doesn't look empty.
        var window = new Border { Width = 900, Height = 560, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Opacity = 0.9 };
        Themed(window, Border.BackgroundProperty, "Ab.Surface");
        Themed(window, Border.BorderBrushProperty, "Ab.Line");
        Scene.Children.Add(Placed(window, 830, 300));
        Fill(860, 330, 240, 10, "Ab.Surface2", 5);
        Fill(860, 366, 620, 8, "Ab.Surface2", 4);
        Fill(860, 390, 520, 8, "Ab.Surface2", 4);
        Fill(860, 414, 580, 8, "Ab.Surface2", 4);
    }

    private void DrawTaskbar(OverlayPreviewViewModel model)
    {
        double top = ScreenHeight - BarHeight;
        Fill(0, top, ScreenWidth, BarHeight, "Ab.Surface");
        Fill(0, top, ScreenWidth, 1, "Ab.Line");
        double x = FirstIconLeft(model.IconsLeft);
        for (int i = 0; i < IconCount; i++)
        {
            Border icon = Fill(x + (i * IconSlot), top + 10, 28, 28, i == 0 ? "Ab.Muted" : "Ab.Faint", 8);
            icon.Opacity = i == 0 ? 0.6 : 0.45;
        }

        Fill(x + 9, top + 42, 10, 3, "Ab.Accent", 1.5);

        double trayLeft = ScreenWidth - TrayWidth;
        AddIcon(MaterialIconKind.ChevronUp, trayLeft + 14, top + 16);
        AddIcon(MaterialIconKind.Wifi, trayLeft + 44, top + 16);
        AddIcon(MaterialIconKind.VolumeHigh, trayLeft + 72, top + 16);
        var clock = new StackPanel { Width = 80 };
        clock.Children.Add(Label(model.Tile.ClockText, 12, "Ab.Ink", bold: false, right: true));
        clock.Children.Add(Label(model.Tile.DateText, 10, "Ab.Muted", bold: false, right: true));
        Scene.Children.Add(Placed(clock, ScreenWidth - 90, top + 6));
    }

    private double FirstIconLeft(bool iconsLeft) => iconsLeft ? 12 : ((ScreenWidth - (IconCount * IconSlot)) / 2) + 8;

    /// <summary>The mock taskbar as the Windows adapter would report it: 100% scaling, along the bottom, with the icons centred or at the left.</summary>
    private TaskbarInfo MockTaskbar(bool iconsLeft, bool hidden)
    {
        var monitor = new PixelBox(0, 0, (int)ScreenWidth, (int)ScreenHeight);
        int top = (int)(ScreenHeight - BarHeight);
        var bar = new PixelBox(0, top, (int)ScreenWidth, (int)ScreenHeight);
        var tray = new PixelBox((int)(ScreenWidth - TrayWidth), top, (int)ScreenWidth, (int)ScreenHeight);
        int iconsStart = (int)FirstIconLeft(iconsLeft) - 8;
        int iconsEnd = iconsStart + (int)(IconCount * IconSlot);
        var free = new PixelBox(iconsEnd, top, tray.Left, (int)ScreenHeight);
        PixelBox? leftFree = iconsLeft ? null : new PixelBox(0, top, iconsStart, (int)ScreenHeight);
        return new TaskbarInfo(
            "preview", true, monitor, hidden ? monitor : new PixelBox(0, 0, (int)ScreenWidth, top), 1.0, TaskbarEdge.Bottom,
            hidden ? new PixelBox(0, (int)ScreenHeight - 2, (int)ScreenWidth, (int)ScreenHeight) : bar, hidden, !hidden, free, tray, leftFree, iconsLeft);
    }

    private static double TileHeight(OverlayTarget target, OverlaySettings settings)
    {
        double bar = target.TaskbarHeight > 0 ? target.TaskbarHeight / target.Scale : FloatingHeight;
        return settings.Size switch
        {
            OverlaySize.Small => SmallHeight,
            OverlaySize.Large => bar + LargeRise,
            _ => bar,
        };
    }

    private static (double X, double Y) TilePosition(OverlayTarget target, OverlaySettings settings, double width, double height)
    {
        PixelBox band = target.Band;
        if (target.Kind == OverlayTargetKind.Docked)
        {
            double x = target.AlignRight ? band.Right - width : band.Left;
            double y = settings.Size == OverlaySize.Small ? band.Top + ((target.TaskbarHeight - height) / 2)
                : settings.Size == OverlaySize.Large ? band.Top - LargeRise
                : band.Top;
            return (x, y);
        }

        (int side, int bottom) = OverlayPlacementCalculator.FloatingMargins(1.0);
        return (target.AlignRight ? band.Right - width - side : band.Left + side, band.Bottom - height - bottom);
    }

    // ---- Cropping ----

    /// <summary>Shows only the part of the mock screen that matters: the bottom (or top) strip, as wide as needed, against the right or left edge.</summary>
    private void SetCrop(double extent, bool anchorRight, bool atTop, double height = MaxCropHeight, double rightInset = 0)
    {
        double width = Math.Clamp(Math.Max(MinCropWidth, extent), MinCropWidth, ScreenWidth);
        height = Math.Clamp(height, MinCropHeight, MaxCropHeight);
        SetCropBox(anchorRight ? ScreenWidth - rightInset - width : 0, atTop ? 0 : ScreenHeight - height, width, height);
    }

    private void SetCropBox(double x, double y, double width, double height)
    {
        Scene.Width = ScreenWidth;
        Scene.Height = ScreenHeight;
        Scene.RenderTransform = new TranslateTransform(-x, -y);
        Crop.Width = width;
        Crop.Height = height;
    }

    // ---- Small drawing helpers ----

    private Border Fill(double x, double y, double width, double height, string key, double radius = 0)
    {
        var rect = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(radius) };
        Themed(rect, Border.BackgroundProperty, key);
        Scene.Children.Add(Placed(rect, x, y));
        return rect;
    }

    private static T Placed<T>(T control, double x, double y)
        where T : Control
    {
        Canvas.SetLeft(control, x);
        Canvas.SetTop(control, y);
        control.IsHitTestVisible = false;
        return control;
    }

    private TextBlock Label(string text, double size, string key, bool bold, bool right = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center };
        if (right)
        {
            block.TextAlignment = TextAlignment.Right;
        }

        Themed(block, TextBlock.ForegroundProperty, key);
        return block;
    }

    private void AddText(string text, double x, double y, double size, string key, bool bold)
    {
        Scene.Children.Add(Placed(Label(text, size, key, bold), x, y));
    }

    private void AddIcon(MaterialIconKind kind, double x, double y)
    {
        var icon = new MaterialIcon { Kind = kind, Width = 16, Height = 16 };
        Themed(icon, TemplatedControl.ForegroundProperty, "Ab.Muted");
        Scene.Children.Add(Placed(icon, x, y));
    }

    /// <summary>Paints a property from a theme token and keeps it following the theme.</summary>
    private void Themed(AvaloniaObject target, AvaloniaProperty property, string key)
    {
        target.Bind(property, this.GetResourceObservable(key));
    }

    private static string CaptionFor(OverlayPreviewViewModel model)
    {
        OverlaySettings settings = model.Settings;
        string state = model.ShowsSession ? "with a session running" : "with nothing running";
        switch (model.Scene)
        {
            case OverlayPreviewScene.Taskbar:
                string placement = OverlaySettingsViewModel.PlacementNames[(int)settings.EffectivePlacement];
                string shape = settings.EffectivePlacement is OverlayPlacement.FloatBottomRight or OverlayPlacement.FloatBottomLeft
                    ? ", " + OverlaySettingsViewModel.ShapeNames[(int)settings.Shape].ToLowerInvariant()
                    : string.Empty;
                return $"{placement} · {OverlaySettingsViewModel.SizeNames[(int)settings.Size]} · {OverlaySettingsViewModel.ProgressNames[(int)settings.Progress].ToLowerInvariant()}{shape}, {state}. The tile is the real one, with sample text.";
            case OverlayPreviewScene.AutoHide:
                return "While your taskbar hides itself: " + OverlaySettingsViewModel.AutoHideNames[(int)settings.AutoHide].ToLowerInvariant() + ".";
            case OverlayPreviewScene.FullScreen:
                return settings.CornerClock && settings.CornerClockParts != CornerClockParts.None
                    ? "Over a full-screen app, top right: " + string.Join(", ", PartNames(settings.CornerClockParts)) + ". Clicks pass through it."
                    : "The corner clock is off.";
            default:
                return $"The Ctrl+Alt panel, {OverlaySettingsViewModel.PanelSizeNames[(int)settings.PanelSize].ToLowerInvariant()} width.";
        }
    }

    private static IEnumerable<string> PartNames(CornerClockParts parts)
    {
        if (parts.HasFlag(CornerClockParts.Session))
        {
            yield return "session";
        }

        if (parts.HasFlag(CornerClockParts.Time))
        {
            yield return "time";
        }

        if (parts.HasFlag(CornerClockParts.Date))
        {
            yield return "date";
        }

        if (parts.HasFlag(CornerClockParts.Battery))
        {
            yield return "battery";
        }
    }
}