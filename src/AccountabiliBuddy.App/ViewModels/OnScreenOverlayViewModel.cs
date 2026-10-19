using AccountabiliBuddy.App.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The small on-screen window: the all-day timeline on top and the session timer under it, either or both shown.
/// They share one spot, size, opacity and Ctrl+Alt drag.
/// </summary>
public sealed partial class OnScreenOverlayViewModel : ObservableObject
{
    private static readonly IBrush DragOutline = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));

    public OnScreenOverlayViewModel(SessionOverlayViewModel timer, DayBarViewModel day)
    {
        Timer = timer;
        Day = day;
        TimeText = string.Empty;
        OutlineBrush = Brushes.Transparent;
        PanelOpacity = OverlaySettings.Default.Opacity;
        Scale = 1;
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

    /// <summary>Shown while Ctrl+Alt is held, when the window can be dragged.</summary>
    [ObservableProperty]
    public partial IBrush OutlineBrush { get; set; }

    public void SetDragReady(bool ready)
    {
        OutlineBrush = ready ? DragOutline : Brushes.Transparent;
        ShowsDetails = ready;
        Timer.SetDragReady(ready);
    }

    public void Update(bool showsTimer, bool showsTimeline, DateTime localNow, OverlaySettings settings)
    {
        ShowsTimer = showsTimer;
        ShowsTimeline = showsTimeline;
        ShowsTimelineTime = settings.TimelineShowsTime;
        TimeText = $"{localNow:t}";
        PanelOpacity = settings.Opacity;
        Scale = settings.Size switch
        {
            OverlaySize.Small => 0.8,
            OverlaySize.Large => 1.35,
            _ => 1,
        };
    }
}
