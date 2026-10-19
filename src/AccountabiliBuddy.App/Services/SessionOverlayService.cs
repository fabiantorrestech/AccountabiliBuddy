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
/// tab), which resets when the session ends. The all-day timeline shows on days with focus blocks; while the timer
/// shows it steps aside or stays above it (<see cref="OverlaySettings.TimelineInFocus"/>). With
/// <see cref="OverlaySettings.HideInFullscreen"/> the window also steps aside while a fullscreen app is in front
/// (checked every tick). Display only: nothing here changes what is blocked. Use from the UI thread only.
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

    private SessionOverlayWindow? _window;
    private (int? X, int? Y) _placedAt;
    private (bool On, bool Hidden) _lastStatus;

    public SessionOverlayService(
        SessionController sessions,
        ConfigurationService configuration,
        OverlaySettingsService settings,
        IOverlayWindowControl control,
        DayBarViewModel timelineDay)
    {
        _day = timelineDay;
        _viewModel = new OnScreenOverlayViewModel(_timer, timelineDay);
        _sessions = sessions;
        _configuration = configuration;
        _settings = settings;
        _control = control;
    }

    /// <summary>True when the timer was hidden for the session that is running now.</summary>
    public bool IsHiddenForSession { get; private set; }

    /// <summary>True while a session runs whose profile has the timer on (shown or hidden for now).</summary>
    public bool IsOnForSession => _sessions.State.IsActive && _settings.Current.For(_sessions.State.ProfileId).Enabled;

    /// <summary>Raised when the timer is shown, hidden or turned on or off for the session.</summary>
    public event EventHandler? Changed;

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
        _sessions.Ticked += (_, _) => Update();
        _settings.Changed += (_, _) => Update();
        _configuration.Changed += (_, _) => Update();
        _day.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DayBarViewModel.HasPlannedFocus))
            {
                Update();
            }
        };
        _day.Start();
        Update();
    }

    public void SetHiddenForSession(bool hidden)
    {
        IsHiddenForSession = hidden && _sessions.State.IsActive;
        Update();
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
        bool fullscreenInFront = settings.HideInFullscreen && _control.IsFullscreenAppInFront();
        if ((!showsTimer && !showsTimeline) || fullscreenInFront)
        {
            _window?.Close();
            _window = null;
            return;
        }

        if (showsTimer)
        {
            Guid profileId = state.ProfileId!.Value;
            string name = _configuration.Current.Profiles.FirstOrDefault(profile => profile.Id == profileId)?.Name ?? "Focus";
            _timer.Update(state, _sessions.Now, _sessions.TimeProvider.LocalTimeZone, name, ColorOf(profileId, settings), settings);
        }

        DateTime localNow = TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;
        _viewModel.Update(showsTimer, showsTimeline, localNow, settings);

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
    }

    private void Open(OverlaySettings settings)
    {
        var window = new SessionOverlayWindow(_control) { DataContext = _viewModel };
        window.PositionChosen += (_, position) =>
        {
            _placedAt = (position.X, position.Y);
            _ = _settings.SavePositionAsync(position.X, position.Y);
        };

        _placedAt = (settings.X, settings.Y);
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
