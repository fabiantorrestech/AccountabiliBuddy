using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.App.Views;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Runs the session overlay (ADR 51): a tile docked over the taskbar (or floating, or standing beside a side taskbar), the Ctrl+Alt panel that
/// rises from it, the corner clock over a full-screen app, and the slim line over an auto-hidden taskbar. It decides which of them shows from
/// the session, the plan, the settings, the app in front and where the taskbar is (<see cref="ITaskbarGeometry"/>), and redraws on events:
/// a session tick, a setting, a taskbar or display change, a new window in front. Display only: nothing here changes what is blocked; the
/// buttons use the Today page's own commands. The timer shows while a session runs for a profile that has it on (its own choice or the default;
/// it can be hidden for the rest of the session), between sessions on days with focus blocks, in the evening (sleep mode), and while this PC's
/// timers and alarms run. Use from the UI thread only.
/// </summary>
public sealed class SessionOverlayService : IDisposable
{
    // The panel opens when Ctrl+Alt has been held this long (a tap of the two keys for something else doesn't flash it).
    private static readonly TimeSpan PeekDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan DoneHold = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan GlowFor = TimeSpan.FromMilliseconds(1400);
    private static readonly TimeSpan NudgeDotFor = TimeSpan.FromMinutes(10);

    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly OverlaySettingsService _settings;
    private readonly IOverlayWindowControl _control;
    private readonly ITaskbarGeometry _geometry;
    private readonly ICtrlAltWatcher _ctrlAlt;
    private readonly IPanelKeys _panelKeys;
    private readonly IGlobalHotkey _stickyHotkey;
    private readonly DayBarViewModel _day;
    private readonly TaskbarOverlayViewModel _model;
    private readonly DispatcherTimer _peekTimer = new() { Interval = PeekDelay };
    private readonly DispatcherTimer _wakeTimer = new();
    private readonly DispatcherTimer _clockTimer = new();

    // While a peek is open, a missed key-up (Ctrl+Alt+Del, a lock screen, an elevated window) would leave it open: once a second the real key
    // state is looked at, only for as long as the peek lasts.
    private readonly DispatcherTimer _peekGuard = new() { Interval = TimeSpan.FromSeconds(1) };

    private TaskbarOverlayWindow? _tile;
    private OverlayPanelWindow? _panel;
    private OverlayPanelViewModel? _panelModel;
    private CornerClockWindow? _corner;
    private SlimLineWindow? _slim;
    private StepAwayService? _stepAway;
    private PlanFirstService? _planFirst;
    private DateTimeOffset _hiddenUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _doneUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _lastShownAt = DateTimeOffset.MinValue;
    private (bool On, bool Hidden) _lastStatus;
    private bool _started;

    // The keyboard hook is installed only while hold-to-peek is on and the tile is on screen.
    private bool _hookOn;
    private bool _disposed;

    public SessionOverlayService(
        SessionController sessions,
        ConfigurationService configuration,
        OverlaySettingsService settings,
        IOverlayWindowControl control,
        ITaskbarGeometry geometry,
        IBatteryReader battery,
        IBatteryVisibility visibility,
        ICtrlAltWatcher ctrlAlt,
        IPanelKeys panelKeys,
        IGlobalHotkey stickyHotkey,
        DayBarViewModel timelineDay,
        SessionCueService cues)
    {
        _sessions = sessions;
        _configuration = configuration;
        _settings = settings;
        _control = control;
        _geometry = geometry;
        _ctrlAlt = ctrlAlt;
        _panelKeys = panelKeys;
        _stickyHotkey = stickyHotkey;
        _day = timelineDay;
        Actions.PendingChangeAt = () => _configuration.Pending is { } pending && pending.ReadyAt > _sessions.Now ? pending.ReadyAt : null;
        _model = new TaskbarOverlayViewModel(sessions, configuration, settings, timelineDay, battery, visibility, Actions)
        {
            HideRequested = HideBriefly,
        };
        cues.Cued += (_, cue) => OnCue(cue.Cue, cue.SessionEnded);
        battery.Changed += (_, _) => Dispatcher.UIThread.Post(Update);
        _peekTimer.Tick += (_, _) =>
        {
            _peekTimer.Stop();
            if (!_control.AreDragKeysHeld())
            {
                // The hook thinks Ctrl+Alt is down but the keyboard says no: a key-up was missed.
                _ctrlAlt.Resync();
                return;
            }

            if (_ctrlAlt.IsHeld && _settings.Current.HoldToPeek)
            {
                OpenPanel(sticky: false);
            }
        };
        _wakeTimer.Tick += (_, _) =>
        {
            _wakeTimer.Stop();
            Update();
        };
        _clockTimer.Tick += (_, _) => Update();
        _peekGuard.Tick += (_, _) =>
        {
            if (_panel is not { IsSticky: false } || !_control.AreDragKeysHeld())
            {
                _peekGuard.Stop();
                if (_panel is { IsSticky: false })
                {
                    ClosePanel();
                    _ctrlAlt.Resync();
                }
            }
        };
    }

