using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>Finds the newest phone placement that belongs to this PC's session (phone placement).</summary>
public static class PhonePlacementLookup
{
    // The phone echoes the session's start back; a rounding difference is fine, a different session is not.
    public static readonly TimeSpan StartTolerance = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The placement of the newest phone file whose session start is <paramref name="sessionStart"/>, with when that file was
    /// written; null when no phone sent one for this session.
    /// </summary>
    public static (SyncPlacement Placement, DateTimeOffset WrittenAt)? Newest(IEnumerable<ReceivedDevice> devices, DateTimeOffset sessionStart)
    {
        return devices
            .Where(device => device.File.Device.Kind == SyncDeviceCard.KindPhone && device.File.Placement is not null)
            .OrderByDescending(device => device.File.Device.WrittenAt)
            .Select(device => (Placement: device.File.Placement!, WrittenAt: device.File.Device.WrittenAt))
            .Where(entry => (entry.Placement.SessionStart - sessionStart).Duration() <= StartTolerance)
            .Select(entry => ((SyncPlacement, DateTimeOffset)?)entry)
            .FirstOrDefault();
    }
}
