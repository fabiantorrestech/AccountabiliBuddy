using System.Collections.ObjectModel;
using System.ComponentModel;
using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The sync light (design 20a; Tasks redesign 9: the dot on the title-bar menu's button and the menu's Sync section), read from what the Devices page already knows
/// (<see cref="DevicesViewModel.Rows"/> and <see cref="DevicesViewModel.SyncProblem"/>). Display only; it changes nothing.
/// Green = all in step. One definition of "behind" serves the glyphs, the dot and the popover: a device is behind when its
/// newest data is more than 5 minutes older than its own sync interval (<see cref="DeviceSyncState.Behind"/>: a day for a
/// phone or watch, the sync method's interval for a PC). A behind device's glyph dims. A hollow ring shows while a sync
/// runs. The lilac dot means 2 or more devices are behind and over an hour old; the red one that this device can't sync
/// (offline, or its sync method is failing). With no other device linked the light is hidden.
/// </summary>
public sealed partial class SyncLightViewModel : ObservableObject
{
    private static readonly TimeSpan SeveralBehindAge = TimeSpan.FromHours(1);

    private readonly DevicesViewModel _devices;
    private readonly SyncLightSettingService? _setting;
    private readonly TimeProvider _time;

    /// <param name="time">Turns a device's age into the 12-hour time it last synced (older than an hour); the system clock by default.</param>
    public SyncLightViewModel(DevicesViewModel devices, SyncLightSettingService? setting = null, TimeProvider? time = null)
    {
        _devices = devices;
        _setting = setting;
        _time = time ?? TimeProvider.System;
        if (setting is not null)
        {
            setting.Changed += (_, _) => Rebuild();
        }

        devices.RowsRefreshed += (_, _) => Rebuild();
        devices.PropertyChanged += OnDevicesChanged;
        Rebuild();
    }

    /// <summary>Every device, this PC first, in the order of the glyphs and of the popover's lines.</summary>
    public ObservableCollection<SyncLightDeviceViewModel> Devices { get; } = new();

    /// <summary>
    /// This PC's glyph: the only one in the title bar, next to the dot that sums up all devices (owner, AB-Improvements #26: the bar
    /// no longer grows with each device; hover or click opens the popover with every device).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThisDevice))]
    public partial SyncLightDeviceViewModel? ThisDevice { get; set; }

    public bool HasThisDevice => ThisDevice is not null;

    /// <summary>False with no other device linked: only the sync button shows then.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>A sync is running: the dot turns into a ring (the title bar's sync button sets it).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDotSolid))]
    public partial bool IsSyncing { get; set; }

    public bool IsDotSolid => !IsSyncing;

    /// <summary>2 or more other devices are behind and over an hour old: the dot turns lilac.</summary>
    [ObservableProperty]
    public partial bool IsSeveralBehind { get; set; }

    /// <summary>This device can't sync right now: the dot turns red.</summary>
    [ObservableProperty]
    public partial bool IsOffline { get; set; }