    /// <summary>What the overlay and its panel ask the app to do and ask it about; set by the app before <see cref="Start"/>.</summary>
    public OverlayActions Actions { get; } = new();

    /// <summary>The tile's box and whether it hugs the right edge, for cards that rise from it; null while no tile is on screen.</summary>
    public (PixelBox Box, bool AlignRight)? TileAnchor => _tile is { IsVisible: true } tile
        ? (new PixelBox(tile.Box.X, tile.Box.Y, tile.Box.Right, tile.Box.Bottom), tile.Target.AlignRight)
        : null;

    /// <summary>Why Ctrl+Alt+Space could not be registered (another copy of the app or another program holds it); null when it works.</summary>
    public string? StickyHotkeyError { get; private set; }

    /// <summary>Raised when the timer is shown, hidden or turned on or off for the session.</summary>
    public event EventHandler? Changed;

    /// <summary>The panel's "Open app", or its gear: the app shows the main window.</summary>
    public event EventHandler? OpenAppRequested
    {
        add => Actions.OpenApp += () => value?.Invoke(this, EventArgs.Empty);
        remove { }
    }

    /// <summary>True when the timer was hidden for the session that is running now.</summary>
    public bool IsHiddenForSession { get; private set; }

    /// <summary>
    /// True while a session runs whose profile has the timer on (shown or hidden for now), or a count-up session of the Quick Start profile.
    /// </summary>
    public bool IsOnForSession => _sessions.State.IsActive
                                  && (_settings.Current.For(_sessions.State.ProfileId).Enabled || IsCountUp(_sessions.State));

