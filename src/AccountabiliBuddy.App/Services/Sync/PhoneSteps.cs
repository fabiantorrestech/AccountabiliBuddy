using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>A paired phone's daily step counts: from the phone file with the newest read time.</summary>
public static class PhoneSteps
{
    /// <returns>The newest <see cref="SyncSteps"/> of the phone files that carry any; null when no phone shares steps.</returns>
    public static SyncSteps? Of(IReadOnlyList<ReceivedDevice> devices)
    {
        return devices
            .Where(device => device.File.Device.Kind == SyncDeviceCard.KindPhone && device.File.Steps is not null)
            .Select(device => device.File.Steps)
            .OrderByDescending(steps => steps!.ReadAt)
            .FirstOrDefault();
    }
}
