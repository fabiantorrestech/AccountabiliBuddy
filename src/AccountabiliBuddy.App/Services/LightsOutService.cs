using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Lights out (design K, opt-in per computer): while the work-hours guard keeps work hours here (on, a work part under
/// way, not stopped for today, goal not met, not a day off), the computer locks (or sleeps) when a work part starts and
/// after every 3-minute idle gap; waking lands on the gate. Never while a session runs here, during a calendar event,
/// or on a Local-only computer. Turning it off waits like the guard: refused while work is left today (stop for today
/// first). Use from the UI thread.
/// </summary>
public sealed class LightsOutService : IDisposable
{
    private const string DocumentKey = "lightsOut";
    public static readonly TimeSpan IdleGap = TimeSpan.FromMinutes(3);

    private readonly SqliteJsonDocumentStore _store;
    private readonly WorkHoursGuard _guard;
    private readonly SessionController _sessions;
    private readonly IUserIdleDetector _idle;
    private readonly IComputerLock _lock;
    private readonly Func<bool> _inCalendarEvent;
    private readonly Func<bool> _isLocalOnly;
    private bool? _wasActive;
    private DateTimeOffset? _lockedForInput;

    public LightsOutService(
        SqliteJsonDocumentStore store, WorkHoursGuard guard, SessionController sessions, IUserIdleDetector idle, IComputerLock computerLock,
        Func<bool> inCalendarEvent, Func<bool> isLocalOnly)
    {
        _store = store;
        _guard = guard;
        _sessions = sessions;
        _idle = idle;
        _lock = computerLock;
        _inCalendarEvent = inCalendarEvent;
        _isLocalOnly = isLocalOnly;
        _sessions.Ticked += OnTicked;
    }

    public LightsOutSettings Settings { get; private set; } = LightsOutSettings.Default;

    public bool CanUse => !_isLocalOnly();

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            Settings = Task.Run(() => _store.LoadAsync<LightsOutSettings>(DocumentKey)).GetAwaiter().GetResult() ?? LightsOutSettings.Default;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Settings = LightsOutSettings.Default;
        }
    }

    /// <returns>Null when saved; otherwise why not (turning it off while work is left today).</returns>
    public async Task<string?> SaveAsync(LightsOutSettings settings)
    {
        if (Settings.On && !settings.On && _guard.HasWorkLeftToday)
        {
            return "Lights out stays on while work is left today. Stop work hours for today first.";
        }

        Settings = settings;
        await _store.SaveAsync(DocumentKey, settings);
        Changed?.Invoke(this, EventArgs.Empty);
        return null;
    }

    /// <summary>Locks when a work part starts and after each 3-minute idle gap. Runs every second.</summary>
    public void CheckNow()
    {
        bool active = Settings.On && CanUse && _guard.IsGuarding && !_sessions.State.IsActive && !_inCalendarEvent();
        bool started = active && _wasActive == false;
        // The first check after launch only learns the state: opening the app mid-work isn't a work part starting.
        _wasActive = active;
        if (!active)
        {
            _lockedForInput = null;
            return;
        }

        TimeSpan idleFor = _idle.GetIdleTime();
        DateTimeOffset lastInput = _sessions.Now - idleFor;
        // The last input as read jitters by a few milliseconds between checks: new input means seconds later.
        bool newInput = _lockedForInput is not DateTimeOffset locked || lastInput - locked > TimeSpan.FromSeconds(5);
        bool idleGap = idleFor >= IdleGap && newInput;
        if (started || idleGap)
        {
            // Once per idle gap: the next lock waits for new input and another 3 minutes.
            _lockedForInput = lastInput;
            _ = Settings.Sleep ? _lock.Sleep() : _lock.Lock();
        }
    }

    public void Dispose()
    {
        _sessions.Ticked -= OnTicked;
    }

    private void OnTicked(object? sender, EventArgs e)
    {
        CheckNow();
    }
}