    /// <summary>The popover's first line: "Synced 1 min ago · Google Drive", or what is wrong.</summary>
    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    private void OnDevicesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DevicesViewModel.SyncProblem))
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        // A removed device (ADR 54) isn't expected to sync, so it has no light.
        IReadOnlyList<DeviceRowViewModel> rows = _devices.Rows.Where(row => row.State != DeviceSyncState.Removed).ToList();
        string? problem = _devices.SyncProblem;
        bool hasOthers = rows.Count > 1;

        Devices.Clear();
        SyncLightDeviceViewModel? thisDevice = null;
        foreach (DeviceRowViewModel row in rows.OrderBy(KindOrder).ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            bool isThisPc = row.Kind == "This PC";
            bool dim = isThisPc ? problem is not null : IsBehind(row) || row.Age is null;
            string ago = isThisPc ? "this PC" : row.Age is TimeSpan age ? DevicesViewModel.Ago(age) : "not yet";
            bool synced = isThisPc ? problem is null : row.State == DeviceSyncState.UpToDate;
            bool failing = isThisPc ? problem is not null : IsBehind(row);
            var device = new SyncLightDeviceViewModel(row.Name, IconFor(row.Kind), ago, dim, synced, failing)
            {
                FailingIcon = isThisPc ? MaterialIconKind.CloudAlertOutline : MaterialIconKind.CloudOffOutline,
                WhenText = isThisPc ? (problem is null ? "This device · in step" : "This device · can't sync") : DeviceWhen(row),
            };
            Devices.Add(device);
            if (isThisPc)
            {
                thisDevice = device;
            }
        }

        ThisDevice = thisDevice;

        IsOffline = problem is not null;
        bool somethingWrong = problem is not null || rows.Skip(1).Any(IsBehind);
        IsVisible = hasOthers && (_setting?.Mode ?? SyncLightMode.Always) switch
        {
            SyncLightMode.Off => false,
            SyncLightMode.OnlyWhenWrong => somethingWrong,
            _ => true,
        };
        int behindForAnHour = rows.Skip(1).Count(row => IsBehind(row) && row.Age is TimeSpan age && age > SeveralBehindAge);
        IsSeveralBehind = problem is null && behindForAnHour >= 2;
        SummaryText = problem ?? (IsSeveralBehind ? $"{behindForAnHour} devices are behind: nothing from them for over an hour" : Summary(rows));
        TipText = Tip(problem, rows);
        HasOthers = hasOthers;
        (StatusTitle, StatusDetail) = Status(problem, rows, behindForAnHour);
    }

    /// <summary>Another device is linked: the menu's status row folds out a device list.</summary>
    [ObservableProperty]
    public partial bool HasOthers { get; set; }

    /// <summary>The menu's status row (Tasks redesign 9.11): "All in step", "Pixel 8 is behind", "Can't sync"…</summary>
    [ObservableProperty]
    public partial string StatusTitle { get; set; } = string.Empty;

    /// <summary>Under it: "3 devices · last sync 2 min ago", or what is wrong.</summary>
    [ObservableProperty]
    public partial string StatusDetail { get; set; } = string.Empty;

    private static (string Title, string Detail) Status(string? problem, IReadOnlyList<DeviceRowViewModel> rows, int behindForAnHour)
    {
        if (problem is not null)
        {
            return ("Can't sync", problem);
        }

        if (rows.Count <= 1)
        {
            return ("Only this PC", "Link a phone or PC in Devices & sync");
        }

        if (behindForAnHour >= 2)
        {
            return ($"{behindForAnHour} devices are behind", "Nothing from them for over an hour");
        }

        string count = $"{rows.Count} devices";
        DeviceRowViewModel? newest = rows.Skip(1).Where(row => row.Age is not null).OrderBy(row => row.Age).FirstOrDefault();
        string detail = newest?.Age is TimeSpan age ? $"{count} · last sync {DevicesViewModel.Ago(age)}" : $"{count} · waiting for your other devices";
        List<string> behind = rows.Skip(1).Where(IsBehind).Select(row => row.Name).ToList();
        string title = behind.Count switch
        {
            0 => newest is null ? "Waiting" : "All in step",
            1 => $"{behind[0]} is behind",
            _ => $"{behind.Count} devices are behind",
        };
        return (title, detail);
    }

    // A device's line in the menu: "Synced 2 min ago" (older than an hour: the 12-hour time it synced), a watch "Through the phone · 14 min ago".
    private string DeviceWhen(DeviceRowViewModel row)
    {
        string? when = row.Age is TimeSpan age ? When(age) : null;
        if (row.Indented)
        {
            return when is null
                ? row.State == DeviceSyncState.Unlinked ? "Through the phone · not found" : "Through the phone · not heard from yet"
                : $"Through the phone · {when}";
        }

        return when is null ? "Not heard from yet" : $"Synced {when}";
    }

    private string When(TimeSpan age)
    {
        if (age < TimeSpan.FromHours(1))
        {
            return DevicesViewModel.Ago(age);
        }

        DateTime now = _time.GetLocalNow().DateTime;
        DateTime at = now - age;
        string clock = OverlayTimeText.Clock(at);
        if (at.Date == now.Date)
        {
            return clock;
        }

        return at.Date > now.Date.AddDays(-7)
            ? $"{at.ToString("ddd", System.Globalization.CultureInfo.InvariantCulture)} {clock}"
            : at.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The light's tip: "Synced · Pixel 8, Home desktop", "Behind · Pixel 8", or this PC's sync problem.</summary>
    [ObservableProperty]
    public partial string TipText { get; set; } = string.Empty;

    private static string Tip(string? problem, IReadOnlyList<DeviceRowViewModel> rows)
    {
        if (problem is not null)
        {
            return $"Sync problem · {problem}";
        }

        List<string> behind = rows.Skip(1).Where(IsBehind).Select(row => row.Name).ToList();
        if (behind.Count > 0)
        {
            return $"Behind · {string.Join(", ", behind)}";
        }

        List<string> synced = rows.Skip(1).Where(row => row.State == DeviceSyncState.UpToDate).Select(row => row.Name).ToList();
        return synced.Count > 0 ? $"Synced · {string.Join(", ", synced)}" : "Waiting for your other devices";
    }

    private static bool IsBehind(DeviceRowViewModel row)
    {
        return row.State == DeviceSyncState.Behind;
    }

    private static string Summary(IReadOnlyList<DeviceRowViewModel> rows)
    {
        DeviceRowViewModel? newest = rows.Skip(1).Where(row => row.Age is not null).OrderBy(row => row.Age).FirstOrDefault();
        return newest is null ? "Waiting for your other devices" : $"Synced {newest.LastText}";
    }

    // Grouped as in the design: PCs, then phones, then watches.
    private static int KindOrder(DeviceRowViewModel row)
    {
        return row.Kind switch
        {
            "This PC" => 0,
            "PC" => 1,
            "Phone" => 2,
            _ => 3,
        };
    }

    private static MaterialIconKind IconFor(string kind)
    {
        return kind switch
        {
            "Phone" => MaterialIconKind.Cellphone,
            "Watch" => MaterialIconKind.Watch,
            _ => MaterialIconKind.Laptop,
        };
    }
}
