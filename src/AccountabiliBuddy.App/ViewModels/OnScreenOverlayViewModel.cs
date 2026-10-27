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

    // Quiet mode at rest in a session: the timer is no more than this opaque (outside a session the timeline's own opacity shows).
    private const double QuietOpacity = 0.6;

    private OverlaySettings _settings = OverlaySettings.Default;
    private bool _showsTimer;

    // No session runs: the timeline's own opacity pair applies instead of the timer's.
    private bool _noSession;
    private Action? _stepAway;

    // The glow rises and falls over the transition set in the window (0.9 s each way), held lit in between.
    private const double GlowBrightness = 0.75;
    private static readonly TimeSpan GlowHold = TimeSpan.FromSeconds(1.4);

    // After Ctrl+Alt is let go, full colour stays this long before going quiet again.
    private static readonly TimeSpan QuietAfterDetails = TimeSpan.FromSeconds(3);

    private readonly DispatcherTimer _glowTimer;
    private readonly DispatcherTimer _settleTimer;

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
        _settleTimer = new DispatcherTimer { Interval = QuietAfterDetails };
        _settleTimer.Tick += (_, _) =>
        {
            _settleTimer.Stop();
            ApplyQuiet();
        };
        _glowTimer = new DispatcherTimer { Interval = GlowHold };
        _glowTimer.Tick += (_, _) =>
        {
            _glowTimer.Stop();
            GlowOpacity = 0;
            ApplyQuiet();
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
        OnPropertyChanged(nameof(SubtaskMaxLines));
        OnPropertyChanged(nameof(TaskMaxLines));
        ApplyQuiet();
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
        bool wasShowing = ShowsDetails;
        ShowsDetails = ready;
        Timer.SetDragReady(ready);
        if (wasShowing && !ready)
        {
            _settleTimer.Stop();
            _settleTimer.Start();
        }

        ApplyQuiet();
    }

    /// <summary>Muted at rest (quiet mode): not while Ctrl+Alt is held, in a phase's last 2 minutes, glowing or saying "done".</summary>
    [ObservableProperty]
    public partial bool IsQuiet { get; set; }

    /// <summary>At rest the timeline's marks and now line are dulled too (<see cref="OverlaySettings.QuietMarks"/>).</summary>
    [ObservableProperty]
    public partial bool QuietMarks { get; set; }

    /// <summary>"Away · 4:12" while the user stepped away.</summary>
    [ObservableProperty]
    public partial string AwayText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowsAway { get; set; }

    /// <summary>The Ctrl+Alt button: "Step away", or "I'm back" while away.</summary>
    [ObservableProperty]
    public partial string StepAwayText { get; set; } = "Step away";

    /// <summary>While a session runs and Step away is wired.</summary>
    [ObservableProperty]
    public partial bool ShowsStepAway { get; set; }

    private Action? _ready;

    /// <summary>"Planning · 4:12 left" during a session's planning minutes, else empty.</summary>
    [ObservableProperty]
    public partial string PlanningText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowsPlanning { get; set; }

    /// <summary>"I'm ready" shows (not while a first session learns).</summary>
    [ObservableProperty]
    public partial bool ShowsReady { get; set; }

    /// <summary>Today's deep work against the goal, in the Ctrl+Alt details ("Goal · 40m of 1h"); empty hides it.</summary>
    [ObservableProperty]
    public partial string GoalText { get; set; } = string.Empty;

    /// <summary>Plan first: the line on the bar and what "I'm ready" does (set by the service).</summary>
    public void SetPlanning(string? text, Action? ready)
    {
        PlanningText = text ?? string.Empty;
        ShowsPlanning = text is not null;
        ShowsReady = text is not null && ready is not null;
        _ready = ready;
    }

    [RelayCommand]
    private void Ready()
    {
        _ready?.Invoke();
    }

    /// <summary>What the Step away button does (set by the service): step away, or back while away.</summary>
    public void SetStepAway(Action toggle)
    {
        _stepAway = toggle;
    }

    /// <param name="awayText">"Away · 4:12" while away, null when not.</param>
    /// <param name="sessionActive">The button shows only while a session runs.</param>
    public void SetAway(string? awayText, bool sessionActive = true)
    {
        AwayText = awayText ?? string.Empty;
        ShowsAway = awayText is not null;
        StepAwayText = awayText is null ? "Step away" : "I'm back";
        ShowsStepAway = _stepAway is not null && sessionActive;
    }

    [RelayCommand]
    private void StepAway()
    {
        _stepAway?.Invoke();
    }

    /// <summary>This PC's timers and alarms, a line each under the timer and timeline (set by the service).</summary>
    [ObservableProperty]
    public partial OverlayTimersViewModel? Timers { get; set; }

    /// <summary>While a timer runs, is paused or is up, or an alarm rings within 12 hours.</summary>
    [ObservableProperty]
    public partial bool ShowsTimers { get; set; }

    /// <summary>Reads the timers again; true when any line shows (the window then shows even with nothing else).</summary>
    public bool RefreshTimers()
    {
        if (Timers is null)
        {
            return false;
        }

        Timers.Refresh(IsQuiet, _settings.FillUp);
        ShowsTimers = Timers.HasLines;
        return ShowsTimers;
    }

    private void ApplyQuiet()
    {
        bool loud = (_showsTimer && Timer.IsLoud) || GlowOpacity > 0 || ShowsDone;
        IsQuiet = _settings.Quiet && !ShowsDetails && !_settleTimer.IsEnabled && !loud;
        Timer.SetQuiet(IsQuiet);
        RefreshTimers();
        RefreshTaskLine();
        QuietMarks = _settings.QuietMarks;
        if (_noSession)
        {
            // The timeline's own pair; quiet mode only turns its colours slate (IsQuiet), never caps its opacity.
            PanelOpacity = ShowsDetails ? Math.Clamp(_settings.TimelineHeldOpacity, 0.2, 1) : _settings.TimelineOpacity;
            return;
        }

        PanelOpacity = ShowsDetails ? Math.Clamp(_settings.HeldOpacity, 0.2, 1)
            : IsQuiet ? Math.Min(_settings.Opacity, QuietOpacity)
            : _settings.Opacity;
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
    /// <summary>Guided sessions: the running session's checklist (ticked here with Ctrl+Alt held).</summary>
    [ObservableProperty]
    public partial ChecklistViewModel? Checklist { get; set; }

    /// <summary>While a session with tasks runs.</summary>
    [ObservableProperty]
    public partial bool ShowsTasks { get; set; }

    /// <summary>One line, e.g. "2/5 ▸ Fix the failing tests": the count first, so a long name cuts off its end, not the count.</summary>
    [ObservableProperty]
    public partial string TaskLine { get; set; } = string.Empty;

    /// <summary>The task's subtask shown under it, e.g. "↳ Integration tests"; empty when it has none.</summary>
    [ObservableProperty]
    public partial string SubtaskLine { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowsSubtaskLine { get; set; }

    /// <summary>The colour of the task line's category (a dot before it).</summary>
    [ObservableProperty]
    public partial IBrush TaskCategoryBrush { get; set; } = Brushes.Transparent;

    /// <summary>The task the line shows is in a category.</summary>
    [ObservableProperty]
    public partial bool ShowsTaskCategory { get; set; }

    /// <summary>Up to 2 lines, then "…"; the whole name while Ctrl+Alt is held (0 = no limit).</summary>
    public int SubtaskMaxLines => ShowsDetails ? 0 : 2;

    /// <summary>One line, then "…"; the whole task while Ctrl+Alt is held (0 = no limit).</summary>
    public int TaskMaxLines => ShowsDetails ? 0 : 1;

    /// <summary>The task line shows: tasks exist, and Ctrl+Alt is held when it's set to show only then.</summary>
    [ObservableProperty]
    public partial bool TaskLineShown { get; set; }

    private void RefreshTaskLine()
    {
        bool onlyWithCtrlAlt = _settings.TaskOnlyWithCtrlAlt;
        TaskLineShown = ShowsTasks && (!onlyWithCtrlAlt || ShowsDetails);
    }

    /// <summary>The checklist and whether a session runs: the task line and the details follow.</summary>
    public void RefreshTasks(bool sessionActive)
    {
        ChecklistViewModel? checklist = Checklist;
        ShowsTasks = sessionActive && checklist is { HasTasks: true };
        TaskLine = checklist is null ? string.Empty : $"{checklist.ProgressText} ▸ {checklist.CurrentTaskText}";
        SubtaskLine = checklist is { CurrentSubtaskText.Length: > 0 } ? $"↳ {checklist.CurrentSubtaskText}" : string.Empty;
        ShowsSubtaskLine = ShowsTasks && SubtaskLine.Length > 0;
        TaskCategoryBrush = checklist?.CurrentCategoryBrush ?? Brushes.Transparent;
        ShowsTaskCategory = checklist is { HasCurrentCategory: true };
        RefreshTaskLine();
        checklist?.RefreshEndAtOnce();
    }

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
        ApplyQuiet();
        _glowTimer.Stop();
        _glowTimer.Start();
    }

    public void SetHovered(bool hovered)
    {
        Fade = hovered ? HoveredFade : 1;
    }

    /// <param name="doneText">Shown when neither the timer nor the timeline shows; null = nothing to say.</param>
    /// <param name="sessionActive">False uses the timeline's own opacity (<see cref="OverlaySettings.TimelineOpacity"/>).</param>
    public void Update(
        bool showsTimer, bool showsTimeline, bool showsSleep, string? doneText, DateTime localNow, OverlaySettings settings, bool sessionActive)
    {
        ShowsTimer = showsTimer;
        ShowsTimeline = showsTimeline;
        ShowsSleep = showsSleep;
        ShowsDone = !showsTimer && !showsTimeline && doneText is not null;
        DoneText = doneText ?? string.Empty;
        ShowsTimelineTime = settings.TimelineShowsTime;
        TimeText = $"{localNow:t}";
        HideButtonText = $"Hide {settings.QuickHideSeconds}s";
        _settings = settings;
        _showsTimer = showsTimer;
        _noSession = !sessionActive;
        ApplyQuiet();
        Scale = settings.Size switch
        {
            OverlaySize.Small => 0.8,
            OverlaySize.Large => 1.35,
            _ => 1,
        };
    }
}
