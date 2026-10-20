using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Drives the <see cref="SessionEngine"/> once a second on the UI thread and keeps its schedule
/// in sync with the configuration. Views and the tray read session state from here.
/// </summary>
public sealed class SessionController : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly SessionEngine _engine;
    private readonly ConfigurationService _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly DispatcherTimer _timer;

    public SessionController(SessionEngine engine, ConfigurationService configuration, TimeProvider timeProvider)
    {
        _engine = engine;
        _configuration = configuration;
        _timeProvider = timeProvider;

        _engine.SetSchedule(configuration.Current.Schedule);
        _configuration.Changed += OnConfigurationChanged;
        _engine.StateChanged += (_, state) => StateChanged?.Invoke(this, state);
        _engine.SessionCompleted += (_, state) => SessionCompleted?.Invoke(this, state);

        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += OnTick;
    }

    public SessionState State => _engine.State;

    /// <summary>True during a focus phase. Ending early and editing settings need friction / are locked.</summary>
    public bool IsEnforcing => _engine.State.IsEnforcing;

    public DateTimeOffset Now => _timeProvider.GetUtcNow();

    public TimeProvider TimeProvider => _timeProvider;

    /// <summary>
    /// Friction for ending the running session early: the configured wait and reason, or none for a session that ends on
    /// command (see <see cref="BypassPolicy.ForSession"/>).
    /// </summary>
    public BypassSettings BypassSettings => BypassPolicy.ForSession(_configuration.Current.Bypass, State);

    /// <summary>A scheduled block or a session with no set end: it ends at once, with no wait or reason.</summary>
    public bool EndsOnCommand => BypassPolicy.EndsOnCommand(State);

    /// <summary>Reason given the last time a focus session was ended early (also logged via <see cref="SessionEnded"/>).</summary>
    public string? LastEarlyEndReason { get; private set; }

    public event EventHandler<SessionState>? StateChanged;

    /// <summary>Raised every second, for countdown displays.</summary>
    public event EventHandler? Ticked;

    /// <summary>Raised with the finished state when a session runs out on its own (not when ended by the user).</summary>
    public event EventHandler<SessionState>? SessionCompleted;

    /// <summary>Raised when the user ends a session by hand (with the friction reason, if one was required).</summary>
    public event EventHandler<SessionEnd>? SessionEnded;

    /// <summary>Raised at launch when a session saved before the app stopped was put back (before catching up).</summary>
    public event EventHandler<SessionResume>? SessionResumed;

    /// <summary>True while a saved session is being put back: its StateChanged continues a session rather than starting one.</summary>
    public bool IsResuming { get; private set; }

    public SessionCheckpoint CreateCheckpoint() => _engine.CreateCheckpoint();

    /// <summary>Keeps scheduled blocks from starting before <paramref name="until"/> (an unlock); null lifts the hold.</summary>
    public void HoldSchedule(DateTimeOffset? until) => _engine.HoldSchedule(until);

    /// <param name="saved">The session saved before the app last stopped, if any; a running one is resumed.</param>
    /// <returns>The resumed session, or null when nothing was resumed.</returns>
    public SessionResume? Start(SavedSession? saved)
    {
        SessionResume? resume = saved is null ? null : TryResume(saved);

        // Tick immediately so a scheduled block that is already running starts at launch,
        // and a resumed session catches up on phases that ran out while the app was closed.
        _engine.Tick();
        _timer.Start();
        return resume;
    }

    public bool StartManual(Guid profileId, TimeSpan? duration)
    {
        return _engine.StartManual(profileId, duration);
    }

    public bool StartPomodoro(Guid profileId, int? totalCycles)
    {
        PomodoroSettings settings = _configuration.Current.PomodoroDefaults with { TotalCycles = totalCycles };
        return _engine.StartPomodoro(profileId, settings);
    }

    /// <summary>One focus block followed by one break, then the session ends (quick session with a break).</summary>
    public bool StartFocusThenBreak(Guid profileId, TimeSpan focus, TimeSpan breakLength)
    {
        // One cycle never reaches the long-break count, so the single break is shown as a short break.
        var settings = new PomodoroSettings(focus, breakLength, breakLength, CyclesBeforeLongBreak: 2, TotalCycles: 1);
        return _engine.StartPomodoro(profileId, settings, breakAfterLastCycle: true);
    }

    /// <param name="earlyEndReason">The reason typed in the friction dialog, if one was required.</param>
    /// <param name="suppressSchedule">See <see cref="SessionEngine.EndSession"/>.</param>
    public void EndSession(string? earlyEndReason, bool suppressSchedule = true)
    {
        if (earlyEndReason is not null)
        {
            LastEarlyEndReason = earlyEndReason;
        }

        SessionState ending = _engine.State;
        _engine.EndSession(suppressSchedule);
        if (ending.IsActive)
        {
            SessionEnded?.Invoke(this, new SessionEnd(ending, earlyEndReason));
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _configuration.Changed -= OnConfigurationChanged;
    }

    private SessionResume? TryResume(SavedSession saved)
    {
        bool restored;
        IsResuming = true;
        try
        {
            restored = _engine.Restore(saved.Checkpoint);
        }
        finally
        {
            IsResuming = false;
        }

        if (!restored)
        {
            return null;
        }

        var resume = new SessionResume(saved.Checkpoint.State, saved.LastAliveAt, Now);
        SessionResumed?.Invoke(this, resume);
        return resume;
    }

    private void OnConfigurationChanged(object? sender, ProfileDocument document)
    {
        _engine.SetSchedule(document.Schedule);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _engine.Tick();
        Ticked?.Invoke(this, EventArgs.Empty);
    }
}
