using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Work hours on a personal computer (ADR 28, superseding ADR 27): while turned on, during the day plan's work (Watch)
/// parts and not stopped for today, the listed apps are minimized at once, also during a session, with one notice a minute
/// (or the gate, <see cref="OpenGate"/>). Stopping for today costs the end-early wait and reason; turning it off, taking an
/// app off the list or a day plan change that frees today's work waits (<see cref="DescribeCut"/>). Use from the UI thread.
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
    private DateOnly? _goalToldOn;

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
    /// Asked when a listed app was just minimized: true when the gate opened for it (then no notice). Set by
    /// <see cref="WorkGateService"/>.
    /// </summary>
    public Func<bool>? OpenGate { get; set; }

    /// <summary>
    /// Today's deep work against the goal, across the user's devices (earned time, design K): once met, this computer is
    /// open for the rest of the day (it only relaxes the guard). Null: no goal unlock.
    /// </summary>
    public Func<GoalProgress?>? Goal { get; set; }

    /// <summary>
    /// Counted focus on a date, for the day plan's Budget days (<see cref="DayWorkWindows"/>): once a day's budget is met
    /// its guarded windows are over. Set by the app; null = nothing counted.
    /// </summary>
    public Func<DateOnly, TimeSpan>? FocusedOn { get; set; }

    /// <summary>The night before a date from the sleep settings, for a Budget day whose plan has no Sleep part. Set by the app.</summary>
    public Func<DateOnly, SleepTimes?>? NightFallback { get; set; }

    /// <summary>The night fallback once a given plan is saved (saving re-derives the sleep settings' nights from the plan); for the hold's "after" side. Set by the app.</summary>
    public Func<DayPlan, Func<DateOnly, SleepTimes?>>? NightFallbackFor { get; set; }

    /// <summary>The day plan the guards read: the stored plan with calendar events laid over it (ADR 58). Set by the app; null = the stored plan.</summary>
    public Func<DayPlan>? GuardedPlan { get; set; }

    /// <summary>Lays the calendar events over a plan (ADR 58); a plan edit's freed work hours (<see cref="DescribeCut"/>) are measured on both plans this way. Set by the app; null = as stored.</summary>
    public Func<DayPlan, DayPlan>? WithCalendarEvents { get; set; }

    private DayPlan PlanInForce => GuardedPlan?.Invoke() ?? _configuration.Current.DayPlan;

    /// <summary>Today's goal is met (earned time): the guard has lifted, and stopping for today costs nothing.</summary>
    public bool IsGoalMet => Goal?.Invoke() is { Met: true };

    /// <summary>
    /// On, not stopped for today, and a work part still to come or under way today, whether or not a session runs or it's a
    /// break: turning it off or taking an app off the list then waits (stop for today first).
    /// </summary>
    public bool HasWorkLeftToday => WorkLeftToday();

    // On, not stopped for today, and a work part still to come or under way today (the goal aside).
    private bool WorkLeftToday()
    {
        if (!Settings.On || Settings.StoppedOn == Today())
        {
            return false;
        }

        DateTime now = LocalNow();
        DateTime endOfDay = now.Date.AddDays(1);
        return DayWorkWindows.WorkWindows(PlanInForce, new LocalWindow(now.Date.AddDays(-1), now.Date.AddDays(2)), now, FocusedOn, NightFallback)
            .Any(focus => focus.Window.End > now && focus.Window.Start < endOfDay);
    }

    /// <summary>
    /// Today is a day off on this computer (ADR 31: chosen in a Pre-Work part after its ten-minute wait): stops guarding
    /// until tomorrow without the end-early wait, which the day off has paid.
    /// </summary>
    public Task TakeDayOffAsync()
    {
        return SaveAsync(Settings with { StoppedOn = Today() });
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
        if (!IsGoalMet && policy.Check(requestedAt, _sessions.Now, reason) != BypassCheckResult.Allowed)
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
        if (OpenGate?.Invoke() == true)
        {
            return;
        }

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
        TellGoalMet();
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

    /// <summary>
    /// For <see cref="ConfigurationService.DayPlanHold"/>: a day plan change that frees some of today's remaining guarded
    /// work (windows or Budget hours, <see cref="DayPlanCut"/>) while guarding is on, not stopped for today and the goal not
    /// met waits (ADR 28, 47). The line describing it, or null.
    /// </summary>
    public string? DescribeCut(DayPlan before, DayPlan after)
    {
        if (!Settings.On || Settings.StoppedOn == Today() || IsGoalMet)
        {
            return null;
        }

        // The larger of the time freed on the stored plans and on the plans with the calendar events laid over them: an event
        // must neither hide a cut nor miss a deleted category that event parts use.
        DayPlan beforeInForce = WithCalendarEvents?.Invoke(before) ?? before;
        DayPlan afterInForce = WithCalendarEvents?.Invoke(after) ?? after;
        TimeSpan freed = DayPlanCut.FreedTodayWithEvents(before, after, beforeInForce, afterInForce, LocalNow(), FocusedOn, NightFallback, NightFallbackFor?.Invoke(after));
        if (freed <= TimeSpan.Zero)
        {
            return null;
        }

        string length = $"{(int)freed.TotalHours}h {freed.Minutes:00}m";
        string reasons = string.Join("; ", DayPlanChanges.DescribeToday(before, after, Today()));
        return reasons.Length == 0
            ? $"Day plan: frees {length} of today's work hours (work hours on this computer)"
            : $"Day plan: frees {length} of today's work hours: {reasons} (work hours on this computer)";
    }

    /// <summary>
    /// For <see cref="ConfigurationService.DayPlanHold"/>, on any computer: a day plan change that makes some of what is left of today less
    /// strict waits (ADR 63), such as Comms laid over Work or Rest over Pre-Work, with or without the work-hours guard. The line describing it, or null.
    /// </summary>
    public string? DescribeWeakening(DayPlan before, DayPlan after)
    {
        DayPlan beforeInForce = WithCalendarEvents?.Invoke(before) ?? before;
        DayPlan afterInForce = WithCalendarEvents?.Invoke(after) ?? after;
        TimeSpan onStored = DayPlanCut.WeakenedToday(before, after, LocalNow(), FocusedOn, NightFallback);
        TimeSpan inForce = DayPlanCut.WeakenedToday(beforeInForce, afterInForce, LocalNow(), FocusedOn, NightFallback);
        TimeSpan weakened = inForce > onStored ? inForce : onStored;
        if (weakened <= TimeSpan.Zero)
        {
            return null;
        }

        string length = $"{(int)weakened.TotalHours}h {weakened.Minutes:00}m";
        string reasons = string.Join("; ", DayPlanChanges.DescribeToday(before, after, Today()));
        return reasons.Length == 0
            ? $"Day plan: makes {length} of today less strict"
            : $"Day plan: makes {length} of today less strict: {reasons}";
    }

    public void Dispose()
    {
        _sessions.Ticked -= OnTicked;
        _sessions.StateChanged -= OnStateChanged;
    }

    /// <summary>The end of the work part now, when guarding; null otherwise.</summary>
    private DateTime? GuardingUntil()
    {
        if (!Settings.On || Settings.StoppedOn == Today() || IsGoalMet)
        {
            return null;
        }

        DateTime now = LocalNow();
        var around = new LocalWindow(now.Date.AddDays(-1), now.Date.AddDays(2));
        // Work parts that touch (Work, then Comms, then Work) are one stretch of work hours.
        LocalWindow? stretch = DayTimeline.Union(DayWorkWindows.WorkWindows(PlanInForce, around, now, FocusedOn, NightFallback).Select(focus => focus.Window))
            .Where(window => window.Start <= now && now < window.End)
            .Select(window => (LocalWindow?)window)
            .FirstOrDefault();
        return stretch?.End;
    }

    // One notice the first time the goal opens this computer on a day it would still guard.
    private void TellGoalMet()
    {
        if (Goal?.Invoke() is not { Met: true } progress || _goalToldOn == Today() || !WorkLeftToday())
        {
            return;
        }

        _goalToldOn = Today();
        _notify($"{progress.Text} · this computer is open", "Today's deep-work goal is met, so work hours are over here for today.");
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
