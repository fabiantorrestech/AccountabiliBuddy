using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Pickups across PCs (design K, ADR 26): this PC notes each time the user starts using it again after 2 idle minutes.
/// Those during another PC's counted session go in this PC's sync file as pickups for that session (shown on that
/// PC's review card next to the phone's). On a PC whose work-hours guard keeps work hours, starting within a minute of
/// input on another PC (from that PC's input stretches in its file) is a pickup at once: a notice, no grace. Today's
/// own input stretches go in this PC's file for the other PCs. Kept 14 days (app-owned document "pickups.pc").
/// Use from the UI thread.
/// </summary>
public sealed class PcPickupsService : IDisposable
{
    private const string DocumentKey = "pickups.pc";
    private static readonly TimeSpan IdleBeforeStart = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ActiveIdle = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RunGap = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SwitchWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan KeptFor = TimeSpan.FromDays(14);

    private readonly SqliteJsonDocumentStore _store;
    private readonly SessionController _sessions;
    private readonly IUserIdleDetector _idle;
    private readonly ReceivedDevicesService _received;
    private readonly SyncIdentityService _identity;
    private readonly Func<bool> _isGuarded;
    private readonly Action<string, string> _notify;
    private List<DateTimeOffset> _starts = [];
    private List<SyncInputRun> _runs = [];
    private bool _wasIdle = true;
    private DateTimeOffset? _lastSwitchTold;
    private readonly HashSet<DateTimeOffset> _switchChecked = [];

    public PcPickupsService(
        SqliteJsonDocumentStore store, SessionController sessions, IUserIdleDetector idle, ReceivedDevicesService received, SyncIdentityService identity,
        Func<bool> isGuarded, Action<string, string> notify)
    {
        _store = store;
        _sessions = sessions;
        _idle = idle;
        _received = received;
        _identity = identity;
        _isGuarded = isGuarded;
        _notify = notify;
        _sessions.Ticked += OnTicked;
        _received.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(CheckRecentSwitches);
    }

