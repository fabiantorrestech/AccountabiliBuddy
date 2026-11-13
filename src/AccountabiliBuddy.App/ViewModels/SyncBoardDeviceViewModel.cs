using AccountabiliBuddy.App.Services.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One other device under a method on the board: its dot, its detail line (kind, app version, last confirmed time) and, folded away, its items.</summary>
public sealed partial class SyncBoardDeviceViewModel : ObservableObject
{
    public SyncBoardDeviceViewModel(SyncBoardDevice device, DateTimeOffset now, TimeZoneInfo zone, bool expanded)
    {
        Name = device.Name;
        Dot = new SyncDotViewModel(device.Dot);
        Parts = device.Parts.Select(part => new SyncBoardPartViewModel(part)).ToList();
        string kind = device.Kind switch
        {
            "pc" => "PC",
            "phone" => "Phone",
            "watch" => "Watch",
            _ => "Device",
        };
        string confirmed = device.ConfirmedFile is { } at ? $" · confirmed your file from {SyncBoardWords.Clock(at, now, zone)}" : string.Empty;
        Detail = $"{kind} · app {device.AppVersion}{confirmed}";
        Key = device.Id;
        IsExpanded = expanded;
    }

    /// <summary>The device's id, kept so a refresh keeps the row open.</summary>
    public Guid Key { get; }

    public string Name { get; }

    public string Detail { get; }

    public SyncDotViewModel Dot { get; }

    public IReadOnlyList<SyncBoardPartViewModel> Parts { get; }

    /// <summary>The items list is open.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}
