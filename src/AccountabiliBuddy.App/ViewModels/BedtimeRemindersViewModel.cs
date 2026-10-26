using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Settings → Sleep → Bedtime reminders (ADR 38): on/off and the list, shared with the phone. Each change is saved at
/// once (and reaches the phone with the next sync).
/// </summary>
public sealed partial class BedtimeRemindersViewModel : ObservableObject
{
    private readonly SleepSettingsService _sleep;
    private readonly Func<Guid?> _deviceId;
    private bool _showing;

    public BedtimeRemindersViewModel(SleepSettingsService sleep, Func<Guid?> deviceId)
    {
        _sleep = sleep;
        _deviceId = deviceId;
        Show();
        _sleep.Changed += (_, _) => Show();
    }

    public ObservableCollection<BedtimeReminderRowViewModel> Rows { get; } = new();

    [ObservableProperty]
    public partial bool On { get; set; }

    /// <summary>When a reminder can be, in minutes before bedtime.</summary>
    public static IReadOnlyList<int> MinuteChoices { get; } = new[] { 0, 5, 10, 15, 30, 45, 60, 90, 120 };

    partial void OnOnChanged(bool value) => Save();

    [RelayCommand]
    private void Add()
    {
        HashSet<int> used = Rows.Select(r => r.MinutesBefore).ToHashSet();
        int minutes = new[] { 30, 60, 90, 120, 10, 5 }.FirstOrDefault(m => !used.Contains(m), 20);
        Rows.Add(new BedtimeReminderRowViewModel(this, new BedtimeReminder(minutes, false, new[] { BedtimeReminder.Phone }), _deviceId()));
        Save();
    }

    [RelayCommand]
    private void Reset()
    {
        _ = _sleep.SaveAsync(_sleep.Current with { BedtimeReminders = BedtimeReminderSettings.Default });
    }

    internal void Remove(BedtimeReminderRowViewModel row)
    {
        Rows.Remove(row);
        Save();
    }

    internal void Save()
    {
        if (_showing)
        {
            return;
        }

        var settings = new BedtimeReminderSettings(On, Rows.Select(r => r.ToReminder()).ToList());
        _ = _sleep.SaveAsync(_sleep.Current with { BedtimeReminders = settings });
    }

    private void Show()
    {
        _showing = true;
        BedtimeReminderSettings settings = _sleep.Current.Reminders;
        On = settings.On;
        Rows.Clear();
        foreach (BedtimeReminder reminder in settings.List.OrderByDescending(r => r.MinutesBefore))
        {
            Rows.Add(new BedtimeReminderRowViewModel(this, reminder, _deviceId()));
        }

        _showing = false;
    }
}

/// <summary>One reminder in the list: when, notification or alarm, and its devices.</summary>
public sealed partial class BedtimeReminderRowViewModel : ObservableObject
{
    private readonly BedtimeRemindersViewModel _owner;
    private readonly BedtimeReminder _original;
    private readonly string? _thisPc;
    private bool _ready;

    public BedtimeReminderRowViewModel(BedtimeRemindersViewModel owner, BedtimeReminder reminder, Guid? thisPc)
    {
        _owner = owner;
        _original = reminder;
        _thisPc = thisPc is Guid id ? BedtimeReminder.Pc(id) : null;
        MinutesBefore = reminder.MinutesBefore;
        Alarm = reminder.Alarm;
        Phone = reminder.Devices.Contains(BedtimeReminder.Phone);
        Watch = reminder.Devices.Contains(BedtimeReminder.Watch);
        AllPcs = reminder.Devices.Contains(BedtimeReminder.AllPcs);
        ThisPc = _thisPc is not null && reminder.Devices.Contains(_thisPc);
        _ready = true;
    }

    public IReadOnlyList<int> MinuteChoices => BedtimeRemindersViewModel.MinuteChoices;

    [ObservableProperty]
    public partial int MinutesBefore { get; set; }

    [ObservableProperty]
    public partial bool Alarm { get; set; }

    [ObservableProperty]
    public partial bool Phone { get; set; }

    [ObservableProperty]
    public partial bool Watch { get; set; }

    [ObservableProperty]
    public partial bool AllPcs { get; set; }

    [ObservableProperty]
    public partial bool ThisPc { get; set; }

    public bool HasThisPc => _thisPc is not null;

    partial void OnMinutesBeforeChanged(int value) => Changed();

    partial void OnAlarmChanged(bool value) => Changed();

    partial void OnPhoneChanged(bool value) => Changed();

    partial void OnWatchChanged(bool value) => Changed();

    partial void OnAllPcsChanged(bool value) => Changed();

    partial void OnThisPcChanged(bool value) => Changed();

    [RelayCommand]
    private void Remove() => _owner.Remove(this);

    internal BedtimeReminder ToReminder()
    {
        // Devices this PC can't name (another PC) are kept as they were.
        var devices = _original.Devices.Where(d => d.StartsWith("pc:", StringComparison.Ordinal) && d != _thisPc).ToList();
        if (Phone) devices.Add(BedtimeReminder.Phone);
        if (Watch) devices.Add(BedtimeReminder.Watch);
        if (AllPcs) devices.Add(BedtimeReminder.AllPcs);
        if (ThisPc && _thisPc is not null) devices.Add(_thisPc);
        return _original with { MinutesBefore = MinutesBefore, Alarm = Alarm, Devices = devices };
    }

    private void Changed()
    {
        if (_ready)
        {
            _owner.Save();
        }
    }
}
