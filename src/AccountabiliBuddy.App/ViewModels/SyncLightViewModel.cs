using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The title bar's sync light (design 20a): a dot and one glyph per device, read from what the Devices page already knows
/// (<see cref="DevicesViewModel.Rows"/> and <see cref="DevicesViewModel.SyncProblem"/>). Display only; it changes nothing.
/// Green = all in step. A glyph dims when its device is behind (older than its interval plus 5 minutes). A hollow ring
/// shows while a sync runs. The lilac dot means 2 or more devices are more than an hour old; the red one that this device
/// can't sync (offline, or its sync method is failing). With no other device linked the light is hidden.
/// </summary>
public sealed partial class SyncLightViewModel : ObservableObject
{
    private static readonly TimeSpan SeveralBehindAge = TimeSpan.FromHours(1);

    private readonly DevicesViewModel _devices;

    public SyncLightViewModel(DevicesViewModel devices)
    {
        _devices = devices;
        devices.RowsRefreshed += (_, _) => Rebuild();
        devices.PropertyChanged += OnDevicesChanged;
        Rebuild();
    }

    /// <summary>Every device, this PC first, in the order of the glyphs and of the popover's lines.</summary>
    public ObservableCollection<SyncLightDeviceViewModel> Devices { get; } = new();

    /// <summary>False with no other device linked: only the sync button shows then.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>A sync is running: the dot turns into a ring (the title bar's sync button sets it).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDotSolid))]
    public partial bool IsSyncing { get; set; }

    public bool IsDotSolid => !IsSyncing;

    /// <summary>2 or more other devices are more than an hour old: the dot turns lilac.</summary>
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
        IReadOnlyList<DeviceRowViewModel> rows = _devices.Rows.ToList();
        string? problem = _devices.SyncProblem;
        bool hasOthers = rows.Count > 1;

        Devices.Clear();
        foreach (DeviceRowViewModel row in rows.OrderBy(KindOrder).ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            bool isThisPc = row.Kind == "This PC";
            bool dim = isThisPc ? problem is not null : row.State != DeviceSyncState.UpToDate;
            string ago = isThisPc ? "this PC" : row.Age is TimeSpan age ? DevicesViewModel.Ago(age) : "not yet";
            Devices.Add(new SyncLightDeviceViewModel(row.Name, IconFor(row.Kind), ago, dim));
        }

        IsVisible = hasOthers;
        IsOffline = problem is not null;
        IsSeveralBehind = problem is null && rows.Skip(1).Count(row => row.Age is TimeSpan age && age > SeveralBehindAge) >= 2;
        SummaryText = problem ?? Summary(rows);
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
            "Watch" => MaterialIconKind.Smartwatch,
            _ => MaterialIconKind.Laptop,
        };
    }
}
