using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Loads, saves and announces changes to <see cref="SleepSettings"/>. The times follow the day plan's Sleep parts
/// (<see cref="SaveFromPlanAsync"/>); only on/off is the user's own. Use from the UI thread only.
/// </summary>
public sealed class SleepSettingsService
{
    private const string DocumentKey = "sleep";

    private readonly SqliteJsonDocumentStore _store;

    public SleepSettingsService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public SleepSettings Current { get; private set; } = SleepSettings.Default;

    /// <summary>A paired phone's next alarm (local time) from its sync file (ADR 23); not saved here.</summary>
    public DateTime? PhoneAlarm { get; private set; }

    /// <summary>A paired phone's smart bedtime (ADR 34) from its sync file; not saved here.</summary>
    public AccountabiliBuddy.Core.Sync.SyncSmartBedtime? PhoneBedtime { get; private set; }

    public event EventHandler<SleepSettings>? Changed;

    /// <summary>The phone's alarm or smart bedtime changed (not a change of the settings themselves).</summary>
    public event EventHandler? PhoneAlarmChanged;

    /// <summary>The nights with the day plan, a one-night change, the phone's alarm and smart bedtime in their order.</summary>
    public SleepSchedule Schedule(DayPlan plan)
    {
        return Current.ToSchedule(plan, PhoneAlarm, PhoneBedtime);
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
    }

    public async Task SaveAsync(SleepSettings settings)
    {
        Current = settings;
        Changed?.Invoke(this, settings);
        await _store.SaveAsync(DocumentKey, settings);
    }

    /// <summary>Changes the wake time of one night only (null = back to the plan's); the day plan stays as it is.</summary>
    public Task SetWakeOnceAsync(WakeOnce? wake)
    {
        return SaveAsync(Current with { WakeOnce = wake });
    }

    /// <summary>Takes the bedtimes and wake times from the plan's Sleep parts (Workdays = weeknights); saves only on a change.</summary>
    public async Task SaveFromPlanAsync(DayPlan plan)
    {
        SleepTimes weeknights = DayPlanSleep.SleepOf(plan.Workday, plan) ?? Current.Weeknights;
        SleepTimes weekends = DayPlanSleep.SleepOf(plan.OtherDay, plan) ?? Current.Weekends;
        SleepSettings updated = Current with { SameEveryNight = false, Weeknights = weeknights, Weekends = weekends };
        if (updated != Current)
        {
            await SaveAsync(updated);
        }
    }
}
