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
/// timers and alarms run. Ctrl+Alt+H hides all of it until pressed again (a held Ctrl+Alt still peeks, a glow still shows), and the panel's
/// Disable turns it off until the user turns it on in the app. Use from the UI thread only.
/// </summary>
public sealed class SessionOverlayService : IDisposable
{
    private static readonly TimeSpan DoneHold = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan WakeMargin = TimeSpan.FromMilliseconds(50);
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
    private readonly IGlobalHotkey _hideHotkey;
    private readonly DayBarViewModel _day;
    private readonly TaskbarOverlayViewModel _model;
    // The panel opens when Ctrl+Alt has been held for the "Peek opens after" delay (a tap of the two keys for something else doesn't flash it).
    private readonly DispatcherTimer _peekTimer = new();
    private readonly DispatcherTimer _wakeTimer = new();
    // Every moment a wake was asked for and has not come yet; the one timer always runs to the nearest, so a nearer wake never drops a later one.
    private readonly List<DateTimeOffset> _pendingWakes = [];
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
    private (bool On, bool Hidden, bool HiddenByShortcut, bool OverlayOn) _lastStatus = (false, false, false, true);
    private bool _started;

    // The keyboard hook is installed only while hold-to-peek is on and the overlay is not turned off, and either the tile is on screen or
    // Ctrl+Alt+H keeps a tile hidden that would otherwise show (ADR 53, amended).
    private bool _hookOn;

    // Ctrl+Alt+Space is held only while the sticky panel option is on and the overlay is not turned off.
    private bool _stickyRegistered;

    // Ctrl+Alt+H is held while its switch is on and the overlay is not turned off.
    private bool _hideRegistered;

    // While the overlay is hidden by Ctrl+Alt+H: Ctrl+Alt is down and the keyboard hook sees it (the tile and panel show for as long), a sticky panel
    // (Ctrl+Alt+Space) is open (the tile shows until it closes), and something is there to show if it were not hidden (the hook stays on for the peek).
    private bool _revealedByHold;

