using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>A paired phone's daily step counts: from the phone file with the newest read time.</summary>
public static class PhoneSteps
{
    /// <summary>Steps read longer ago than this are ignored: a phone that turned them off where only a QR file reaches this PC would otherwise stay forever.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(3);

    /// <returns>The newest <see cref="SyncSteps"/> of the phone files that carry any and were read within <see cref="MaxAge"/> of <paramref name="now"/>; null when none does.</returns>
    public static SyncSteps? Of(IReadOnlyList<ReceivedDevice> devices, DateTimeOffset now)
    {
        return devices
            .Where(device => device.File.Device.Kind == SyncDeviceCard.KindPhone && device.File.Steps is not null)
            .Select(device => device.File.Steps!)
            .Where(steps => IsFresh(steps, now))
            .OrderByDescending(steps => steps.ReadAt)
            .FirstOrDefault();
    }

    public static bool IsFresh(SyncSteps steps, DateTimeOffset now) => now - steps.ReadAt <= MaxAge;
}