    /// <summary>Raised when a new start or input stretch should go in this PC's file.</summary>
    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            Stored? stored = Task.Run(() => _store.LoadAsync<Stored>(DocumentKey)).GetAwaiter().GetResult();
            _starts = stored?.Starts?.ToList() ?? [];
            _runs = stored?.Runs?.ToList() ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _starts = [];
            _runs = [];
        }
    }

    /// <summary>Today's input stretches (local day), for this PC's sync file; null when none.</summary>
    public IReadOnlyList<SyncInputRun>? InputRunsToday()
    {
        DateTimeOffset dayStart = DayStart(_sessions.Now);
        List<SyncInputRun> today = _runs.Where(run => run.End > dayStart).ToList();
        return today.Count == 0 ? null : today;
    }

    /// <summary>
    /// For each counted session of another PC that started in the last 14 days: this PC's starts inside it (a running
    /// one until now), for this PC's sync file; null when there are none.
    /// </summary>
    public IReadOnlyList<SyncPickup>? PickupsForOtherPcs()
    {
        DateTimeOffset now = _sessions.Now;
        Guid self = _identity.DeviceId;
        var list = new List<SyncPickup>();
        foreach (ReceivedDevice device in _received.Devices.Where(device => device.File.Device.Kind == SyncDeviceCard.KindPc && device.File.Device.DeviceId != self))
        {
            foreach (SyncSession session in device.File.Sessions.Where(session => !session.Added && session.Start >= now - KeptFor && session.Start < now))
            {
                bool running = session.End is not DateTimeOffset ended || ended >= now;
                DateTimeOffset end = running ? now : session.End!.Value;
                // A running session counts up to this moment, a start this second included.
                List<DateTimeOffset> inside = _starts.Where(start => start >= session.Start && (running ? start <= end : start < end)).OrderBy(start => start).ToList();
                List<DateTimeOffset> marks = [session.Start, .. inside, end];
                TimeSpan longest = marks.Zip(marks.Skip(1), (a, b) => b - a).DefaultIfEmpty(TimeSpan.Zero).Max();
                list.Add(new SyncPickup(device.File.Device.DeviceId, session.Start, inside.Count, (int)longest.TotalMinutes, end));
            }
        }

        return list.Count == 0 ? null : list;
    }

    public void Dispose()
    {
        _sessions.Ticked -= OnTicked;
    }

    private void OnTicked(object? sender, EventArgs e) => Run();

    /// <summary>Notes a start or a longer stretch; runs every second.</summary>
    public void Run()
    {
        DateTimeOffset now = _sessions.Now;
        TimeSpan idleFor = _idle.GetIdleTime();
        if (idleFor >= IdleBeforeStart)
        {
            _wasIdle = true;
            return;
        }

        if (idleFor > ActiveIdle)
        {
            return;
        }

        DateTimeOffset lastInput = now - idleFor;
        bool changed = false;
        if (_wasIdle)
        {
            // Back at this PC after 2 idle minutes (or the first input since launch).
            _wasIdle = false;
            _starts.Add(lastInput);
            changed = true;
            CheckSwitch(lastInput);
        }

        if (_runs.Count > 0 && lastInput - _runs[^1].End <= RunGap)
        {
            // Extend the stretch; written at most once a minute so the file doesn't change every second.
            if (lastInput - _runs[^1].End >= TimeSpan.FromSeconds(30))
            {
                _runs[^1] = _runs[^1] with { End = lastInput };
                changed = true;
            }
        }
        else
        {
            _runs.Add(new SyncInputRun(lastInput, lastInput));
            changed = true;
        }

        if (changed)
        {
            _starts = _starts.Where(start => now - start < KeptFor).ToList();
            _runs = _runs.Where(run => now - run.End < TimeSpan.FromDays(2)).ToList();
            _ = SaveAsync();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    // A start within a minute of input on another PC, on a PC keeping work hours: a pickup, told at once.
    private void CheckSwitch(DateTimeOffset start)
    {
        if (!_isGuarded() || !_switchChecked.Add(start))
        {
            return;
        }

        Guid self = _identity.DeviceId;
        ReceivedDevice? from = _received.Devices
            .Where(device => device.File.Device.Kind == SyncDeviceCard.KindPc && device.File.Device.DeviceId != self)
            .FirstOrDefault(device => (device.File.InputRuns ?? []).Any(run => run.Start <= start && run.End >= start - SwitchWindow));
        if (from is null)
        {
            // That PC's newest stretch may not have arrived yet: looked at again when its file comes.
            _switchChecked.Remove(start);
            return;
        }

        if (_lastSwitchTold is DateTimeOffset told && start - told < TimeSpan.FromMinutes(2))
        {
            return;
        }

        _lastSwitchTold = start;
        _notify($"Straight from {from.File.Device.Name}: that's a pickup",
            "This computer is off-limits during work hours. Park the thought with Ctrl+Alt+N and head back.");
    }

    // A file from another PC came: the last 90 seconds' starts are looked at again (its stretch may have been late).
    private void CheckRecentSwitches()
    {
        DateTimeOffset now = _sessions.Now;
        foreach (DateTimeOffset start in _starts.Where(start => now - start < TimeSpan.FromSeconds(90)).ToList())
        {
            CheckSwitch(start);
        }
    }

    private DateTimeOffset DayStart(DateTimeOffset now)
    {
        DateTime local = TimeZoneInfo.ConvertTime(now, _sessions.TimeProvider.LocalTimeZone).Date;
        return new DateTimeOffset(local, _sessions.TimeProvider.LocalTimeZone.GetUtcOffset(local));
    }

    private async Task SaveAsync()
    {
        try
        {
            await _store.SaveAsync(DocumentKey, new Stored(_starts, _runs));
        }
        catch (SqliteException)
        {
            // Kept in memory; written with the next change.
        }
    }

    private sealed record Stored(List<DateTimeOffset> Starts, List<SyncInputRun> Runs);
}