    // Ctrl+Alt has been held for the "Peek opens after" delay (the peek opened, or opens now at delay 0): only then does a hold show a hidden tile,
    // so a Ctrl+Alt+key shortcut of another app does not flash it.
    private bool _peekDelayPassed;
    private bool _stickyReveal;
    private bool _hiddenButPeekable;
    private bool _sessionWasActive;
    private string? _panelKeyMode;
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
        IGlobalHotkey hideHotkey,
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
        _hideHotkey = hideHotkey;
        _day = timelineDay;
        Actions.PendingChangeAt = () => _configuration.Pending is { } pending && pending.ReadyAt > _sessions.Now ? pending.ReadyAt : null;
        _model = new TaskbarOverlayViewModel(sessions, configuration, settings, timelineDay, battery, visibility, Actions)
        {
            HideRequested = HideBriefly,
            ToggleHiddenRequested = ToggleHidden,
            DisableRequested = DisableFromPanel,
        };
        cues.Cued += (_, cue) => OnCue(cue.Cue, cue.SessionEnded);
        battery.Changed += (_, _) => Dispatcher.UIThread.Post(Update);
        visibility.Changed += (_, _) => Update();
        _peekTimer.Tick += (_, _) =>
        {
            _peekTimer.Stop();
            OpenPeek(verifyKeys: true);
        };
        _wakeTimer.Tick += (_, _) =>
        {
            _wakeTimer.Stop();
            Update();
            ArmWakeTimer();
        };
        _clockTimer.Tick += (_, _) => Update();
        _peekGuard.Tick += (_, _) =>
        {
            // Also guards the tile shown only for as long as Ctrl+Alt is held (hidden by Ctrl+Alt+H).
            bool panelPeek = _panel is { IsSticky: false };
            if ((panelPeek || _revealedByHold) && _control.AreDragKeysHeld())
            {
                return;
            }

            _peekGuard.Stop();
            if (panelPeek)
            {
                ClosePanel();
            }

            if (panelPeek || _revealedByHold)
            {
                _ctrlAlt.Resync();
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

    /// <summary>Why Ctrl+Alt+H could not be registered (another program holds it); null when it works or is off.</summary>
    public string? HideHotkeyError { get; private set; }

    /// <summary>True while Ctrl+Alt+H keeps the overlay hidden (a held Ctrl+Alt still peeks). Never true with the overlay turned off or the shortcut switched off.</summary>
    public bool IsHiddenByShortcut
    {
        get
        {
            OverlaySettings settings = _settings.Current;
            return settings.OverlayOn && settings.HideShortcutOn && settings.HiddenByShortcut;
        }
    }

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
    public bool IsOnForSession => _settings.Current.OverlayOn
                                  && _sessions.State.IsActive
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

            // A fresh hook at each real session start or end (not every phase change): Windows drops one that was too slow once, silently.
            if (_hookOn && state.IsActive != _sessionWasActive)
            {
                _ctrlAlt.SetListening(true);
            }

            _sessionWasActive = state.IsActive;

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
        _stickyHotkey.Pressed += (_, _) => Dispatcher.UIThread.Post(OpenStickyPanel);
        _hideHotkey.Pressed += (_, _) => Dispatcher.UIThread.Post(ToggleHidden);
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
        if (_settings.Current.HiddenByShortcut)
        {
            // Saving it runs Update. A turned-off overlay stays off: only Today (or Settings) turns it on.
            _ = _settings.SaveLookAsync(current => current with { HiddenByShortcut = false });
            return;
        }

        Update();
    }

    /// <summary>Ctrl+Alt+H (and the panel's H key while that shortcut is on): hide the overlay until this is pressed again. Kept across sessions and restarts.</summary>
    public void ToggleHidden()
    {
        OverlaySettings settings = _settings.Current;
        if (!settings.OverlayOn || !settings.HideShortcutOn)
        {
            return;
        }

        bool hide = !settings.HiddenByShortcut;
        if (hide)
        {
            // Keys still down must not open the panel for the tile that was just hidden.
            _peekTimer.Stop();
            ClosePanel();
        }
        else
        {
            // Showing it means showing it: a quick hide that is still running ends too.
            _hiddenUntil = DateTimeOffset.MinValue;
        }

        _ = _settings.SaveLookAsync(current => current with { HiddenByShortcut = hide, HideShortcutSeen = true });
    }

    /// <summary>
    /// The panel's Disable: the overlay is off (no tile, no peek, no shortcuts) until the user turns it on in the app. Refused during a focus
    /// session, like the Settings switch.
    /// </summary>
    public void DisableFromPanel()
    {
        if (_sessions.IsEnforcing || !_settings.Current.OverlayOn)
        {
            return;
        }

        ClosePanel();
        _ = _settings.SaveLookAsync(current => current with { OverlayOn = false, OffFromPanel = true, HiddenByShortcut = false });
        Actions.Notify("Overlay turned off", "Turn it back on from Today or in Settings.");
    }

    /// <summary>Today's "Turn on", or the Settings switch: the overlay is back. Always allowed (it only adds to what shows).</summary>
    public void TurnOn()
    {
        _hiddenUntil = DateTimeOffset.MinValue;
        _ = _settings.SaveLookAsync(current => current with { OverlayOn = true, OffFromPanel = false, HiddenByShortcut = false });
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
        _pendingWakes.Clear();
        _clockTimer.Stop();
        _hookOn = false;
        _ctrlAlt.SetListening(false);
        _stickyHotkey.Unregister();
        _hideHotkey.Unregister();
        _panelKeys.Unregister();
        CloseWindows();
        _day.Dispose();
    }

    private void OnCue(SessionCue cue, bool sessionEnded)
    {
        DateTimeOffset now = _sessions.Now;
        // Hidden by Ctrl+Alt+H, the notice still shows with the glow (the tile is not showing, so "shown a moment ago" would never be true).
        if (sessionEnded && (IsHiddenByShortcut || now - _lastShownAt <= TimeSpan.FromSeconds(2)))
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
        if (at - _sessions.Now + WakeMargin <= TimeSpan.Zero)
        {
            return;
        }

        if (!_pendingWakes.Contains(at))
        {
            _pendingWakes.Add(at);
        }

        ArmWakeTimer();
    }

    // Runs the timer to the nearest wake still ahead. Wakes already past are dropped (so nothing fires twice), and with none left the timer stays off.
    private void ArmWakeTimer()
    {
        DateTimeOffset now = _sessions.Now;
        _pendingWakes.RemoveAll(at => at + WakeMargin <= now);
        if (_pendingWakes.Count == 0)
        {
            _wakeTimer.Stop();
            return;
        }

        TimeSpan wait = _pendingWakes.Min() - now + WakeMargin;
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
            _peekDelayPassed = false;
            int delayMs = _settings.Current.PeekDelayMs;
            if (delayMs <= 0)
            {
                // Called from inside the hook's key-down: the system's own key state has not caught up yet, so it can't be checked here.
                // The peek guard (once a second while a peek is open) closes a panel whose keys are not really down.
                OpenPeek(verifyKeys: false);
                return;
            }

            _peekTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
            _peekTimer.Start();
            return;
        }

        _peekTimer.Stop();
        _peekDelayPassed = false;
        if (_panel is { IsSticky: false })
        {
            ClosePanel();
        }

        if (IsHiddenByShortcut)
        {
            // Hidden: the tile hides again on release (a sticky panel keeps it up until it closes).
            Update();
        }
    }

