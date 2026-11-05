using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The quit safeguard (ADR 59): when the tray's Quit is refused, and the watchdog that brings the app back if it is killed meanwhile.
/// Device-local (documents "quit.guard", "quit.guard.pending"), not synced and not backed up, so restoring a backup can't undo it.
/// Stricter changes apply at once; looser ones wait the looser-change delay and only apply with no session running; nothing changes
/// during a focus session. Leaving "All the time" also needs the spare key. Use from the UI thread only.
/// </summary>
public sealed class QuitGuardService
{
    private const string DocumentKey = "quit.guard";
    private const string PendingKey = "quit.guard.pending";

    // How often the watchdog is told what holds now, between changes of the settings or the session.
    private static readonly TimeSpan WatchdogRefresh = TimeSpan.FromSeconds(15);

    private readonly SqliteJsonDocumentStore _store;
    private readonly TimeProvider _time;
    private readonly SessionController _sessions;
    private readonly ColdTurkeyKeysService _keys;
    private readonly IQuitWatchdog _watchdog;
    private readonly Func<TimeSpan> _looserWait;
    private readonly Func<DayPlan> _plan;
    private readonly Action _keepStartingAtSignIn;
    private bool _watching;

    // The app is ending on purpose: a late tick must not arm the watchdog again.
    private bool _ending;
    private DateTimeOffset _lastKept = DateTimeOffset.MinValue;

    /// <param name="looserWait">How long a looser change waits: the looser-change wait of the configuration in effect.</param>
    /// <param name="plan">The day plan in effect, calendar events included.</param>
    /// <param name="keepStartingAtSignIn">Makes sure the app starts at sign-in, while the safeguard is on.</param>
    public QuitGuardService(
        SqliteJsonDocumentStore store, TimeProvider time, SessionController sessions, ColdTurkeyKeysService keys, IQuitWatchdog watchdog,
        Func<TimeSpan> looserWait, Func<DayPlan> plan, Action keepStartingAtSignIn)
    {
        _store = store;
        _time = time;
        _sessions = sessions;
        _keys = keys;
        _watchdog = watchdog;
        _looserWait = looserWait;
        _plan = plan;
        _keepStartingAtSignIn = keepStartingAtSignIn;
    }

    /// <summary>What is in force now.</summary>
    public QuitGuardSettings Current { get; private set; } = QuitGuardSettings.Default;

    /// <summary>A looser change still waiting; null when none is.</summary>
    public PendingQuitGuard? Pending { get; private set; }

    /// <summary>What the settings page shows: the last change the user made.</summary>
    public QuitGuardSettings Editing => Pending?.Settings ?? Current;

    /// <summary>The focus-session test the Cold Turkey keys use: a session the user started, breaks included.</summary>
    public bool InFocusSession => _keys.InFocusSession;

    public Func<DateOnly, TimeSpan>? FocusedOn { get; set; }

    public Func<DateOnly, SleepTimes?>? NightFallback { get; set; }

    /// <summary>Work ended early (ADR 65), cut out of the work windows: that time counts as not work. Set by the app; null = none.</summary>
    public Func<IReadOnlyList<LocalWindow>>? WorkEnded { get; set; }

    public event EventHandler? Changed;

    public DayPlan Plan => _plan();

    public void LoadBlocking()
    {
        try
        {
            Current = Task.Run(() => _store.LoadAsync<QuitGuardSettings>(DocumentKey)).GetAwaiter().GetResult() ?? QuitGuardSettings.Default;
            Pending = Task.Run(() => _store.LoadAsync<PendingQuitGuard>(PendingKey)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = QuitGuardSettings.Default;
            Pending = null;
        }
    }

    public void Start()
    {
        _sessions.Ticked += (_, _) =>
        {
            _ = ApplyPendingIfDueAsync();
            Refresh(force: false);
        };
        _sessions.StateChanged += (_, _) =>
        {
            _ = ApplyPendingIfDueAsync();
            Refresh(force: true);
        };
        Refresh(force: true);
    }

    /// <summary>What holds now; null when the app may be quit.</summary>
    public QuitLock? CurrentLock()
    {
        DayPlan plan = _plan();
        DateTime now = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).DateTime;
        return QuitGuard.Check(Current, InFocusSession, plan, now, at => DayWorkWindows.PartAt(plan, at, FocusedOn, NightFallback, WorkEnded?.Invoke()));
    }

