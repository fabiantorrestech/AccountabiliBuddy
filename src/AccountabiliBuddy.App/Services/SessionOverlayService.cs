using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.App.Views;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Media;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Runs the small on-screen window. The session timer shows while a session runs (focus and breaks) for a profile
/// that has it on (its own choice or the default); it can be hidden for the rest of the current session (Session
/// tab), which resets when the session ends. In the evening (sleep mode) a sleep line shows on its own or on top.
/// The all-day timeline shows on days with focus blocks; while the timer
/// shows it steps aside or stays above it (<see cref="OverlaySettings.TimelineInFocus"/>). With
/// <see cref="OverlaySettings.HideInFullscreen"/> the window also steps aside while a fullscreen app is in front
/// (checked every tick), and the Ctrl+Alt "Hide" button hides it for a few seconds. When time is up it glows (sounds:
/// <see cref="SessionCueService"/>). This PC's timers and alarms add a line each under the rest and keep the window
/// showing. Display only: nothing here changes what is blocked. Use from the UI thread only.
/// </summary>
public sealed class SessionOverlayService : IDisposable
{
    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly OverlaySettingsService _settings;
    private readonly IOverlayWindowControl _control;
    private readonly SessionOverlayViewModel _timer = new();
    private readonly DayBarViewModel _day;
    private readonly OnScreenOverlayViewModel _viewModel;

    /// <summary>The white glow, e.g. for this PC's alarm (ADR 35).</summary>
    public void Glow() => _viewModel.Glow();

    private SessionOverlayWindow? _window;
    private StepAwayService? _stepAway;
    private PlanFirstService? _planFirst;
    private DateTimeOffset _hiddenUntil = DateTimeOffset.MinValue;

    // After a session ran out, the window stays a few seconds (glowing) even if nothing else would show.
    private static readonly TimeSpan DoneHold = TimeSpan.FromSeconds(4);
    private DateTimeOffset _doneUntil = DateTimeOffset.MinValue;
    private string? _doneText;
    private DateTimeOffset _lastShownAt = DateTimeOffset.MinValue;

    // A fullscreen app must stay in front this long before the window steps aside.
    private static readonly TimeSpan FullscreenDelay = TimeSpan.FromSeconds(1.5);
    private DateTimeOffset? _fullscreenSince;
    private (int? X, int? Y) _placedAt;
    private (bool On, bool Hidden) _lastStatus;

    public SessionOverlayService(
        SessionController sessions,
        ConfigurationService configuration,
        OverlaySettingsService settings,
        IOverlayWindowControl control,
        DayBarViewModel timelineDay,
        SessionCueService cues)
    {
        cues.Cued += (_, cue) => OnCue(cue.Cue, cue.SessionEnded);
        _day = timelineDay;
        _sessions = sessions;
        _configuration = configuration;
        _settings = settings;
        _control = control;
        _viewModel = new OnScreenOverlayViewModel(
            _timer, timelineDay, HideBriefly, () => _ = _settings.SavePositionAsync(null, null), () => OpenAppRequested?.Invoke(this, EventArgs.Empty));
        _viewModel.SetUseDefaultForApp(app => _ = _settings.ForgetAppOffsetAsync(app));
        _timer.HalfHourMarked += (_, _) => _viewModel.Glow();
    }

    /// <summary>True when the timer was hidden for the session that is running now.</summary>
    public bool IsHiddenForSession { get; private set; }

    /// <summary>
    /// True while a session runs whose profile has the timer on (shown or hidden for now), or a count-up session of
    /// the Quick Start profile.
    /// </summary>
    public bool IsOnForSession => _sessions.State.IsActive
                                  && (_settings.Current.For(_sessions.State.ProfileId).Enabled || IsCountUp(_sessions.State));

    // A Quick Start (with the default profile or the one picked) has no set end: it always shows the timer, since the
    // bar counting up is the point.
    private static bool IsCountUp(SessionState state)
    {
        return state.Mode == SessionMode.Manual && !state.HasSetEnd;
    }

    /// <summary>Raised when the timer is shown, hidden or turned on or off for the session.</summary>
    public event EventHandler? Changed;

    /// <summary>Ctrl+Alt + double-click on the window, or its Open button: the app shows the main window.</summary>
    public event EventHandler? OpenAppRequested;

