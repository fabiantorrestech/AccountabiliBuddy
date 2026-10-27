using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Loads, saves and announces changes to <see cref="SleepSettings"/>. The times follow the day plan's Sleep parts
/// (<see cref="SaveFromPlanAsync"/>); only on/off is the user's own. Turning sleep mode on applies at once; turning it
/// off is a looser change, so it is refused during a focus session and otherwise waits like the other looser changes
/// (<see cref="LockSettings.LooseningDelay"/>, counted by the trusted clock, kept in documents "sleep.pendingOff" so a
/// restart doesn't skip the wait, and applied only while no session runs). Use from the UI thread only.
/// </summary>
public sealed class SleepSettingsService
{
    private const string DocumentKey = "sleep";
    private const string PendingOffKey = "sleep.pendingOff";

    private readonly SqliteJsonDocumentStore _store;
    private readonly TimeProvider _time;
    private readonly SessionController _sessions;
    private readonly Func<TimeSpan> _offWait;

    /// <param name="offWait">How long turning sleep mode off waits: the looser-change wait of the configuration in effect.</param>
    public SleepSettingsService(SqliteJsonDocumentStore store, TimeProvider time, SessionController sessions, Func<TimeSpan> offWait)
    {
        _store = store;
        _time = time;
        _sessions = sessions;
        _offWait = offWait;
    }

    public SleepSettings Current { get; private set; } = SleepSettings.Default;

    /// <summary>A paired phone's next alarm (local time) from its sync file (ADR 23); not saved here.</summary>
    public DateTime? PhoneAlarm { get; private set; }

    /// <summary>A paired phone's smart bedtime (ADR 34) from its sync file; not saved here.</summary>
    public AccountabiliBuddy.Core.Sync.SyncSmartBedtime? PhoneBedtime { get; private set; }

    /// <summary>A paired phone's estimated wake per kind of morning (ADR 40) from its sync file; not saved here.</summary>
    public AccountabiliBuddy.Core.Sync.SyncWakeEstimate? PhoneWake { get; private set; }

    /// <summary>When sleep mode turns off, while that is waiting; null when no off is pending.</summary>
    public DateTimeOffset? PendingOffAt { get; private set; }

    /// <summary>"Sleep mode turns off at 4:05 PM.", while an off is pending; otherwise null.</summary>
    public string? PendingOffText => PendingOffAt is DateTimeOffset at ? $"Sleep mode turns off at {at.ToLocalTime():t}." : null;

    public event EventHandler<SleepSettings>? Changed;

    /// <summary>A pending off was started, cancelled or applied.</summary>
    public event EventHandler? PendingOffChanged;

    /// <summary>The phone's alarm, smart bedtime or wake estimate changed (not a change of the settings themselves).</summary>
    public event EventHandler? PhoneAlarmChanged;

    /// <summary>The nights with the day plan, a one-night change, the phone's alarm, smart bedtime and wake estimate in their order.</summary>
    public SleepSchedule Schedule(DayPlan plan)
    {
        return Current.ToSchedule(plan, PhoneAlarm, PhoneBedtime, PhoneWake);
    }

