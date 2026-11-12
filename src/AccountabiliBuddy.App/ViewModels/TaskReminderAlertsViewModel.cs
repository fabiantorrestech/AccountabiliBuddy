using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Settings › Alerts &amp; on-screen › <b>Task reminders</b> and <b>Task reminders ring on</b> (spec 03 §D, 05 §7, Settings Restructure
/// Plan, prototype's taskrem/taskring groups). Task reminders: Volume (its own row for now; merging it with the chimes' volume is
/// deferred), Rising volume, Ring even when the PC is muted, Accidental dismiss protection + Wait before dismiss, Stop ringing after,
/// How reminders show, Snooze shortcuts (Edit: the snooze picker's editor) and the test rings (Ring one always; Ring 6 and Can't miss in
/// development builds only). Each row saves at once through <see cref="TaskReminderSettingsService.UpdateAsync"/> (this PC only; a ring
/// setting loosens nothing about a session, so it isn't locked, like the chimes). Test rings show made-up tasks and write nothing.
/// Ring on: one switch per device (the account's list, synced; a PC stays on: it always rings for its own reminders), the per-label
/// summary (→ Tasks › Labels) and Your devices (→ Devices &amp; sync).
/// </summary>
public sealed partial class TaskReminderAlertsViewModel : ObservableObject
{
    private static readonly TaskReminderDisplay[] Displays = [TaskReminderDisplay.Toast, TaskReminderDisplay.FullScreen];

    private readonly TaskReminderSettingsService _settings;
    private readonly TaskReminderService _reminders;
    private readonly TaskAccountSettingsService _account;
    private readonly TaskLabelService _labels;
    private readonly Func<IReadOnlyList<TaskRingDevice>> _devices;
    private IReadOnlyList<TaskRingDevice> _shownDevices = [];

    // True while the page reads the services: the properties' change handlers then save nothing.
    private bool _loading;

    public TaskReminderAlertsViewModel(
        TaskReminderSettingsService settings,
        TaskReminderService reminders,
        TaskAccountSettingsService account,
        TaskLabelService labels,
        Func<IReadOnlyList<TaskRingDevice>> devices)
    {
        _settings = settings;
        _reminders = reminders;
        _account = account;
        _labels = labels;
        _devices = devices;
        ShortcutsText = PerLabelText = RisingHint = string.Empty;
        settings.Changed += (_, _) => Dispatcher.UIThread.Post(Load);
        account.Changed += (_, _) => Dispatcher.UIThread.Post(Load);
        labels.Changed += (_, _) => Dispatcher.UIThread.Post(Load);
        Load();
    }

    // ---- set by the app ----

    /// <summary>"Per-label rings": opens Settings › Tasks › Labels.</summary>
    public Action? OpenLabels { get; init; }

    /// <summary>"Your devices": opens Devices &amp; sync.</summary>
    public Action? OpenDevices { get; init; }


    // ---- Task reminders ----

    /// <summary>Reminder volume, 10 to 100 %.</summary>
    [ObservableProperty]
    public partial double Volume { get; set; }

    [ObservableProperty]
    public partial bool RisingVolume { get; set; }

    /// <summary>"Starts quiet, reaches 70% in 30 s".</summary>
    [ObservableProperty]
    public partial string RisingHint { get; set; }

    [ObservableProperty]
    public partial bool RingEvenWhenMuted { get; set; }

    [ObservableProperty]
    public partial bool DismissProtect { get; set; }

    public IReadOnlyList<string> ProtectChoices { get; } = TaskReminderRingPolicy.ProtectChoices.Select(seconds => $"{seconds} s").ToList();

    [ObservableProperty]
    public partial int ProtectIndex { get; set; }

    public IReadOnlyList<string> StopAfterChoices { get; } = TaskReminderRingPolicy.StopAfterChoices.Select(minutes => $"{minutes} min").ToList();

    [ObservableProperty]
    public partial int StopAfterIndex { get; set; }

    public IReadOnlyList<string> DisplayChoices { get; } = ["Toast", "Always full screen"];

    [ObservableProperty]
    public partial int DisplayIndex { get; set; }

    /// <summary>"Tonight 6:00 PM · Tomorrow 9:00 AM · Monday 9:00 AM".</summary>
    [ObservableProperty]
    public partial string ShortcutsText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditingShortcuts))]
    public partial bool IsEditingShortcuts { get; set; }

    public bool IsNotEditingShortcuts => !IsEditingShortcuts;

    [ObservableProperty]
    public partial IReadOnlyList<SnoozeShortcutEditViewModel> ShortcutEdits { get; set; } = [];

    /// <summary>"Ring a stack of 6" and "Ring a Can't miss alarm" show only in development builds (Settings Placement: the release keeps "Ring a test reminder").</summary>
    public bool ShowsDevTests =>
#if DEBUG
        true;
#else
        false;
