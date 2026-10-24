using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Tonight's wake time in the on-screen window's Ctrl+Alt details, with sleep mode on: − and + move it 15 minutes for
/// this one night (a <see cref="WakeOnce"/>), "Back to plan" undoes that. The day plan stays as it is. While the phone's
/// alarm sets tomorrow's wake (ADR 23), it wins over a one-night change; changing it then asks first, and a confirmed
/// change wins over the alarm for that morning. Display only, so it works during focus too.
/// </summary>
public sealed partial class TonightWakeViewModel : ObservableObject
{
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(15);

    private readonly SleepSettingsService _sleep;
    private readonly ConfigurationService _configuration;
    private readonly TimeProvider _time;

    public TonightWakeViewModel(SleepSettingsService sleep, ConfigurationService configuration, TimeProvider time)
    {
        _sleep = sleep;
        _configuration = configuration;
        _time = time;
        WakeText = PlanWakeText = string.Empty;
        _sleep.Changed += (_, _) => Refresh();
        _sleep.PhoneAlarmChanged += (_, _) => Refresh();
        _configuration.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>With sleep mode on.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }

    /// <summary>E.g. "Up at 7:15 AM".</summary>
    [ObservableProperty]
    public partial string WakeText { get; set; }

    /// <summary>True while tonight's wake differs from the plan's.</summary>
    [ObservableProperty]
    public partial bool IsChanged { get; set; }

    /// <summary>E.g. "Back to plan (7:00 AM)", or "Back to alarm (6:15 AM)".</summary>
    [ObservableProperty]
    public partial string PlanWakeText { get; set; }

    /// <summary>Set by the app: asks (title, message, on confirm) before a change wins over the phone's alarm.</summary>
    public Action<string, string, Action>? AskToOverrideAlarm { get; set; }

    public void Refresh()
    {
        (LocalWindow night, LocalWindow planNight) = Nights();
        bool alarm = WakesByAlarm(night);
        bool baseByAlarm = BaselineByAlarm(planNight);
        IsShown = _sleep.Current.Enabled;
        WakeText = $"Up at {night.End:t}" + (alarm ? " (phone alarm)" : "");
        IsChanged = night.End != planNight.End;
        PlanWakeText = baseByAlarm ? $"Back to alarm ({planNight.End:t})" : $"Back to plan ({planNight.End:t})";
    }

    [RelayCommand]
    private Task EarlierAsync()
    {
        return ShiftAsync(-Step);
    }

    [RelayCommand]
    private Task LaterAsync()
    {
        return ShiftAsync(Step);
    }

    [RelayCommand]
    private Task BackToPlanAsync()
    {
        return _sleep.SetWakeOnceAsync(null);
    }

    // Moves tonight's wake, staying on the plan's morning and after bedtime; the plan's own time clears the change.
    private Task ShiftAsync(TimeSpan by)
    {
        (LocalWindow night, LocalWindow planNight) = Nights();
        DateOnly morning = DateOnly.FromDateTime(planNight.End);
        DateTime wake = night.End + by;
        DateTime earliest = night.Start + Step;
        if (wake < earliest || DateOnly.FromDateTime(wake) != morning)
        {
            return Task.CompletedTask;
        }

        bool overAlarm = _sleep.Current.WakeOnce is { OverridesAlarm: true } current && current.Morning == morning;
        WakeOnce? change = wake == planNight.End ? null : new WakeOnce(morning, TimeOnly.FromDateTime(wake), overAlarm);
        if (change is not null && !overAlarm && BaselineByAlarm(planNight))
        {
            // The phone's alarm sets this morning and wins over a one-night change: ask before this one wins instead.
            AskToOverrideAlarm?.Invoke(
                $"Your phone's alarm sets tomorrow ({planNight.End:t})",
                $"Your phone's alarm wins over a one-night change. Use {wake:t} for tomorrow instead of the alarm?",
                () => _ = _sleep.SetWakeOnceAsync(change with { OverridesAlarm = true }));
            return Task.CompletedTask;
        }

        return _sleep.SetWakeOnceAsync(change);
    }

    private bool WakesByAlarm(LocalWindow night)
    {
        return _sleep.Schedule(_configuration.Current.DayPlan).WakesByAlarm(DateOnly.FromDateTime(night.End).AddDays(-1));
    }

    // The baseline (no one-night change) comes from the phone's alarm.
    private bool BaselineByAlarm(LocalWindow planNight)
    {
        return (_sleep.Current with { WakeOnce = null }).ToSchedule(_configuration.Current.DayPlan, _sleep.PhoneAlarm, _sleep.PhoneBedtime)
            .WakesByAlarm(DateOnly.FromDateTime(planNight.End).AddDays(-1));
    }

    // Tonight with the one-night change, and as the plan has it.
    private (LocalWindow Night, LocalWindow PlanNight) Nights()
    {
        DateTime now = _time.GetLocalNow().DateTime;
        SleepSettings settings = _sleep.Current;
        LocalWindow planNight = (settings with { WakeOnce = null }).ToSchedule(_configuration.Current.DayPlan, _sleep.PhoneAlarm, _sleep.PhoneBedtime).NightAround(now);
        LocalWindow night = settings.ToSchedule(_configuration.Current.DayPlan, _sleep.PhoneAlarm, _sleep.PhoneBedtime).NightAround(now);
        return (night, planNight);
    }
}
