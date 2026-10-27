using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>Whether another of the user's PCs has a focus session running, as the files received from them say.</summary>
public static class OtherPcSessions
{
    // A planning stage is believed only until its time, the focus length it leads to and this much more (the phone's rule).
    private static readonly TimeSpan PlanningSlack = TimeSpan.FromMinutes(10);

    // The bound when a planning session has no set length.
    private static readonly TimeSpan PlanningWithoutLengthBound = TimeSpan.FromHours(12);

    // A session with no set end counts as running only while its PC file is younger than this (the phone's rule).
    private static readonly TimeSpan NoSetEndFreshness = TimeSpan.FromMinutes(30);

    /// <summary>
    /// True when a received PC file carries a running session that is still live at <paramref name="now"/>: before its phase's
    /// end, with no set end, or while planning (see <see cref="SyncRunning"/>). A PC that went off mid-session stops counting
    /// once its phase end has passed, as on the phone.
    /// </summary>
    public static bool IsRunning(IEnumerable<ReceivedDevice> devices, DateTimeOffset now)
    {
        return devices
            .Where(device => device.File.Device.Kind == SyncDeviceCard.KindPc && device.File.Running is not null)
            .Any(device => IsLive(device.File.Running!, device.File.Device.WrittenAt, now));
    }

    private static bool IsLive(SyncRunning running, DateTimeOffset writtenAt, DateTimeOffset now)
    {
        if (running.PlanningEndsAt is DateTimeOffset planningEnd)
        {
            TimeSpan focusLength = running.PhaseEndsAt is DateTimeOffset end ? end - running.PhaseStartedAt : PlanningWithoutLengthBound;
            return now < planningEnd + focusLength + PlanningSlack;
        }

        // A session with no set end counts only while its PC's file is fresh (the hard rule), so a PC that went off
        // mid-session doesn't hold things back for good.
        return running.PhaseEndsAt is DateTimeOffset phaseEnd ? now < phaseEnd : now - writtenAt < NoSetEndFreshness;
    }
}