#endif

    // ---- Task reminders ring on ----

    public ObservableCollection<RingDeviceChoiceViewModel> Devices { get; } = new();

    /// <summary>Why a switch didn't move ("This PC always rings for its own reminders"), under the devices; empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string NoticeText { get; set; } = string.Empty;

    public bool HasNotice => NoticeText.Length > 0;

    /// <summary>"None: every label rings on the devices above", or each label that rings on fewer.</summary>
    [ObservableProperty]
    public partial string PerLabelText { get; set; }

    /// <summary>Reads the settings, the account's list and the devices again (also when the device list changed).</summary>
    public void Load()
    {
        _loading = true;
        try
        {
            TaskReminderSettings current = _settings.Current;
            Volume = current.Volume;
            RisingVolume = current.RisingVolume;
            RisingHint = $"Starts quiet, reaches {current.Volume}% in 30 s.";
            RingEvenWhenMuted = current.RingEvenWhenMuted;
            DismissProtect = current.DismissProtect;
            ProtectIndex = IndexOf(TaskReminderRingPolicy.ProtectChoices, current.DismissProtectSeconds);
            StopAfterIndex = IndexOf(TaskReminderRingPolicy.StopAfterChoices, current.StopAfterMinutes);
            DisplayIndex = Math.Max(0, Array.IndexOf(Displays, current.Display));
            ShortcutsText = string.Join(" · ", current.Shortcuts.Select(TaskSnooze.Label));
            LoadDevices();
        }
        finally
        {
            _loading = false;
        }
    }

    private void LoadDevices()
    {
        IReadOnlyList<TaskRingDevice> devices = _devices();
        IReadOnlyList<Guid>? ringOn = _account.RingOn;
        if (devices.Select(device => device).SequenceEqual(_shownDevices) && Devices.Count == devices.Count)
        {
            foreach (RingDeviceChoiceViewModel choice in Devices)
            {
                choice.Show(TaskRingDevices.Rings(ringOn, choice.Device.Id));
            }
        }
        else
        {
            Devices.Clear();
            foreach (TaskRingDevice device in devices)
            {
                Devices.Add(new RingDeviceChoiceViewModel(device, TaskRingDevices.Rings(ringOn, device.Id), ToggleDevice));
            }
        }

        _shownDevices = devices;
        PerLabelText = TaskRingText.PerLabel(_labels.Labels, devices);
    }

    private void ToggleDevice(TaskRingDevice device, bool on)
    {
        if (device.IsPc)
        {
            if (!on)
            {
                NoticeText = device.IsThisPc ? "This PC always rings for its own reminders" : "A PC always rings for its own reminders";
            }

            return;
        }

        NoticeText = string.Empty;

        IReadOnlyList<Guid> known = _shownDevices.Select(shown => shown.Id).Where(id => id != Guid.Empty).ToList();
        _ = _account.SetRingOnAsync(TaskRingDevices.Toggled(_account.RingOn, known, device.Id, on));
    }

    // ---- saving ----

    partial void OnVolumeChanged(double value)
    {
        int volume = (int)Math.Round(value);
        Save(current => current with { Volume = volume });
    }

    partial void OnRisingVolumeChanged(bool value) => Save(current => current with { RisingVolume = value });

    partial void OnRingEvenWhenMutedChanged(bool value) => Save(current => current with { RingEvenWhenMuted = value });

    partial void OnDismissProtectChanged(bool value) => Save(current => current with { DismissProtect = value });

    partial void OnProtectIndexChanged(int value)
    {
        if (value >= 0 && value < TaskReminderRingPolicy.ProtectChoices.Count)
        {
            Save(current => current with { DismissProtectSeconds = TaskReminderRingPolicy.ProtectChoices[value] });
        }
    }

    partial void OnStopAfterIndexChanged(int value)
    {
        if (value >= 0 && value < TaskReminderRingPolicy.StopAfterChoices.Count)
        {
            Save(current => current with { StopAfterMinutes = TaskReminderRingPolicy.StopAfterChoices[value] });
        }
    }

    partial void OnDisplayIndexChanged(int value)
    {
        if (value >= 0 && value < Displays.Length)
        {
            Save(current => current with { Display = Displays[value] });
        }
    }

    private void Save(Func<TaskReminderSettings, TaskReminderSettings> change)
    {
        if (_loading)
        {
            return;
        }

        _ = _settings.UpdateAsync(change);
    }

    [RelayCommand]
    private void EditShortcuts()
    {
        ShortcutEdits = _settings.Current.Shortcuts.Select(shortcut => new SnoozeShortcutEditViewModel(shortcut)).ToList();
        IsEditingShortcuts = true;
    }

    [RelayCommand]
    private void CancelShortcuts()
    {
        IsEditingShortcuts = false;
    }

    /// <summary>Saves the three shortcuts (all three must read as a day and a time).</summary>
    [RelayCommand]
    private async Task SaveShortcutsAsync()
    {
        List<TaskSnoozeShortcut> edited = ShortcutEdits.Select(edit => edit.Shortcut).OfType<TaskSnoozeShortcut>().ToList();
        if (edited.Count != 3)
        {
            return;
        }

        await _settings.UpdateAsync(current => current with { SnoozeShortcuts = edited });
        IsEditingShortcuts = false;
    }

    // ---- test rings: made-up tasks, nothing written ----

    [RelayCommand]
    private void RingOne() => _reminders.RingSamples(TaskReminderSamples.One(_labels.Labels, _reminders.Now));

    [RelayCommand]
    private void RingSix() => _reminders.RingSamples(TaskReminderSamples.Six(_labels.Labels, _reminders.Now));

    [RelayCommand]
    private void RingCantMiss() => _reminders.RingSamples(TaskReminderSamples.CantMiss(_labels.Labels, _reminders.Now));

    [RelayCommand]
    private void ShowLabels() => OpenLabels?.Invoke();

    [RelayCommand]
    private void ShowDevices() => OpenDevices?.Invoke();

    private static int IndexOf(IReadOnlyList<int> choices, int value)
    {
        int index = choices.ToList().IndexOf(value);
        return index >= 0 ? index : 0;
    }
}
