using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Work hours on a personal computer (ADR 27): while turned on, during the day plan's work (Watch) parts, with no session the
/// user started running here and not stopped for today, the listed apps are minimized at once, with one notice a minute.
/// Stopping for today, like turning it off during work hours, costs the end-early wait and reason. Use from the UI thread.
/// </summary>
public sealed class WorkHoursGuard : IDisposable
{
    private const string DocumentKey = "workHours";
    private static readonly TimeSpan NoticeGap = TimeSpan.FromMinutes(1);

    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly SqliteJsonDocumentStore _store;
    private readonly IEnforcer _enforcer;
    private readonly Func<IReadOnlyList<ActivitySnapshot>> _currentWindows;
    private readonly Action<string, string> _notify;
    private DateTimeOffset _lastNotice = DateTimeOffset.MinValue;
    private bool _wasGuarding;

    /// <param name="currentWindows">The window in front on each monitor (swept when work hours start).</param>
    /// <param name="notify">Shows a notice (title, message).</param>
    public WorkHoursGuard(
        SessionController sessions,
        ConfigurationService configuration,
        SqliteJsonDocumentStore store,
        IEnforcer enforcer,
        Func<IReadOnlyList<ActivitySnapshot>> currentWindows,
        Action<string, string> notify)
    {
        _sessions = sessions;
        _configuration = configuration;
        _store = store;
        _enforcer = enforcer;
        _currentWindows = currentWindows;
        _notify = notify;
        _sessions.Ticked += OnTicked;
        _sessions.StateChanged += OnStateChanged;
    }

    public WorkHoursSettings Settings { get; private set; } = WorkHoursSettings.Default;

    /// <summary>
    /// On, not stopped for today, and a work part still to come or under way today, whether or not a session runs or it's a
    /// break: turning it off or taking an app off the list then waits (stop for today first).
    /// </summary>
    public bool HasWorkLeftToday
    {
        get
        {
            if (!Settings.On || Settings.StoppedOn == Today())
            {
                return false;
            }

            DateTime now = LocalNow();
            DateTime endOfDay = now.Date.AddDays(1);
            return DayTimeline.Planned(_configuration.Current.DayPlan, new LocalWindow(now.Date.AddDays(-1), now.Date.AddDays(2)))
                .Any(focus => focus.Window.End > now && focus.Window.Start < endOfDay);
        }
    }

    /// <summary>How long stopping for today waits (the end-early wait).</summary>
    public TimeSpan StopWait => _configuration.Current.Bypass.WaitDuration;

    /// <summary>True while the listed apps wait.</summary>
    public bool IsGuarding => GuardingUntil() is not null;

    /// <summary>E.g. "Work until 5:00 PM"; empty when not guarding.</summary>
    public string UntilText => GuardingUntil() is DateTime until ? $"Work until {until:t}" : string.Empty;