    private static bool IsCountUp(SessionState state)
    {
        return state.Mode == SessionMode.Manual && !state.HasSetEnd;
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _sessions.StateChanged += (_, state) =>
        {
            if (!state.IsActive)
            {
                IsHiddenForSession = false;
            }

            // A fresh hook at each session start or end: Windows drops one that was too slow once, silently.
            if (_hookOn)
            {
                _ctrlAlt.SetListening(true);
            }

            Update();
        };
        _sessions.Ticked += (_, _) => Update();
        _settings.Changed += (_, _) => Update();
        _configuration.Changed += (_, _) => Update();
        _day.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DayBarViewModel.HasPlannedFocus) or nameof(DayBarViewModel.ShowsSleep) or nameof(DayBarViewModel.StatusText))
            {
                Update();
            }
        };
        _geometry.Changed += (_, _) => Dispatcher.UIThread.Post(Update);
        _ctrlAlt.HeldChanged += (_, held) => Dispatcher.UIThread.Post(() => OnCtrlAlt(held));
        _panelKeys.Pressed += (_, chord) => Dispatcher.UIThread.Post(() => _panelModel?.HandleKey(chord.Key == "Plus" ? "+" : chord.Key == "Minus" ? "-" : chord.Key));
        _stickyHotkey.Pressed += (_, _) => Dispatcher.UIThread.Post(() => OpenPanel(sticky: true));
        _stickyHotkey.TryRegister(new HotkeyChord(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "Space"), out string? error);
        StickyHotkeyError = error;
        _day.Start();
        Update();
    }

    /// <summary>The window in front changed: place or hide the overlay now, not at the next tick.</summary>
    public void OnActivityChanged()
    {
        Update();
    }

    public void SetNoisePlayer(NoisePlayerViewModel noise)
    {
        _model.Noise = noise;
        noise.PropertyChanged += (_, _) => RefreshPanel();
    }

    public void SetTonightWake(TonightWakeViewModel wake)
    {
        _model.Wake = wake;
    }

    /// <summary>Guided sessions: the running session's checklist, for the task line, Done and the panel.</summary>
    public void SetChecklist(ChecklistViewModel checklist)
    {
        _model.Checklist = checklist;
        checklist.PropertyChanged += (_, _) => Update();
    }

    public void SetPlanFirst(PlanFirstService planFirst)
    {
        _planFirst = planFirst;
        _model.PlanFirst = planFirst;
        planFirst.Changed += (_, _) => Update();
    }

    public void SetStepAway(StepAwayService stepAway)
    {
        _stepAway = stepAway;
        _model.StepAway = stepAway;
        stepAway.Changed += (_, _) => Update();
    }

    /// <summary>This PC's timers and alarms: the tile says them when nothing else runs, and the large panel lists them.</summary>
    public void SetTimers(Timers.TimersService timers)
    {
        _model.Timers = new OverlayTimersViewModel(timers);
        timers.Changed += (_, _) => Update();
    }

    public void SetGoalText(string text)
    {
        _model.GoalText = text;
    }

    public void SetSyncQr(Action? show)
    {
        Actions.ShowSyncQr = show;
    }

    public void SetHiddenForSession(bool hidden)
    {
        IsHiddenForSession = hidden && _sessions.State.IsActive;
        Update();
    }

    /// <summary>The white glow, e.g. for this PC's alarm (ADR 35): the border lights for a moment.</summary>
    public void Glow()
    {
        _model.GlowUntil = _sessions.Now + GlowFor;
        Update();
        ScheduleWake(_model.GlowUntil);
    }

    /// <summary>The drift ladder's step 1 is on (or over): the tile shows the drift state; the nudge card's dot stays until the user looks.</summary>
    public void SetDrift(string? label, DateTimeOffset since)
    {
        _model.SetDrift(label, since);
        Update();
    }

    /// <summary>A nudge card went unread (it timed out after 20 s): the tile keeps a dot until it is clicked or the panel opens (18d).</summary>
    public void ShowNudgeDot()
    {
        _model.HasNudgeDot = true;
        Update();
        ScheduleWake(_sessions.Now + NudgeDotFor);
    }

    /// <summary>The tray's "Show on-screen window again": a quick hide is cut short and the windows are made anew.</summary>
    public void ShowAgain()
    {
        _hiddenUntil = DateTimeOffset.MinValue;
        CloseWindows();
        Update();
    }

    /// <summary>The panel's "Hide": out of the way for a few seconds, then back on its own.</summary>
    public void HideBriefly()
    {
        _hiddenUntil = _sessions.Now + TimeSpan.FromSeconds(_settings.Current.QuickHideSeconds);
        ClosePanel();
        Update();
        ScheduleWake(_hiddenUntil);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _peekTimer.Stop();
        _wakeTimer.Stop();
        _clockTimer.Stop();
        _hookOn = false;
        _ctrlAlt.SetListening(false);
        _stickyHotkey.Unregister();
        _panelKeys.Unregister();
        CloseWindows();
        _day.Dispose();
    }

    private void OnCue(SessionCue cue, bool sessionEnded)
    {
        DateTimeOffset now = _sessions.Now;
        if (sessionEnded && now - _lastShownAt <= TimeSpan.FromSeconds(2))
        {
            _doneUntil = now + DoneHold;
            _model.DoneNotice = cue == SessionCue.BreakEnded ? "Break over · session done" : "Session done";
            ScheduleWake(_doneUntil);
        }

        _model.GlowUntil = now + GlowFor;
        Update();
        ScheduleWake(_model.GlowUntil);
    }

    // One timer wakes the overlay at the next moment something it shows runs out (a quick hide, a glow, the done notice, the nudge dot).
    private void ScheduleWake(DateTimeOffset at)
    {
        TimeSpan wait = at - _sessions.Now + TimeSpan.FromMilliseconds(50);
        if (wait <= TimeSpan.Zero)
        {
            return;
        }

        if (!_wakeTimer.IsEnabled || wait < _wakeTimer.Interval)
        {
            _wakeTimer.Stop();
            _wakeTimer.Interval = wait;
            _wakeTimer.Start();
        }
    }

    private void OnCtrlAlt(bool held)
    {
        if (held)
        {
            _peekTimer.Stop();
            _peekTimer.Start();
            return;
        }

        _peekTimer.Stop();
        if (_panel is { IsSticky: false })
        {
            ClosePanel();
        }
    }

    private void OpenPanel(bool sticky)
    {
        if (_tile is not { IsVisible: true } tile || (sticky && !_settings.Current.PanelSticky))
        {
            return;
        }

        if (_panel is { IsSticky: true } && sticky)
        {
            ClosePanel();
            return;
        }

        _ = _settings.MarkPanelSeenAsync();
        _model.HasNudgeDot = false;
        _model.Wake?.Refresh();
        _model.Refresh();
        _panelModel ??= new OverlayPanelViewModel(_model, _settings, _sessions);
        _panelModel.Size = _settings.Current.PanelSize;
        if (_panel is null)
        {
            _panel = new OverlayPanelWindow(_control, _panelModel);
            _panel.Closed += (_, _) =>
            {
                _panelKeys.Unregister();
                _panel = null;
            };
            RegisterPanelKeys();
        }

        _panel.Present(tile.Box, tile.Target, sticky);
        if (sticky)
        {
            _peekGuard.Stop();
        }
        else
        {
            _peekGuard.Start();
        }
    }

    private void ClosePanel()
    {
        _peekGuard.Stop();
        _panel?.Close();
        _panel = null;
    }

    private void RefreshPanel()
    {
        _panelModel?.Refresh();
    }

    // While the panel is open, Ctrl+Alt+its letters work whichever app is in front. Shortcuts another app already holds are skipped.
    private void RegisterPanelKeys()
    {
        string[] keys = ["D", "P", "T", "B", "H", "O", "M", "F", "L", "1", "2", "3", "Up", "Down", "Plus", "Minus"];
        const HotkeyModifiers modifiers = HotkeyModifiers.Ctrl | HotkeyModifiers.Alt;
        _panelKeys.Register(keys.Select(key => new HotkeyChord(modifiers, key)).ToList());
    }

    private void CloseWindows()
    {
        ClosePanel();
        _tile?.Close();
        _tile = null;
        _corner?.Close();
        _corner = null;
        _slim?.Close();
        _slim = null;
    }

    private void Update()
    {
        if (_disposed)
        {
            return;
        }

        Show();
        SyncHook();
        (bool, bool) status = (IsOnForSession, IsHiddenForSession);
        if (status != _lastStatus)
        {
            _lastStatus = status;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SyncHook()
    {
        bool want = _settings.Current.HoldToPeek && _tile is { IsVisible: true };
        if (want != _hookOn)
        {
            _hookOn = want;
            _ctrlAlt.SetListening(want);
        }
    }

    private void Show()
    {
        OverlaySettings settings = _settings.Current;
        SessionState state = _sessions.State;
        DateTimeOffset now = _sessions.Now;
        ForegroundScreenState foreground = _geometry.Foreground;
        OverlayAppRule? rule = OverlayAppRules.Find(settings.Rules, foreground.ProcessName);

        bool showsTimer = IsOnForSession && !IsHiddenForSession;
        bool showsTimeline = settings.ShowTimeline && _day.HasPlannedFocus && !state.IsActive;
        _model.Timers?.Refresh(false, settings.FillUp);
        bool showsTimers = _model.Timers?.HasLines ?? false;
        bool doneNotice = now < _doneUntil;
        if (!doneNotice)
        {
            _model.DoneNotice = null;
        }

        bool hasSomething = showsTimer || showsTimeline || _day.ShowsSleep || showsTimers || doneNotice;
        bool hiddenBriefly = now < _hiddenUntil;
        bool inCall = rule is { HideDuringCalls: true } && (foreground.IsFullScreen || (rule.CallClasses is { } classes && classes.Contains(foreground.ClassName ?? string.Empty)));
        if (!hasSomething || hiddenBriefly || inCall)
        {
            CloseWindows();
            _clockTimer.Stop();
            return;
        }

        _lastShownAt = now;
        _model.Refresh();
        RefreshPanel();

        // A full-screen app in front: the corner clock takes over (or nothing, when it is off and the old "hide in full screen" is on).
        if (foreground.IsFullScreen && (settings.CornerClock || settings.HideInFullscreen))
        {
            _tile?.Close();
            _tile = null;
            ClosePanel();
            _slim?.Close();
            _slim = null;
            ShowCornerClock(settings, foreground, rule);
            StopClockTimer();
            return;
        }

        _corner?.Close();
        _corner = null;

        OverlayTarget target = OverlayPlacementCalculator.Resolve(settings, _geometry.Taskbars);
        switch (target.Kind)
        {
            case OverlayTargetKind.Hidden:
                _tile?.Close();
                _tile = null;
                ClosePanel();
                _slim?.Close();
                _slim = null;
                break;
            case OverlayTargetKind.SlimLine:
                _tile?.Close();
                _tile = null;
                ClosePanel();
                _slim ??= new SlimLineWindow(_control, _model);
                _slim.Apply(target.Band);
                _slim.UpdateLine();
                if (!_slim.IsVisible)
                {
                    _slim.Show();
                }

                break;
            default:
                _slim?.Close();
                _slim = null;
                ShowTile(target, settings, rule);
                break;
        }

        UpdateClockTimer(state.IsActive);
    }

    private void ShowTile(OverlayTarget target, OverlaySettings settings, OverlayAppRule? rule)
    {
        if (_tile is null)
        {
            _tile = new TaskbarOverlayWindow(_control, _model);
            _tile.Clicked += (_, _) => OpenPanel(sticky: true);
            _tile.SideTileMoved += (_, top) => _ = _settings.SaveLookAsync(current => current with { SideTileY = top });
            _tile.Closed += (_, _) => _tile = null;
        }

        _tile.Apply(target, settings, rule);
        if (!_tile.IsVisible)
        {
            _tile.Show();
        }

        _tile.KeepOnTop();
        _panel?.Present(_tile.Box, _tile.Target, _panel.IsSticky);
    }

    private void ShowCornerClock(OverlaySettings settings, ForegroundScreenState foreground, OverlayAppRule? rule)
    {
        TaskbarInfo? taskbar = _geometry.Taskbars.FirstOrDefault(info => info.MonitorId == foreground.MonitorId) ?? _geometry.Taskbars.FirstOrDefault();
        if (taskbar is null || !settings.CornerClock)
        {
            return;
        }

        _corner ??= new CornerClockWindow(_control, _model);
        _corner.Apply(taskbar.Monitor, settings, foreground.IsGame || rule is { TimeOnly: true });
        if (!_corner.IsVisible)
        {
            _corner.Show();
        }
    }

    // With no session ticking, the clock in the overlay needs the next minute: one timer, only while the overlay shows its own clock.
    private void UpdateClockTimer(bool sessionActive)
    {
        OverlaySettings settings = _settings.Current;
        bool needsClock = !sessionActive && _tile is not null
                          && (settings.Size == OverlaySize.Wide || settings.EffectivePlacement == OverlayPlacement.CoverTray);
        if (!needsClock)
        {
            StopClockTimer();
            return;
        }

        DateTimeOffset now = _sessions.Now;
        _clockTimer.Interval = TimeSpan.FromSeconds(60 - now.Second) + TimeSpan.FromMilliseconds(100);
        _clockTimer.Start();
    }

    private void StopClockTimer()
    {
        _clockTimer.Stop();
    }
}