    public void Start()
    {
        _sessions.StateChanged += (_, state) =>
        {
            if (!state.IsActive)
            {
                IsHiddenForSession = false;
            }

            Update();
        };
        _sessions.Ticked += (_, _) =>
        {
            Update();
            _viewModel.RefreshTasks(_sessions.State.IsActive);
        };
        _settings.Changed += (_, _) => Update();
        _configuration.Changed += (_, _) => Update();
        _day.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DayBarViewModel.HasPlannedFocus) or nameof(DayBarViewModel.ShowsSleep))
            {
                Update();
            }
        };
        _day.Start();
        Update();
    }

    /// <summary>The window in front changed (switching apps): place or hide the window now, not at the next tick.</summary>
    public void OnActivityChanged()
    {
        if (_window is not null)
        {
            Update();
        }
    }

    /// <summary>Shows the noise player's play/pause and volume while Ctrl+Alt is held.</summary>
    public void SetNoisePlayer(NoisePlayerViewModel noise)
    {
        _viewModel.Noise = noise;
    }

    /// <summary>Shows tonight's wake time, changeable for the night, while Ctrl+Alt is held.</summary>
    public void SetTonightWake(TonightWakeViewModel wake)
    {
        _viewModel.Wake = wake;
    }

    /// <summary>A "QR" button in the Ctrl+Alt details opens today's sync QR codes (ADR 22); null hides it.</summary>
    /// <summary>Guided sessions: the running session's checklist for the bar and the Ctrl+Alt details.</summary>
    public void SetChecklist(ChecklistViewModel checklist)
    {
        _viewModel.Checklist = checklist;
        checklist.PropertyChanged += (_, _) => _viewModel.RefreshTasks(_sessions.State.IsActive);
        _sessions.StateChanged += (_, state) => _viewModel.RefreshTasks(state.IsActive);
        _viewModel.RefreshTasks(_sessions.State.IsActive);
    }

    /// <summary>Plan first: "Planning · 4:12 left" on the bar and "I'm ready" with Ctrl+Alt.</summary>
    public void SetPlanFirst(PlanFirstService planFirst)
    {
        _planFirst = planFirst;
        planFirst.Changed += (_, _) => Update();
    }

    /// <summary>Step away: the Ctrl+Alt button and the "Away · 4:12" badge.</summary>
    public void SetStepAway(StepAwayService stepAway)
    {
        _stepAway = stepAway;
        _viewModel.SetStepAway(stepAway.Toggle);
        _viewModel.SetSideTask(stepAway.AskSideTaskLength);
        stepAway.Changed += (_, _) => Update();
    }

    /// <summary>This PC's timers and alarms: a line each on the window, with Pause, +1 min and ✕ while Ctrl+Alt is held.</summary>
    public void SetTimers(Timers.TimersService timers)
    {
        _viewModel.Timers = new OverlayTimersViewModel(timers);
        timers.Changed += (_, _) => Update();
    }

    /// <summary>Today's deep work against the goal for the Ctrl+Alt details (redesign phase 4).</summary>
    public void SetGoalText(string text)
    {
        _viewModel.GoalText = text;
    }

    public void SetSyncQr(Action? show)
    {
        _viewModel.SetSyncQr(show);
    }

    public void SetHiddenForSession(bool hidden)
    {
        IsHiddenForSession = hidden && _sessions.State.IsActive;
        Update();
    }

    /// <summary>
    /// The tray's "Show on-screen window again": a new window at its kept spot (or the corner when that spot is off
    /// every screen), and a quick hide is cut short. Hidden for the session (Session tab) stays hidden.
    /// </summary>
    public void ShowAgain()
    {
        _hiddenUntil = DateTimeOffset.MinValue;
        _window?.Close();
        _window = null;
        Update();
    }

    /// <summary>The Ctrl+Alt "Hide" button: out of the way for a few seconds, then back on its own.</summary>
    public void HideBriefly()
    {
        _hiddenUntil = _sessions.Now + TimeSpan.FromSeconds(_settings.Current.QuickHideSeconds);
        Update();
    }

    /// <summary>
    /// Time is up: the window glows. When the session ended with it and the window was showing a moment ago, it
    /// stays a few seconds so the glow is seen. A window hidden for now (quick hide, fullscreen) stays hidden.
    /// </summary>
    private void OnCue(SessionCue cue, bool sessionEnded)
    {
        DateTimeOffset now = _sessions.Now;
        if (sessionEnded && now - _lastShownAt <= TimeSpan.FromSeconds(2))
        {
            _doneUntil = now + DoneHold;
            _doneText = cue == SessionCue.BreakEnded ? "Break over · session done" : "Session done";
        }

        Update();
        if (_window is not null)
        {
            _viewModel.Glow();
        }
    }

    public void Dispose()
    {
        _day.Dispose();
        _window?.Close();
        _window = null;
    }

    private void Update()
    {
        Show();

        // Tell the Session tab only when something it shows changed, not on every tick.
        (bool, bool) status = (IsOnForSession, IsHiddenForSession);
        if (status != _lastStatus)
        {
            _lastStatus = status;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Show()
    {
        SessionState state = _sessions.State;
        OverlaySettings settings = _settings.Current;

        bool showsTimer = IsOnForSession && !IsHiddenForSession;
        bool showsTimeline = settings.ShowTimeline && _day.HasPlannedFocus
                             && !(showsTimer && settings.TimelineInFocus == TimelineInFocus.SwapForTimer);
        bool fullscreenInFront = settings.HideInFullscreen && FullscreenLastedAWhile();
        bool hiddenBriefly = _sessions.Now < _hiddenUntil;
        string? doneText = _sessions.Now < _doneUntil ? _doneText : null;
        bool showsSleep = _day.ShowsSleep;
        bool showsTimers = _viewModel.RefreshTimers();
        if ((!showsTimer && !showsTimeline && !showsSleep && !showsTimers && doneText is null) || fullscreenInFront || hiddenBriefly)
        {
            _window?.Close();
            _window = null;
            return;
        }

        if (showsTimer)
        {
            Guid profileId = state.ProfileId!.Value;
            string name = state.IsTest
                ? "Test session"
                : _configuration.Current.Profiles.FirstOrDefault(profile => profile.Id == profileId)?.Name ?? "Focus";
            _timer.Update(state, _sessions.Now, _sessions.TimeProvider.LocalTimeZone, name, ColorOf(profileId, settings), settings);
        }

        if (_stepAway is not null)
        {
            _viewModel.SetAway(state.IsAway ? _stepAway.AwayText : null, state.IsActive, _stepAway.IsSideTask);
        }

        if (_planFirst is not null)
        {
            // The planning stage shows as the timer's own bar (Planning, in its colour); a first session's learning keeps its line.
            _viewModel.SetPlanning(
                _planFirst.IsLearning ? "Learning · first session" : null,
                _planFirst.InStage ? _planFirst.Ready : null);
        }

        DateTime localNow = TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;
        _viewModel.Update(showsTimer, showsTimeline, showsSleep, doneText, localNow, settings, state.IsActive);
        _lastShownAt = _sessions.Now;

        if (_window is { IsLost: true })
        {
            // Minimized or off every screen from outside: a new one in its place.
            _window.Close();
            _window = null;
        }

        if (_window is null)
        {
            Open(settings);
        }
        else if (_placedAt != (settings.X, settings.Y))
        {
            // Moved from Settings (Reset position).
            _placedAt = (settings.X, settings.Y);
            _window.PlaceAt(settings.X, settings.Y);
        }

        _window!.SetCorner(settings.Corner);
        _window.KeepOnTop();
        // Its hint goes back above it (a Windows tooltip would land behind it).
        _window.KeepHintOnTop();

        // Keeps a maximized app's top-right buttons (close, tabs, extensions) clickable, by that app's own distance.
        MaximizedApp? maximized = settings.MoveForMaximizedApps ? _control.MaximizedAppInFront() : null;
        int drop = maximized is null ? 0 : settings.DropFor(maximized.ProcessName);
        int shift = maximized is null ? 0 : settings.ShiftFor(maximized.ProcessName);
        _window!.AvoidMaximizedApp(maximized?.Screen, drop, shift, maximized?.ProcessName);
        _viewModel.SetPlacement(_window.IsInDefaultCorner, _window.AvoidedApp, drop, shift, settings.Corner,
            _window.IsTemporary, _window.TemporaryForApp, maximized is not null && settings.HasOwnOffset(maximized.ProcessName));
    }

    /// <summary>
    /// True once a fullscreen app has been in front for a moment, so something that covers the screen only briefly
    /// (switching apps) never makes the window blink; it shows again as soon as the fullscreen app is gone.
    /// </summary>
    private bool FullscreenLastedAWhile()
    {
        if (!_control.IsFullscreenAppInFront())
        {
            _fullscreenSince = null;
            return false;
        }

        _fullscreenSince ??= _sessions.Now;
        return _sessions.Now - _fullscreenSince.Value >= FullscreenDelay;
    }

    private void Open(OverlaySettings settings)
    {
        var window = new SessionOverlayWindow(_control) { DataContext = _viewModel };
        window.PositionChosen += (_, position) =>
        {
            // Null: dropped near the default corner, which then counts as no saved position.
            _placedAt = (position?.X, position?.Y);
            _ = _settings.SavePositionAsync(position?.X, position?.Y);
        };

        window.TemporaryChanged += (_, _) => Update();
        // Closed from outside (Alt+F4 while Ctrl+Alt is held, another tool): opened again at the next tick.
        window.Closed += (_, _) =>
        {
            if (_window == window)
            {
                _window = null;
            }
        };
        window.AppOffsetChosen += (_, chosen) => _ = _settings.SaveAppOffsetAsync(chosen.App, (int)chosen.InVDips, (int)chosen.InHDips);

        _placedAt = (settings.X, settings.Y);
        window.SetCorner(settings.Corner);
        window.PlaceAt(settings.X, settings.Y);
        _window = window;
        window.Show();
    }

    private static Color ColorOf(Guid profileId, OverlaySettings settings)
    {
        string? chosen = settings.For(profileId).Color;
        return chosen is not null && Color.TryParse(chosen, out Color color) ? color : ProfileColors.For(profileId);
    }
}
