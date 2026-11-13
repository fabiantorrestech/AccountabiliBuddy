using AccountabiliBuddy.App.Services.Sync;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One sync method in use: its title and a row for each other device; "No other devices yet" while this PC is alone (no red).</summary>
public sealed class SyncBoardMethodViewModel
{
    public SyncBoardMethodViewModel(SyncBoardMethod method, DateTimeOffset now, TimeZoneInfo zone, ISet<(string Method, Guid Device)> open)
    {
        Method = method.Method;
        Title = method.Title;
        Devices = method.Devices.Select(device => new SyncBoardDeviceViewModel(device, now, zone, open.Contains((method.Method, device.Id)))).ToList();
    }

    /// <summary>The method's code (folder, drive, relay, qr, file).</summary>
    public string Method { get; }

    public string Title { get; }

    public IReadOnlyList<SyncBoardDeviceViewModel> Devices { get; }

    public bool HasDevices => Devices.Count > 0;

    public bool IsAlone => Devices.Count == 0;
}
