using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>Whether another of the user's PCs has a focus session running, as the files received from them say.</summary>
public static class OtherPcSessions
{
    // A planning stage is believed only until its time, the focus length it leads to and this much more (the phone's rule).
    private static readonly TimeSpan PlanningSlack = TimeSpan.FromMinutes(10);

    // The bound when a planning session has no set length.
    private static readonly TimeSpan PlanningWithoutLengthBound = TimeSpan.FromHours(12);

    /// <summary>
    /// True when a received PC file carries a running session that is still live at <paramref name="now"/>: before its phase's
    /// end, with no set end, or while planning (see <see cref="SyncRunning"/>). A PC that went off mid-session stops counting
    /// once its phase end has passed, as on the phone.
    /// </summary>
    public static bool IsRunning(IEnumerable<ReceivedDevice> devices, DateTimeOffset now)
    {
        return devices
            .Where(device => device.File.Device.Kind == SyncDeviceCard.KindPc && device.File.Running is not null)
            .Any(device => IsLive(device.File.Running!, now));
    }

    private static bool IsLive(SyncRunning running, DateTimeOffset now)
    {
        if (running.PlanningEndsAt is DateTimeOffset planningEnd)
        {
            TimeSpan focusLength = running.PhaseEndsAt is DateTimeOffset end ? end - running.PhaseStartedAt : PlanningWithoutLengthBound;
            return now < planningEnd + focusLength + PlanningSlack;
        }

        return running.PhaseEndsAt is not DateTimeOffset phaseEnd || now < phaseEnd;
    }
}
