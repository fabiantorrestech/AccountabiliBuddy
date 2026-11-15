using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>Whether another of the user's PCs has a focus session running, as the files received from them say.</summary>
public static class OtherPcSessions
{
    /// <summary>
    /// True when a received PC file carries a running session that is still live at <paramref name="now"/>: before its phase's
    /// end, with no set end, or while planning (see <see cref="SyncRunning"/>). A PC that went off mid-session stops counting
    /// once its phase end has passed, as on the phone. The rule itself is <see cref="GroupSessions.IsLive"/> (ADR 85).
    /// </summary>
    public static bool IsRunning(IEnumerable<ReceivedDevice> devices, DateTimeOffset now)
    {
        return devices
            .Where(device => device.File.Device.Kind == SyncDeviceCard.KindPc && device.File.Running is not null)
            .Any(device => GroupSessions.IsLive(device.File, device.ReceivedAt, now));
    }
}
