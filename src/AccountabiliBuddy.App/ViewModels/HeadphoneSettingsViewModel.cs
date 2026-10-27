using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Settings → Noise player → "Play when headphones connect": wired on/off, Bluetooth on/off and which paired devices.
/// Changes apply at once.
/// </summary>
public sealed partial class HeadphoneSettingsViewModel : ObservableObject
{
    private readonly HeadphoneAutoPlayService _service;
    private bool _loading;

    public HeadphoneSettingsViewModel(HeadphoneAutoPlayService service)
    {
        _service = service;
        _loading = true;
        Wired = service.Current.Wired;
        Bluetooth = service.Current.Bluetooth;
        LoadSchedule(service.Current);
        // Each choice reports a change as it's built, before it's in Devices; saving then would drop it.
        ShowChosen(service.Current.Devices);
        _loading = false;
        if (Bluetooth)
        {
            _ = RefreshDevicesAsync();
        }
    }

    [ObservableProperty]
    public partial bool Wired { get; set; }

    [ObservableProperty]
    public partial bool Bluetooth { get; set; }

    public ObservableCollection<HeadphoneChoiceViewModel> Devices { get; } = new();

    /// <summary>E.g. "Pick at least one device" or "No paired Bluetooth audio devices".</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>"Only at certain times": auto-play only when a ticked option below matches.</summary>
    [ObservableProperty]
    public partial bool OnlyAtTimes { get; set; }

    [ObservableProperty]
    public partial bool DuringPlanWindow { get; set; }

    [ObservableProperty]
    public partial bool OnDaysAndHours { get; set; }

    [ObservableProperty]
    public partial TimeSpan? StartTime { get; set; }

    [ObservableProperty]
    public partial TimeSpan? EndTime { get; set; }

    public ObservableCollection<HeadphoneOptionViewModel> Categories { get; } = new();

    public ObservableCollection<HeadphoneOptionViewModel> Days { get; } = new();

    /// <summary>Shown when the switch is on but nothing is ticked, so it would never play.</summary>
    public string ScheduleHint =>
        OnlyAtTimes && !(DuringPlanWindow || OnDaysAndHours)
            ? "Nothing is ticked, so the noise will never start by itself. Tick at least one option."
            : string.Empty;

    partial void OnOnlyAtTimesChanged(bool value) => ScheduleChanged();

    partial void OnDuringPlanWindowChanged(bool value) => ScheduleChanged();

    partial void OnOnDaysAndHoursChanged(bool value) => ScheduleChanged();

    partial void OnStartTimeChanged(TimeSpan? value) => ScheduleChanged();

    partial void OnEndTimeChanged(TimeSpan? value) => ScheduleChanged();

    partial void OnWiredChanged(bool value) => _ = SaveAsync();

    partial void OnBluetoothChanged(bool value)
    {
        _ = SaveAsync();
        if (value)
        {
            _ = RefreshDevicesAsync();
        }
    }

    /// <summary>Reads the paired Bluetooth audio devices again (chosen ones that are no longer paired stay listed).</summary>
    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        IReadOnlyList<BluetoothAudioDevice> paired = await _service.ListBluetoothAsync();
        IReadOnlyList<BluetoothAudioDevice> chosen = _service.Current.Devices;
        List<BluetoothAudioDevice> all = [.. paired, .. chosen.Where(device => paired.All(other => other.Id != device.Id))];
        _loading = true;
        Devices.Clear();
        foreach (BluetoothAudioDevice device in all)
        {
            Devices.Add(new HeadphoneChoiceViewModel(device, chosen.Any(other => other.Id == device.Id), () => _ = SaveAsync()));
        }

        _loading = false;
        ShowStatus();
    }

    private void ShowChosen(IReadOnlyList<BluetoothAudioDevice> chosen)
    {
        foreach (BluetoothAudioDevice device in chosen)
        {
            Devices.Add(new HeadphoneChoiceViewModel(device, true, () => _ = SaveAsync()));
        }

        ShowStatus();
    }

    private void ShowStatus()
    {
        Status = !Bluetooth ? string.Empty
            : Devices.Count == 0 ? "No paired Bluetooth audio devices. Pair them in Windows' Bluetooth settings, then press Look again."
            : Devices.Any(device => device.IsChosen) ? string.Empty
            : "Tick the headphones that should start the noise.";
    }

    private void LoadSchedule(HeadphoneSettings settings)
    {
        OnlyAtTimes = settings.OnlyAtTimes;
        DuringPlanWindow = settings.DuringPlanWindow;
        OnDaysAndHours = settings.OnDaysAndHours;
        StartTime = settings.EffectiveStart.ToTimeSpan();
        EndTime = settings.EffectiveEnd.ToTimeSpan();
        DayPlan plan = _service.PlanProvider?.Invoke() ?? DayPlan.Default;
        IReadOnlyList<Guid> chosen = HeadphoneSchedule.ChosenCategoryIds(settings, plan);
        foreach (DayCategory category in plan.Categories.Where(category => category.Behavior != CategoryBehavior.Sleep))
        {
            Categories.Add(new HeadphoneOptionViewModel(category.Name, category.Id, chosen.Contains(category.Id), ScheduleChanged));
        }

        foreach (DayOfWeek day in DayPlan.MondayFirst)
        {
            Days.Add(new HeadphoneOptionViewModel(day.ToString()[..3], day, settings.EffectiveDays.Contains(day), ScheduleChanged));
        }
    }

    private void ScheduleChanged()
    {
        OnPropertyChanged(nameof(ScheduleHint));
        _ = SaveAsync();
    }

    private async Task SaveAsync()
    {
        if (_loading)
        {
            return;
        }

        ShowStatus();
        List<BluetoothAudioDevice> chosen = Devices.Where(device => device.IsChosen).Select(device => device.Device).ToList();
        await _service.SaveAsync(new HeadphoneSettings(Wired, Bluetooth, chosen)
        {
            OnlyAtTimes = OnlyAtTimes,
            DuringPlanWindow = DuringPlanWindow,
            PlanCategoryIds = Categories.Where(option => option.IsChosen).Select(option => (Guid)option.Value).ToList(),
            OnDaysAndHours = OnDaysAndHours,
            Days = Days.Where(option => option.IsChosen).Select(option => (DayOfWeek)option.Value).ToList(),
            Start = TimeOnly.FromTimeSpan(StartTime ?? HeadphoneSettings.DefaultStart.ToTimeSpan()),
            End = TimeOnly.FromTimeSpan(EndTime ?? HeadphoneSettings.DefaultEnd.ToTimeSpan()),
        });
    }
}