    public void SetPhoneWake(AccountabiliBuddy.Core.Sync.SyncWakeEstimate? wake)
    {
        if (wake == PhoneWake)
        {
            return;
        }

        PhoneWake = wake;
        PhoneAlarmChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetPhoneBedtime(AccountabiliBuddy.Core.Sync.SyncSmartBedtime? bedtime)
    {
        if (bedtime == PhoneBedtime)
        {
            return;
        }

        PhoneBedtime = bedtime;
        PhoneAlarmChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The smart bedtime's switches and limits (shared both ways, ADR 35); saved at once.</summary>
    public Task SetSmartAsync(bool workdays, bool offDays, double minHours, double maxHours)
    {
        SleepSettings updated = Current with { SmartWorkdays = workdays, SmartOffDays = offDays, MinHours = minHours, MaxHours = maxHours };
        return updated == Current ? Task.CompletedTask : SaveAsync(updated);
    }

    /// <summary>The wake source (ADR 35): the phone's alarm, this PC's alarm, or neither; this PC's own.</summary>
    public Task SetWakeSourceAsync(bool phoneAlarm, bool pcAlarm)
    {
        SleepSettings updated = Current with { UsePhoneAlarm = phoneAlarm, UsePcAlarm = pcAlarm };
        return updated == Current ? Task.CompletedTask : SaveAsync(updated);
    }

    public Task SetUsePhoneBedtimeAsync(bool use)
    {
        return use == Current.UsePhoneBedtime ? Task.CompletedTask : SaveAsync(Current with { UsePhoneBedtime = use });
    }

    public void SetPhoneAlarm(DateTime? alarm)
    {
        if (alarm == PhoneAlarm)
        {
            return;
        }

        PhoneAlarm = alarm;
        PhoneAlarmChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task SetUsePhoneAlarmAsync(bool use)
    {
        return use == Current.UsePhoneAlarm ? Task.CompletedTask : SaveAsync(Current with { UsePhoneAlarm = use });
    }

    public void LoadBlocking()
    {
        try
        {
            SleepSettings? loaded = Task.Run(() => _store.LoadAsync<SleepSettings>(DocumentKey)).GetAwaiter().GetResult();
            Current = loaded is null || loaded.Weeknights is null || loaded.Weekends is null ? SleepSettings.Default : loaded;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = SleepSettings.Default;
        }

        try
        {
            DateTimeOffset? pending = Task.Run(() => _store.LoadAsync<DateTimeOffset?>(PendingOffKey)).GetAwaiter().GetResult();
            // An off that is no longer needed (sleep mode is off already) is dropped.
            PendingOffAt = Current.Enabled ? pending : null;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            // Unreadable: sleep mode stays on, the stricter state.
            PendingOffAt = null;
        }
    }

    /// <summary>
    /// Saves the settings. Turning sleep mode off is a looser change: it starts the wait (or leaves the running one as it
    /// is) and sleep mode stays on until then; during a focus session it is refused. Everything else in
    /// <paramref name="settings"/> is saved either way. Saving with sleep mode on leaves a pending off alone (other
    /// settings change meanwhile): turning it back on is <see cref="CancelPendingOffAsync"/>.
    /// </summary>
    /// <param name="fromAnotherDevice">The settings came from another device's sync file: an off that device already waited
    /// for (<see cref="SleepSettings.OffWaited"/>) applies at once, any other off waits here too, also during a session
    /// (it then applies after it).</param>
    /// <returns>Null when everything was saved as asked; otherwise what happened to the off.</returns>
    public async Task<string?> SaveAsync(SleepSettings settings, bool fromAnotherDevice = false)
    {
        string? note = null;
        if (Current.Enabled && !settings.Enabled && (!fromAnotherDevice || !settings.OffWaited))
        {
            if (!fromAnotherDevice && _sessions.IsEnforcing)
            {
                note = "Sleep mode stays on: turning it off is locked while a focus session you started is running.";
            }
            else
            {
                await StartPendingOffAsync();
                note = PendingOffText;
            }

            settings = settings with { Enabled = true, OffWaited = false };
        }
        else if (settings.Enabled)
        {
            settings = settings with { OffWaited = false };
        }

        Current = settings;
        Changed?.Invoke(this, settings);
        await _store.SaveAsync(DocumentKey, settings);
        return note;
    }

    /// <summary>Drops a pending off, which is turning sleep mode back on: it stays on.</summary>
    public async Task CancelPendingOffAsync()
    {
        if (PendingOffAt is null)
        {
            return;
        }

        PendingOffAt = null;
        PendingOffChanged?.Invoke(this, EventArgs.Empty);
        await _store.DeleteAsync(PendingOffKey);
    }

    /// <summary>Turns sleep mode off once its wait is over and no session is running. Runs every second.</summary>
    public async Task ApplyPendingOffIfDueAsync()
    {
        if (PendingOffAt is not DateTimeOffset at || _sessions.State.IsActive || _time.GetUtcNow() < at)
        {
            return;
        }

        PendingOffAt = null;
        // Marked as waited, so another PC that gets this off from the sync file doesn't wait again.
        SleepSettings updated = Current with { Enabled = false, OffWaited = true };
        Current = updated;
        Changed?.Invoke(this, updated);
        PendingOffChanged?.Invoke(this, EventArgs.Empty);
        await _store.DeleteAsync(PendingOffKey);
        await _store.SaveAsync(DocumentKey, updated);
    }

    // A running wait is not restarted by asking again.
    private async Task StartPendingOffAsync()
    {
        if (PendingOffAt is not null)
        {
            return;
        }

        DateTimeOffset readyAt = _time.GetUtcNow() + _offWait();
        PendingOffAt = readyAt;
        PendingOffChanged?.Invoke(this, EventArgs.Empty);
        await _store.SaveAsync<DateTimeOffset?>(PendingOffKey, readyAt);
    }

    /// <summary>Changes the wake time of one night only (null = back to the plan's); the day plan stays as it is.</summary>
    public Task SetWakeOnceAsync(WakeOnce? wake)
    {
        return SaveAsync(Current with { WakeOnce = wake });
    }

    /// <summary>Takes the bedtimes and wake times from the plan's Sleep parts (Workdays = weeknights); saves only on a change.</summary>
    public async Task SaveFromPlanAsync(DayPlan plan)
    {
        // The first workday (and the first other day) of week 1, Monday first, by the cycle; the stock templates when none.
        SleepTimes weeknights = DayPlanSleep.TypicalNight(plan, workday: true) ?? DayPlanSleep.SleepOf(plan.Workday, plan) ?? Current.Weeknights;
        SleepTimes weekends = DayPlanSleep.TypicalNight(plan, workday: false) ?? DayPlanSleep.SleepOf(plan.OtherDay, plan) ?? Current.Weekends;
        SleepSettings updated = Current with { SameEveryNight = false, Weeknights = weeknights, Weekends = weekends };
        if (updated != Current)
        {
            await SaveAsync(updated);
        }
    }
}
