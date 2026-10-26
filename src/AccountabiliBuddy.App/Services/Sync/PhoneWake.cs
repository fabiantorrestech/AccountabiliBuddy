using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>A paired phone's estimated wake time per kind of morning (ADR 40): from the newest phone file that has one.</summary>
public static class PhoneWake
{
    /// <returns>Its wake estimate; null when no phone shares one.</returns>
    public static SyncWakeEstimate? Of(IReadOnlyList<ReceivedDevice> devices)
    {
        return devices
            .Where(device => device.File.Device.Kind == SyncDeviceCard.KindPhone && device.File.WakeEstimate is not null)
            .OrderByDescending(device => device.File.Device.WrittenAt)
            .Select(device => device.File.WakeEstimate)
            .FirstOrDefault();
    }
}
