using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Timers;

/// <summary>
/// This PC's timers and alarms (V1, local only: document "timers", never sent anywhere). Several at once, named and
/// editable. Checked on the app's 1-second tick against the trusted clock; a timer keeps its end time and an alarm its
/// time of day, so both survive a restart. Reminders only: they never touch sessions, enforcement or History, and a
/// running session never holds them back. One that came due more than an hour ago (the app was closed or the PC
/// asleep) is marked done without ringing. Alarms don't wake a sleeping PC (V1). Use from the UI thread only.
/// </summary>
public sealed class TimersService
{
    private const string DocumentKey = "timers";
    private static readonly TimeSpan TooLate = TimeSpan.FromHours(1);
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    /// <summary>Snooze on a ringing alarm.</summary>
    public static readonly TimeSpan Snooze = TimeSpan.FromMinutes(9);

    private readonly SqliteJsonDocumentStore _store;
    private readonly SessionController _sessions;
    private List<TimerItem> _timers = [];
    private List<AlarmItem> _alarms = [];

    public TimersService(SqliteJsonDocumentStore store, SessionController sessions)
    {
        _store = store;
        _sessions = sessions;
    }

    /// <summary>In the order they were added.</summary>
    public IReadOnlyList<TimerItem> Timers => _timers;

    /// <summary>In the order they were added.</summary>
    public IReadOnlyList<AlarmItem> Alarms => _alarms;

    /// <summary>Trusted time.</summary>
    public DateTimeOffset Now => _sessions.Now;

    /// <summary>This PC's wall-clock time (what alarms are set in).</summary>
    public DateTime LocalNow => TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;

    /// <summary>Raised after any timer or alarm changed (not on every tick).</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when a timer's time is up (on time, not long after).</summary>
    public event EventHandler<TimerItem>? TimerUp;

    /// <summary>Raised when an alarm (or its snooze) rings.</summary>
    public event EventHandler<AlarmItem>? AlarmUp;

    public void LoadBlocking()
    {
        try
        {
            TimersDocument? loaded = Task.Run(() => _store.LoadAsync<TimersDocument>(DocumentKey)).GetAwaiter().GetResult();
            _timers = loaded?.Timers?.ToList() ?? [];
            _alarms = loaded?.Alarms?.Select(alarm => alarm with { Days = alarm.Days ?? [] }).ToList() ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _timers = [];
            _alarms = [];
        }
    }

    public void Start()
    {
        _sessions.Ticked += (_, _) => Check();
        Check();
    }

    public TimerItem? FindTimer(Guid id) => _timers.FirstOrDefault(timer => timer.Id == id);

    public AlarmItem? FindAlarm(Guid id) => _alarms.FirstOrDefault(alarm => alarm.Id == id);