    /// <summary>The notice for a refused Quit.</summary>
    public static string Describe(QuitLock quitLock)
    {
        return quitLock.Reason switch
        {
            QuitLockReason.AllTheTime => "\"All the time\" is on, so AccountabiliBuddy keeps running. Turning it off takes your spare key and a wait, in Settings › Guards.",
            QuitLockReason.Session => "A focus session is running. End the session first, then you can quit.",
            _ => quitLock.Until is DateTime until
                ? $"Quit is locked during {quitLock.CategoryName}, until {until:t}."
                : $"Quit is locked during {quitLock.CategoryName}.",
        };
    }

    /// <summary>
    /// A change from the settings page. Stricter parts apply now; a looser change waits, and replaces one already waiting, which starts
    /// the wait over. Going back to what is in force drops the waiting change.
    /// </summary>
    /// <param name="spareKey">The key typed for leaving "All the time"; null when none was asked for yet.</param>
    public async Task<QuitGuardChange> ChangeAsync(QuitGuardSettings wanted, string? spareKey = null)
    {
        if (InFocusSession)
        {
            return QuitGuardChange.RefusedInFocus;
        }

        bool entersHardcore = wanted.On && wanted.AllTheTime && !(Current.On && Current.AllTheTime);
        if (entersHardcore && !_keys.HasSpareKey)
        {
            return QuitGuardChange.NeedsSpareKeySetup;
        }

        if (QuitGuard.LeavesHardcore(Current, wanted))
        {
            if (spareKey is null)
            {
                return QuitGuardChange.NeedsSpareKey;
            }

            if (!_keys.IsSpareKey(spareKey))
            {
                return QuitGuardChange.WrongKey;
            }
        }

        DayPlan plan = _plan();
        if (!QuitGuard.Loosens(Current, wanted, plan))
        {
            Current = wanted;
            await ClearPendingAsync();
            await FinishChangeAsync();
            return QuitGuardChange.Saved;
        }

        Current = QuitGuard.StricterOf(Current, wanted, plan);
        Pending = new PendingQuitGuard(wanted, _time.GetUtcNow() + _looserWait());
        await _store.SaveAsync(PendingKey, Pending);
        await FinishChangeAsync();
        return QuitGuardChange.Waiting;
    }

    /// <summary>Drops a waiting looser change: what is in force stays. Needs no key, since it loosens nothing.</summary>
    public async Task CancelPendingAsync()
    {
        if (Pending is null)
        {
            return;
        }

        await ClearPendingAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The app is ending on purpose: nothing may bring it back.</summary>
    public void StandDown()
    {
        _ending = true;
        _watching = false;
        _watchdog.StandDown();
    }

    private async Task ApplyPendingIfDueAsync()
    {
        if (Pending is not PendingQuitGuard pending || _sessions.State.IsActive || _time.GetUtcNow() < pending.ReadyAt)
        {
            return;
        }

        Current = pending.Settings;
        await ClearPendingAsync();
        await FinishChangeAsync();
    }

    private async Task ClearPendingAsync()
    {
        Pending = null;
        await _store.DeleteAsync(PendingKey);
    }

    private async Task FinishChangeAsync()
    {
        await _store.SaveAsync(DocumentKey, Current);
        Refresh(force: true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Refresh(bool force)
    {
        if (_ending)
        {
            return;
        }

        if (!Current.On)
        {
            if (_watching)
            {
                _watching = false;
                _watchdog.StandDown();
            }

            return;
        }

        DateTimeOffset now = _time.GetUtcNow();
        if (!force && _watching && now - _lastKept < WatchdogRefresh)
        {
            return;
        }

        _lastKept = now;
        _watching = true;
        _keepStartingAtSignIn();
        _watchdog.Keep(CurrentLock() is not null);
    }
}
