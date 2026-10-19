using AccountabiliBuddy.App.Services;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The small on-screen window: the all-day timeline on top and the session timer under it, either or both shown.
/// They share one spot, size, opacity and Ctrl+Alt drag. While Ctrl+Alt is held a button hides it for a moment.
/// When time is up it glows white for about three seconds.
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

    /// <param name="hideBriefly">Hides the window for <see cref="OverlaySettings.QuickHideSeconds"/>.</param>
    public OnScreenOverlayViewModel(SessionOverlayViewModel timer, DayBarViewModel day, Action hideBriefly)
    {
        _hideBriefly = hideBriefly;
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

    /// <summary>E.g. "Hide 15s".</summary>
    [ObservableProperty]
    public partial string HideButtonText { get; set; }

    /// <summary>1, or nearly 0 while the mouse is over the window (and Ctrl+Alt isn't held).</summary>
    [ObservableProperty]
    public partial double Fade { get; set; }

    /// <summary>Shown while Ctrl+Alt is held, when the window can be dragged.</summary>
    [ObservableProperty]
    public partial IBrush OutlineBrush { get; set; }

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
    public void Update(bool showsTimer, bool showsTimeline, string? doneText, DateTime localNow, OverlaySettings settings)
    {
        ShowsTimer = showsTimer;
        ShowsTimeline = showsTimeline;
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