    // Ctrl+Alt+Space, and a click on the tile. While Ctrl+Alt+H keeps the overlay hidden, the tile shows for as long as this panel is open,
    // also when it was a peek (or a glow) that brought the tile up.
    private void OpenStickyPanel()
    {
        if (IsHiddenByShortcut && _settings.Current.PanelSticky)
        {
            _stickyReveal = true;
            Update();
        }

        OpenPanel(sticky: true);
        if (_stickyReveal && _panel is not { IsSticky: true })
        {
            _stickyReveal = false;
            Update();
        }
    }

    // The peek delay is over (or zero): open the panel if Ctrl+Alt is really still held.
    private void OpenPeek(bool verifyKeys)
    {
        if (verifyKeys && !_control.AreDragKeysHeld())
        {
            // The hook thinks Ctrl+Alt is down but the keyboard says no: a key-up was missed.
            _ctrlAlt.Resync();
            return;
        }

        if (_ctrlAlt.IsHeld && _settings.Current.HoldToPeek)
        {
            // Hidden: the tile comes up now, as the panel does.
            _peekDelayPassed = true;
            if (IsHiddenByShortcut)
            {
                Update();
            }

            OpenPanel(sticky: false);
        }
    }

    private void OpenPanel(bool sticky)
    {
        if (_tile is not { IsVisible: true } tile || (sticky && !_settings.Current.PanelSticky))
        {
            return;
        }

        if (sticky)
        {
            // A peek timer still running must not fire later and turn this panel into a peek.
            _peekTimer.Stop();
        }

        if (_panel is { IsSticky: true })
        {
            if (sticky)
            {
                ClosePanel();
            }

            // A peek never demotes a sticky panel.
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
            OverlayPanelWindow opened = _panel;
            _panel.Closed += (_, _) =>
            {
                if (_panel is not null && !ReferenceEquals(_panel, opened))
                {
                    // A newer panel is current; this old one closing late must not clear it.
                    return;
                }

                _panelKeys.Unregister();
                _panelKeyMode = null;
                _panel = null;
                if (_stickyReveal)
                {
                    // Posted: this runs inside a redraw that may be closing the panel.
                    _stickyReveal = false;
                    Dispatcher.UIThread.Post(Update);
                }
            };
        }

        _panelModel.IsSticky = sticky;
        _panel.Present(tile.Box, tile.Target, sticky);
        SyncPanelKeys();
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

    // A peek (Ctrl+Alt held) takes no system-wide keys while "peek keys locked" is on (the default), and otherwise only ones that cannot
    // start, end, lock or unlock anything: hide, +10 min (stricter only), up and down. Done can end a session, so it is not among them.
    // The sticky panel (Ctrl+Alt+Space) has the keyboard focus and takes its keys itself; its shortcuts stay registered as before.
    // H is not in these lists: with Ctrl+Alt+H on, the one system-wide registration of it is the hide shortcut's (the panel's H then does the same
    // toggle; registering it twice would fail), and with it off the panel adds H itself (the short Hide) below.
    private static readonly string[] PeekSafeKeys = ["T", "Up", "Down"];
    private static readonly string[] StickyKeys = ["D", "P", "T", "B", "O", "M", "F", "L", "1", "2", "3", "Up", "Down", "Plus", "Minus"];

    private void SyncPanelKeys()
    {
        if (_panel is null)
        {
            return;
        }

        OverlaySettings settings = _settings.Current;
        bool panelOwnsH = !(settings.OverlayOn && settings.HideShortcutOn);
        string mode = (_panel.IsSticky ? "sticky" : settings.PeekKeysLocked ? "locked" : "safe") + (panelOwnsH ? "+h" : string.Empty);
        if (mode == _panelKeyMode)
        {
            return;
        }

        _panelKeyMode = mode;
        string[] keys = mode.Split('+')[0] switch
        {
            "sticky" => StickyKeys,
            "safe" => PeekSafeKeys,
            _ => [],
        };
        if (panelOwnsH && keys.Length > 0)
        {
            keys = [.. keys, "H"];
        }

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
        SyncStickyHotkey();
        SyncHideHotkey();
        SyncPanelKeys();
        (bool, bool, bool, bool) status = (IsOnForSession, IsHiddenForSession, IsHiddenByShortcut, _settings.Current.OverlayOn);
        if (status != _lastStatus)
        {
            _lastStatus = status;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SyncStickyHotkey()
    {
        bool want = _settings.Current.PanelSticky && _settings.Current.OverlayOn;
        if (want == _stickyRegistered)
        {
            return;
        }

        _stickyRegistered = want;
        if (want)
        {
            _stickyHotkey.TryRegister(new HotkeyChord(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "Space"), out string? error);
            StickyHotkeyError = error;
        }
        else
        {
            _stickyHotkey.Unregister();
            StickyHotkeyError = null;
        }
    }

    private void SyncHideHotkey()
    {
        OverlaySettings settings = _settings.Current;
        bool want = settings.HideShortcutOn && settings.OverlayOn;
        if (want == _hideRegistered)
        {
            return;
        }

        _hideRegistered = want;
        if (want)
        {
            // An open panel may still hold the chord as its own H key (the shortcut was off a moment ago); it gets its keys again right after.
            if (_panel is not null)
            {
                _panelKeys.Unregister();
                _panelKeyMode = null;
            }

            _hideHotkey.TryRegister(new HotkeyChord(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "H"), out string? error);
            HideHotkeyError = error;
        }
        else
        {
            _hideHotkey.Unregister();
            HideHotkeyError = null;
        }
    }

    // The hook reads Ctrl and Alt for the peek. It is on while the tile shows, and while Ctrl+Alt+H keeps a tile hidden that would otherwise show
    // (so a held Ctrl+Alt can bring it up); off when the overlay is turned off.
    private void SyncHook()
    {
        OverlaySettings settings = _settings.Current;
        bool want = settings.OverlayOn && settings.HoldToPeek && (_tile is { IsVisible: true } || _hiddenButPeekable);
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
        if (!settings.OverlayOn)
        {
            // Turned off (Settings or the panel's Disable): nothing at all, no peek and no timers of its own.
            _hiddenButPeekable = false;
            _revealedByHold = false;
            _stickyReveal = false;
            CloseWindows();
            StopClockTimer();
            return;
        }

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

        // Hidden by Ctrl+Alt+H: it shows for a held Ctrl+Alt, for an open sticky panel, and for the glow of a time-up or alarm (the chime plays either way).
        // The hook (and the peek) only make sense where a tile could show: not over a full-screen app that takes the corner clock's place or hides
        // the overlay, not on the slim line, not where the placement says hidden.
        OverlayTarget placed = OverlayPlacementCalculator.Resolve(settings, _geometry.Taskbars);
        bool tileCouldShow = !(foreground.IsFullScreen && (settings.CornerClock || settings.HideInFullscreen))
                             && placed.Kind is not (OverlayTargetKind.Hidden or OverlayTargetKind.SlimLine);
        bool hiddenByUser = false;
        _revealedByHold = false;
        if (IsHiddenByShortcut)
        {
            _revealedByHold = _hookOn && _ctrlAlt.IsHeld && _peekDelayPassed;
            bool glowing = tileCouldShow && now < _model.GlowUntil;
            hiddenByUser = !(_revealedByHold || _stickyReveal || glowing);
            if (glowing)
            {
                // The glow's end is what hides it again; no other tick may be due while idle.
                ScheduleWake(_model.GlowUntil);
            }
        }

        _hiddenButPeekable = IsHiddenByShortcut && hasSomething && !hiddenBriefly && !inCall && tileCouldShow;
        if (_revealedByHold && !_peekGuard.IsEnabled)
        {
            _peekGuard.Start();
        }

        if (!hasSomething || hiddenBriefly || inCall || hiddenByUser)
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

        OverlayTarget target = placed;
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
            TaskbarOverlayWindow created = new(_control, _model);
            _tile = created;
            _tile.Clicked += (_, _) => OpenStickyPanel();
            _tile.SideTileMoved += (_, top) => _ = _settings.SaveLookAsync(current => current with { SideTileY = top });
            _tile.Closed += (_, _) =>
            {
                // Only this tile clears the field; a newer one assigned before this Closed fired stays.
                if (ReferenceEquals(_tile, created))
                {
                    _tile = null;
                }
            };
        }

        // Set before Apply, which puts the tile back in front: not over a Windows flyout that is open (it climbs back once the flyout closes).
        _tile.YieldsToShellFlyout = _geometry.Foreground.IsShellFlyout;
        if (_tile.YieldsToShellFlyout)
        {
            // Read the foreground again with each update while a flyout is up (no new timer: the per-second update already runs), so a flyout
            // that closes without a foreground event does not leave the tile waiting; Changed follows only if something differs.
            _geometry.Refresh();
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
        bool needsClock = !sessionActive && _tile is not null && ShowsClock(settings);
        if (!needsClock)
        {
            StopClockTimer();
            return;
        }

        DateTimeOffset now = _sessions.Now;
        _clockTimer.Interval = TimeSpan.FromSeconds(60 - now.Second) + TimeSpan.FromMilliseconds(100);
        _clockTimer.Start();
    }

    // The tile draws a clock when it covers the tray, or when Time & date is on at Medium and up (Small stays one line).
    private static bool ShowsClock(OverlaySettings settings)
    {
        if (settings.EffectivePlacement == OverlayPlacement.CoverTray)
        {
            return true;
        }

        return settings.Size != OverlaySize.Small && settings.Items.Any(item => item.Kind == OverlayItemKind.TimeDate && item.Enabled);
    }

    private void StopClockTimer()
    {
        _clockTimer.Stop();
    }
}
