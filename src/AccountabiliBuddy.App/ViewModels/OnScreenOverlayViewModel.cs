using Avalonia.Layout;
using Avalonia;
using AccountabiliBuddy.App.Services;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The small on-screen window: the all-day timeline on top and the session timer under it, either or both shown.
/// They share one spot, size, opacity and Ctrl+Alt drag. While Ctrl+Alt is held a button hides it for a moment.
/// When time is up it glows white for about three seconds. In the evening (sleep mode) a line on top says how
/// much sleep is left if you go to bed now: green from 7 hours, yellow from 5, red below.
/// </summary>
public sealed partial class OnScreenOverlayViewModel : ObservableObject
{
    private static readonly IBrush DragOutline = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));

    // With the mouse over it, it almost disappears, so what's under it can be seen (clicks already pass through).
    private const double HoveredFade = 0.12;

    // The glow rises and falls over the transition set in the window (0.9 s each way), held lit in between.
    private const double GlowBrightness = 0.75;
    private static readonly TimeSpan GlowHold = TimeSpan.FromSeconds(1.4);

    private readonly DispatcherTimer _glowTimer;

    private readonly Action _hideBriefly;
    private readonly Action _backToDefaultSpot;
    private readonly Action _openApp;
    private Action<string>? _useDefaultForApp;
    private string? _appWithOwnSpot;

    /// <param name="hideBriefly">Hides the window for <see cref="OverlaySettings.QuickHideSeconds"/>.</param>
    /// <param name="backToDefaultSpot">Forgets a dragged position, so it goes back to the top-right corner.</param>
    /// <param name="openApp">Opens and focuses the main window (also from the tray).</param>
    public OnScreenOverlayViewModel(
        SessionOverlayViewModel timer, DayBarViewModel day, Action hideBriefly, Action backToDefaultSpot, Action openApp)
    {
        _hideBriefly = hideBriefly;
        _backToDefaultSpot = backToDefaultSpot;
        _openApp = openApp;
        AppDropText = string.Empty;
        KeepText = "Keep this spot";
        UseDefaultText = string.Empty;
        _glowTimer = new DispatcherTimer { Interval = GlowHold };
        _glowTimer.Tick += (_, _) =>
        {
            _glowTimer.Stop();
            GlowOpacity = 0;
        };
        Timer = timer;
        Day = day;
        TimeText = string.Empty;
        HideButtonText = string.Empty;
        DoneText = string.Empty;
        OutlineBrush = Brushes.Transparent;
        PanelOpacity = OverlaySettings.Default.Opacity;
        Scale = 1;
        Fade = 1;
    }

    public SessionOverlayViewModel Timer { get; }

    /// <summary>The timeline's day: layout, next block and how much was focused.</summary>
    public DayBarViewModel Day { get; }

    [ObservableProperty]
    public partial bool ShowsTimer { get; set; }

    [ObservableProperty]
    public partial bool ShowsTimeline { get; set; }

    [ObservableProperty]
    public partial bool ShowsTimelineTime { get; set; }

    /// <summary>E.g. "4:07 PM".</summary>
    [ObservableProperty]
    public partial string TimeText { get; set; }

    /// <summary>The details, shown only while Ctrl+Alt is held.</summary>
    [ObservableProperty]
    public partial bool ShowsDetails { get; set; }

    /// <summary>The noise player's play/pause and volume, shown with the details.</summary>
    [ObservableProperty]
    public partial NoisePlayerViewModel? Noise { get; set; }

    /// <summary>Tonight's wake time with − and +, shown with the details (sleep mode).</summary>
    [ObservableProperty]
    public partial TonightWakeViewModel? Wake { get; set; }

    [ObservableProperty]
    public partial double PanelOpacity { get; set; }

    [ObservableProperty]
    public partial double Scale { get; set; }

    /// <summary>White layer over the window: 0 normally, lit for a moment when time is up.</summary>
    [ObservableProperty]
    public partial double GlowOpacity { get; set; }

    /// <summary>Shown for a few seconds after a session ran out, when nothing else would show.</summary>
    [ObservableProperty]
    public partial bool ShowsDone { get; set; }

    /// <summary>E.g. "Session done" or "Break over · session done".</summary>
    [ObservableProperty]
    public partial string DoneText { get; set; }

    /// <summary>The sleep line (text and colour from <see cref="Day"/>).</summary>
    [ObservableProperty]
    public partial bool ShowsSleep { get; set; }

    /// <summary>While it sits where it was dragged but not kept (shown with Ctrl+Alt held).</summary>
    [ObservableProperty]
    public partial bool IsTemporary { get; set; }

    public bool ShowsTemporary => IsTemporary && ShowsDetails;

    /// <summary>"Keep this spot for brave" or "Keep this spot".</summary>
    [ObservableProperty]
    public partial string KeepText { get; set; }

    partial void OnIsTemporaryChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowsTemporary));
    }

    partial void OnShowsDetailsChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowsTemporary));
        if (value)
        {
            // "Tonight" moves on at each wake time; read it again when the details open.
            Wake?.Refresh();
        }
    }

    /// <summary>Forgets an app's own distances (set by the service).</summary>
    public void SetUseDefaultForApp(Action<string> useDefaultForApp)
    {
        _useDefaultForApp = useDefaultForApp;
    }

    /// <summary>Shown with Ctrl+Alt held while the maximized app in front has its own spot.</summary>
    [ObservableProperty]
    public partial bool ShowsUseDefaultForApp { get; set; }

    /// <summary>E.g. "Use default for brave".</summary>
    [ObservableProperty]
    public partial string UseDefaultText { get; set; }

    [RelayCommand]
    private void UseDefaultForApp()
    {
        if (_appWithOwnSpot is string app)
        {
            _useDefaultForApp?.Invoke(app);
        }
    }

    /// <summary>Shown in the Ctrl+Alt panel when it isn't in its default spot.</summary>
    [ObservableProperty]
    public partial bool ShowsBackToSpot { get; set; }

    /// <summary>E.g. "brave: 140 px down, 60 px left · drag to change", while moved for a maximized app.</summary>
    [ObservableProperty]
    public partial string AppDropText { get; set; }

    public bool ShowsAppDrop => AppDropText.Length > 0;

    partial void OnAppDropTextChanged(string value)
    {
        OnPropertyChanged(nameof(ShowsAppDrop));
    }

    [RelayCommand]
    private void BackToDefaultSpot()
    {
        _backToDefaultSpot();
    }

    /// <summary>Where the window is: its default spot or not, and the maximized app it is slid down for.</summary>
    /// <param name="temporaryForApp">The maximized app a temporary spot was made over, or null.</param>
    /// <param name="appHasOwnSpot">The maximized app in front has its own distances.</param>
    public void SetPlacement(bool inDefaultSpot, string? avoidedApp, int drop, int shift, OverlayCorner corner,
        bool isTemporary = false, string? temporaryForApp = null, bool appHasOwnSpot = false)
    {
        _appWithOwnSpot = appHasOwnSpot && !isTemporary ? avoidedApp : null;
        ShowsUseDefaultForApp = _appWithOwnSpot is not null;
        UseDefaultText = _appWithOwnSpot is null ? string.Empty : $"Use default for {_appWithOwnSpot}";
        IsTemporary = isTemporary;
        KeepText = temporaryForApp is null ? "Keep this spot" : $"Keep this spot for {temporaryForApp}";
        ShowsBackToSpot = !inDefaultSpot && !isTemporary;
        string vertical = corner is OverlayCorner.TopRight or OverlayCorner.TopLeft ? "down" : "up";
        string sideways = corner is OverlayCorner.TopRight or OverlayCorner.BottomRight ? "left" : "right";
        AppDropText = avoidedApp is null || isTemporary ? string.Empty
            : shift > 0 ? $"{avoidedApp}: {drop} px {vertical}, {shift} px {sideways} · drag to change"
            : $"{avoidedApp}: {drop} px {vertical} · drag to change";
    }

    /// <summary>E.g. "Hide 15s".</summary>
    [ObservableProperty]
    public partial string HideButtonText { get; set; }

    /// <summary>1, or nearly 0 while the mouse is over the window (and Ctrl+Alt isn't held).</summary>
    [ObservableProperty]
    public partial double Fade { get; set; }

    /// <summary>Shown while Ctrl+Alt is held, when the window can be dragged.</summary>
    [ObservableProperty]
    public partial IBrush OutlineBrush { get; set; }

    /// <summary>Grid row of the Ctrl+Alt details: 0 = above the bar, 2 = below it.</summary>
    [ObservableProperty]
    public partial int DetailsRow { get; set; } = 2;

    /// <summary>The gap between the bar and the details, on whichever side they open.</summary>
    [ObservableProperty]
    public partial Thickness DetailsMargin { get; set; } = new(0, 8, 0, 0);

    /// <summary>Keeps the bar against the side the window grows away from when the details are wider than it.</summary>
    [ObservableProperty]
    public partial HorizontalAlignment MainAlignment { get; set; } = HorizontalAlignment.Left;

    /// <summary>Where the Ctrl+Alt details open: above the bar (lower half of the screen) or below; and leftward (right half).</summary>
    public void SetDetailsDirection(bool above, bool leftward)
    {
        DetailsRow = above ? 0 : 2;
        DetailsMargin = above ? new Thickness(0, 0, 0, 8) : new Thickness(0, 8, 0, 0);
        MainAlignment = leftward ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    public void SetDragReady(bool ready)
    {
        OutlineBrush = ready ? DragOutline : Brushes.Transparent;
        ShowsDetails = ready;
        Timer.SetDragReady(ready);
    }

    [RelayCommand]
    private void HideBriefly()
    {
        _hideBriefly();
    }

    /// <summary>"Open AccountabiliBuddy" in the Ctrl+Alt details, and Ctrl+Alt + double-click on an empty spot.</summary>
    [RelayCommand]
    private void OpenApp()
    {
        _openApp();
    }

    // Opens today's sync QR codes (ADR 22); null while the QR code method is off.
    private Action? _showSyncQr;

    /// <summary>The "QR" button next to Hide in the Ctrl+Alt details: while the QR code method is on.</summary>
    [ObservableProperty]
    public partial bool ShowsQrButton { get; set; }

    /// <summary>What the QR button opens; null hides it.</summary>
    public void SetSyncQr(Action? show)
    {
        _showSyncQr = show;
        ShowsQrButton = show is not null;
    }

    [RelayCommand]
    private void ShowSyncQr()
    {
        _showSyncQr?.Invoke();
    }

    public void Glow()
    {
        GlowOpacity = GlowBrightness;
        _glowTimer.Stop();
        _glowTimer.Start();
    }

    public void SetHovered(bool hovered)
    {
        Fade = hovered ? HoveredFade : 1;
    }

    /// <param name="doneText">Shown when neither the timer nor the timeline shows; null = nothing to say.</param>
    public void Update(bool showsTimer, bool showsTimeline, bool showsSleep, string? doneText, DateTime localNow, OverlaySettings settings)
    {
        ShowsTimer = showsTimer;
        ShowsTimeline = showsTimeline;
        ShowsSleep = showsSleep;
        ShowsDone = !showsTimer && !showsTimeline && doneText is not null;
        DoneText = doneText ?? string.Empty;
        ShowsTimelineTime = settings.TimelineShowsTime;
        TimeText = $"{localNow:t}";
        HideButtonText = $"Hide {settings.QuickHideSeconds}s";
        PanelOpacity = settings.Opacity;
        Scale = settings.Size switch
        {
            OverlaySize.Small => 0.8,
            OverlaySize.Large => 1.35,
            _ => 1,
        };
    }
}
