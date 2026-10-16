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

        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += OnTick;
    }

    public SessionState State => _engine.State;

    /// <summary>True during a focus phase. Ending early and editing settings need friction / are locked.</summary>
    public bool IsEnforcing => _engine.State.IsEnforcing;

    public DateTimeOffset Now => _timeProvider.GetUtcNow();

    public TimeProvider TimeProvider => _timeProvider;

    public BypassSettings BypassSettings => _configuration.Current.Bypass;

    /// <summary>Reason given the last time a focus session was ended early. Persisted to the log in M7.</summary>
    public string? LastEarlyEndReason { get; private set; }

    public event EventHandler<SessionState>? StateChanged;

    /// <summary>Raised every second, for countdown displays.</summary>
    public event EventHandler? Ticked;

    public void Start()
    {
        // Tick immediately so a scheduled block that is already running starts at launch.
        _engine.Tick();
        _timer.Start();
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

    /// <param name="earlyEndReason">The reason typed in the friction dialog, if one was required.</param>
    public void EndSession(string? earlyEndReason)
    {
        if (earlyEndReason is not null)
        {
            LastEarlyEndReason = earlyEndReason;
        }

        _engine.EndSession();
    }

    public void Dispose()
    {
        _timer.Stop();
        _configuration.Changed -= OnConfigurationChanged;
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