    /// <summary>Adds a timer and starts it at once. A blank name is "Timer".</summary>
    public Task AddTimerAsync(string? name, TimeSpan length)
    {
        if (length <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        _timers.Add(new TimerItem(Guid.NewGuid(), NameOr(name, TimerItem.DefaultName), length, TimerState.Running, Now + length, null, false));
        return SaveAsync();
    }

    public Task PauseAsync(Guid id)
    {
        return UpdateTimer(id, timer => timer.State != TimerState.Running ? timer
            : timer with { State = TimerState.Paused, Remaining = timer.LeftAt(Now), EndsAt = null });
    }

    /// <summary>Runs a paused timer on; a done one starts over from its whole length.</summary>
    public Task ResumeAsync(Guid id)
    {
        return UpdateTimer(id, timer => timer.State switch
        {
            TimerState.Paused => timer with { State = TimerState.Running, EndsAt = Now + (timer.Remaining ?? timer.Length), Remaining = null },
            TimerState.Done => timer with { State = TimerState.Running, EndsAt = Now + timer.Length, Remaining = null, Dismissed = false },
            _ => timer,
        });
    }

    /// <summary>A minute more; a done timer runs again for a minute (the Today row's and on-screen window's "+1 min").</summary>
    public Task AddMinuteAsync(Guid id) => AddMinutesAsync(id, 1);

    /// <summary>Some minutes more; a done timer runs again for that long (the ring window's "+5 min" and the rest of its menu).</summary>
    public Task AddMinutesAsync(Guid id, int minutes)
    {
        TimeSpan extra = TimeSpan.FromMinutes(minutes);
        return UpdateTimer(id, timer => timer.State switch
        {
            TimerState.Running => timer with { EndsAt = (timer.EndsAt ?? Now) + extra },
            TimerState.Paused => timer with { Remaining = (timer.Remaining ?? timer.Length) + extra },
            _ => timer with { State = TimerState.Running, EndsAt = Now + extra, Remaining = null, Dismissed = false },
        });
    }

    /// <summary>Back to its whole length, paused.</summary>
    public Task ResetAsync(Guid id)
    {
        return UpdateTimer(id, timer => timer with { State = TimerState.Paused, EndsAt = null, Remaining = timer.Length, Dismissed = false });
    }

    /// <summary>Done now, quietly, and off the on-screen window (its ✕).</summary>
    public Task StopAsync(Guid id)
    {
        return UpdateTimer(id, timer => timer with { State = TimerState.Done, EndsAt = null, Remaining = null, Dismissed = true });
    }

    /// <summary>A done timer leaves the on-screen window and stops ringing; it stays on Today.</summary>
    public Task DismissTimerAsync(Guid id)
    {
        return UpdateTimer(id, timer => timer.State == TimerState.Done ? timer with { Dismissed = true } : timer);
    }

    /// <summary>
    /// A new name and length. The time left becomes the new length (running ones count down from it now; a done one
    /// waits paused).
    /// </summary>
    public Task EditTimerAsync(Guid id, string? name, TimeSpan length)
    {
        if (length <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        return UpdateTimer(id, timer =>
        {
            TimerItem renamed = timer with { Name = NameOr(name, TimerItem.DefaultName) };
            if (length == timer.Length)
            {
                return renamed;
            }

            return timer.State == TimerState.Running
                ? renamed with { Length = length, EndsAt = Now + length }
                : renamed with { Length = length, State = TimerState.Paused, Remaining = length, EndsAt = null, Dismissed = false };
        });
    }

    public Task DeleteTimerAsync(Guid id)
    {
        return _timers.RemoveAll(timer => timer.Id == id) == 0 ? Task.CompletedTask : SaveAsync();
    }

    /// <summary>Adds an alarm, on. No days = once. A blank name is "Alarm".</summary>
    public Task AddAlarmAsync(string? name, TimeOnly time, IReadOnlyList<DayOfWeek> days)
    {
        _alarms.Add(new AlarmItem(Guid.NewGuid(), NameOr(name, AlarmItem.DefaultName), time, days.Distinct().ToList(), true, LocalNow, null));
        return SaveAsync();
    }

    /// <summary>A new name, time and days; it's on afterwards and rings at the next chosen time from now.</summary>
    public Task EditAlarmAsync(Guid id, string? name, TimeOnly time, IReadOnlyList<DayOfWeek> days)
    {
        return UpdateAlarm(id, alarm => alarm with
        {
            Name = NameOr(name, AlarmItem.DefaultName),
            Time = time,
            Days = days.Distinct().ToList(),
            IsOn = true,
            ArmedFrom = LocalNow,
            SnoozedUntil = null,
        });
    }

    /// <summary>On (from now) or off (a snooze goes too).</summary>
    public Task SetAlarmOnAsync(Guid id, bool on)
    {
        return UpdateAlarm(id, alarm => alarm.IsOn == on ? alarm
            : alarm with { IsOn = on, ArmedFrom = LocalNow, SnoozedUntil = null });
    }

    public Task SnoozeAlarmAsync(Guid id, int minutes = 9)
    {
        return UpdateAlarm(id, alarm => alarm with { SnoozedUntil = LocalNow.AddMinutes(minutes) });
    }

    /// <summary>A ringing alarm stops; a snooze goes. Repeating ones ring again on their next day.</summary>
    public Task DismissAlarmAsync(Guid id)
    {
        return UpdateAlarm(id, alarm => alarm.SnoozedUntil is null ? alarm : alarm with { SnoozedUntil = null });
    }

    public Task DeleteAlarmAsync(Guid id)
    {
        return _alarms.RemoveAll(alarm => alarm.Id == id) == 0 ? Task.CompletedTask : SaveAsync();
    }

    private void Check()
    {
        DateTimeOffset now = Now;
        DateTime localNow = LocalNow;
        var timersUp = new List<TimerItem>();
        var alarmsUp = new List<AlarmItem>();
        bool changed = false;

        for (int index = 0; index < _timers.Count; index++)
        {
            TimerItem timer = _timers[index];
            if (timer.State != TimerState.Running || timer.EndsAt is not DateTimeOffset end || now < end)
            {
                continue;
            }

            bool late = now - end > TooLate;
            TimerItem done = timer with { State = TimerState.Done, EndsAt = null, Remaining = null, Dismissed = late };
            _timers[index] = done;
            changed = true;
            if (!late)
            {
                timersUp.Add(done);
            }
        }

        for (int index = 0; index < _alarms.Count; index++)
        {
            AlarmItem alarm = _alarms[index];
            if (alarm.NextRing() is not DateTime due || localNow < due)
            {
                continue;
            }

            bool late = localNow - due > TooLate;
            AlarmItem rung = alarm.SnoozedUntil is not null
                ? alarm with { SnoozedUntil = null }
                // Missed by a lot: skip every missed time, not one per tick.
                : alarm with { ArmedFrom = late ? localNow : due, IsOn = alarm.IsOn && !alarm.IsOnce };
            _alarms[index] = rung;
            changed = true;
            if (!late)
            {
                alarmsUp.Add(rung);
            }
        }

        // Late ones changed too, without a ring: saved all the same.
        if (!changed)
        {
            return;
        }

        _ = SaveAsync();
        foreach (TimerItem timer in timersUp)
        {
            TimerUp?.Invoke(this, timer);
        }

        foreach (AlarmItem alarm in alarmsUp)
        {
            AlarmUp?.Invoke(this, alarm);
        }
    }

    private Task UpdateTimer(Guid id, Func<TimerItem, TimerItem> change)
    {
        int index = _timers.FindIndex(timer => timer.Id == id);
        if (index < 0)
        {
            return Task.CompletedTask;
        }

        TimerItem changed = change(_timers[index]);
        if (changed == _timers[index])
        {
            return Task.CompletedTask;
        }

        _timers[index] = changed;
        return SaveAsync();
    }

    private Task UpdateAlarm(Guid id, Func<AlarmItem, AlarmItem> change)
    {
        int index = _alarms.FindIndex(alarm => alarm.Id == id);
        if (index < 0)
        {
            return Task.CompletedTask;
        }

        AlarmItem changed = change(_alarms[index]);
        if (changed == _alarms[index])
        {
            return Task.CompletedTask;
        }

        _alarms[index] = changed;
        return SaveAsync();
    }

    private Task SaveAsync()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        return _store.SaveAsync(DocumentKey, new TimersDocument(_timers.ToList(), _alarms.ToList()));
    }

    private static string NameOr(string? name, string fallback)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? fallback : trimmed;
    }
}
