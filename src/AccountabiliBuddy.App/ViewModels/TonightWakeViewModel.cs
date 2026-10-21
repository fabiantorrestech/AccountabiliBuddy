using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Tonight's wake time in the on-screen window's Ctrl+Alt details, with sleep mode on: − and + move it 15 minutes for
/// this one night (a <see cref="WakeOnce"/>), "Back to plan" undoes that. The day plan stays as it is. Display only,
/// so it works during focus too.
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

    /// <summary>E.g. "Back to plan (7:00 AM)".</summary>
    [ObservableProperty]
    public partial string PlanWakeText { get; set; }

    public void Refresh()
    {
        (LocalWindow night, LocalWindow planNight) = Nights();
        IsShown = _sleep.Current.Enabled;
        WakeText = $"Up at {night.End:t}";
        IsChanged = night.End != planNight.End;
        PlanWakeText = $"Back to plan ({planNight.End:t})";
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

        return _sleep.SetWakeOnceAsync(wake == planNight.End ? null : new WakeOnce(morning, TimeOnly.FromDateTime(wake)));
    }

    // Tonight with the one-night change, and as the plan has it.
    private (LocalWindow Night, LocalWindow PlanNight) Nights()
    {
        DateTime now = _time.GetLocalNow().DateTime;
        SleepSettings settings = _sleep.Current;
        LocalWindow planNight = (settings with { WakeOnce = null }).ToSchedule(_configuration.Current.DayPlan).NightAround(now);
        LocalWindow night = settings.ToSchedule(_configuration.Current.DayPlan).NightAround(now);
        return (night, planNight);
    }
}