    /// <summary>Raised when guarding starts or stops, or the settings change.</summary>
    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            WorkHoursSettings? loaded = Task.Run(() => _store.LoadAsync<WorkHoursSettings>(DocumentKey)).GetAwaiter().GetResult();
            Settings = loaded is null ? WorkHoursSettings.Default : loaded with { Apps = Normalize(loaded.Apps ?? WorkHoursSettings.DefaultApps) };
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Settings = WorkHoursSettings.Default;
        }

        _wasGuarding = IsGuarding;
    }

    /// <summary>Stricter: at once.</summary>
    public Task TurnOnAsync()
    {
        return SaveAsync(Settings with { On = true });
    }

    /// <summary>Looser: refused while work is left today (stop for today first, with its wait and reason).</summary>
    /// <returns>False when refused.</returns>
    public async Task<bool> TurnOffAsync()
    {
        if (HasWorkLeftToday)
        {
            return false;
        }

        await SaveAsync(Settings with { On = false });
        return true;
    }

    /// <summary>The apps that wait (stricter when added; removing one while guarding waits like any looser change).</summary>
    /// <returns>False when an app would be removed while work is left today.</returns>
    public async Task<bool> SetAppsAsync(IEnumerable<string> apps)
    {
        IReadOnlyList<string> next = Normalize(apps);
        if (HasWorkLeftToday && Settings.Apps.Any(app => !next.Contains(app)))
        {
            return false;
        }

        await SaveAsync(Settings with { Apps = next });
        return true;
    }

    /// <summary>Stops guarding until tomorrow, after the end-early wait from <paramref name="requestedAt"/> and a reason.</summary>
    /// <returns>False while the wait runs or the reason is too short.</returns>
    public async Task<bool> StopForTodayAsync(string reason, DateTimeOffset requestedAt)
    {
        var policy = new BypassPolicy(_configuration.Current.Bypass);
        if (policy.Check(requestedAt, _sessions.Now, reason) != BypassCheckResult.Allowed)
        {
            return false;
        }

        await SaveAsync(Settings with { StoppedOn = Today() });
        return true;
    }

    /// <summary>How long the stop-for-today wait has left, from <paramref name="requestedAt"/>.</summary>
    public TimeSpan WaitLeft(DateTimeOffset requestedAt)
    {
        return new BypassPolicy(_configuration.Current.Bypass).RemainingWait(requestedAt, _sessions.Now);
    }

    /// <summary>A window came to the front: a listed app waits while guarding.</summary>
    public void OnActivity(ActivitySnapshot snapshot)
    {
        if (!IsGuarding || snapshot.IsEmptyDesktop || !Settings.Apps.Contains(snapshot.ProcessName.ToLowerInvariant()))
        {
            return;
        }

        _ = _enforcer.MinimizeWindowAsync(snapshot.WindowId);
        DateTimeOffset now = _sessions.Now;
        if (now - _lastNotice >= NoticeGap)
        {
            _lastNotice = now;
            _notify(UntilText, $"{snapshot.ProcessName} waits until work is done. Park the idea with Ctrl+Alt+N.");
        }
    }

    /// <summary>Notices guarding starting or stopping, and sweeps the windows in front when it starts. Runs every second.</summary>
    public void CheckNow()
    {
        bool guarding = IsGuarding;
        if (guarding == _wasGuarding)
        {
            return;
        }

        _wasGuarding = guarding;
        if (guarding)
        {
            foreach (ActivitySnapshot snapshot in _currentWindows())
            {
                OnActivity(snapshot);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _sessions.Ticked -= OnTicked;
        _sessions.StateChanged -= OnStateChanged;
    }

    /// <summary>The end of the work part now, when guarding; null otherwise.</summary>
    private DateTime? GuardingUntil()
    {
        if (!Settings.On || _sessions.State.IsActive || Settings.StoppedOn == Today())
        {
            return null;
        }

        DateTime now = LocalNow();
        var around = new LocalWindow(now.Date.AddDays(-1), now.Date.AddDays(2));
        PlannedFocus? part = DayTimeline.Planned(_configuration.Current.DayPlan, around)
            .FirstOrDefault(focus => focus.Window.Start <= now && now < focus.Window.End);
        return part?.Window.End;
    }

    private async Task SaveAsync(WorkHoursSettings settings)
    {
        Settings = settings;
        await _store.SaveAsync(DocumentKey, settings);
        _wasGuarding = !IsGuarding; // Let CheckNow announce the new state (and sweep when it starts).
        CheckNow();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnTicked(object? sender, EventArgs e)
    {
        CheckNow();
    }

    private void OnStateChanged(object? sender, SessionState state)
    {
        CheckNow();
    }

    private DateTime LocalNow()
    {
        return TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;
    }

    private DateOnly Today()
    {
        return DateOnly.FromDateTime(LocalNow());
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string> apps)
    {
        return apps
            .Select(app => app.Trim().ToLowerInvariant())
            .Select(app => app.EndsWith(".exe", StringComparison.Ordinal) ? app[..^4] : app)
            .Where(app => app.Length > 0)
            .Distinct()
            .ToList();
    }
}
