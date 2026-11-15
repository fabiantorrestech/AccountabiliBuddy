namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Which device of the group has a focus session running, from the sync files received from it (ADR 85, rules R1 to R3 and
/// the receiving-side rules of the plan check, B4, and of the PS1a review). The one place the "live" rule lives: a PC file by the rule PCs
/// have always used (ADR 60), a phone file by the phone rule. Pure; the caller passes a trusted <c>now</c> and, for each file, the
/// trusted time it arrived (<see cref="ReceivedSyncFile.ReceivedAt"/>). A phone's own stamp is never trusted past that arrival time:
/// <see cref="HeardAt"/>.
/// </summary>
public static class GroupSessions
{
    /// <summary>ADR 60 and ADR 85 R2: a file quiet for longer than this isn't running a session another device should wait for.</summary>
    public static readonly TimeSpan QuietAfter = TimeSpan.FromMinutes(15);

    /// <summary>R3: a quiet phone session is reported as "last heard" only for this long; after it the session is taken as over.</summary>
    public static readonly TimeSpan StaleFor = TimeSpan.FromHours(4);

    /// <summary>A phone file stamped further ahead of the receiver's trusted now than this is not live (clock skew).</summary>
    public static readonly TimeSpan AheadTolerance = TimeSpan.FromMinutes(2);

    // A PC planning stage is believed only until its time, the focus length it leads to and this much more (the phone's rule).
    private static readonly TimeSpan PlanningSlack = TimeSpan.FromMinutes(10);

    // The bound when a planning session has no set length.
    private static readonly TimeSpan PlanningWithoutLengthBound = TimeSpan.FromHours(12);

    // A PC session with no set end counts as running only while its PC file is younger than this (the phone's rule).
    private static readonly TimeSpan NoSetEndFreshness = TimeSpan.FromMinutes(30);

    /// <summary>
    /// True when <paramref name="file"/> carries a running session that is live at <paramref name="now"/>. PC files by the PC rule
    /// (unchanged, except that a file's age is taken from <see cref="HeardAt"/>, which is the file's time on an honest clock). Phone files (R2): no <c>offSince</c>, not stamped more than
    /// <see cref="AheadTolerance"/> ahead of <paramref name="now"/>, heard from within <see cref="QuietAfter"/> (<see cref="HeardAt"/>) and not
    /// past its set end (a set end that has passed still counts while the user is away, or when there is none). Any other kind is never live.
    /// </summary>
    /// <param name="receivedAt">When this file arrived, on the receiver's trusted clock.</param>
    public static bool IsLive(SyncFile file, DateTimeOffset receivedAt, DateTimeOffset now)
    {
        if (file.Running is not SyncRunning running)
        {
            return false;
        }

        return file.Device.Kind switch
        {
            SyncDeviceCard.KindPc => IsPcLive(running, HeardAt(file.Device, receivedAt), now),
            SyncDeviceCard.KindPhone => IsPhoneLive(running, file.Device.WrittenAt, HeardAt(file.Device, receivedAt), now),
            _ => false,
        };
    }

    /// <summary>
    /// The live session on another device, or null for none (R2). Several live: the one heard from last (<see cref="HeardAt"/>; a tie: the
    /// lower device id, as the id's text in order).
    /// </summary>
    /// <param name="others">The received files of the other devices (never this device's own).</param>
    public static GroupSessionLive? LiveElsewhere(IEnumerable<ReceivedSyncFile> others, DateTimeOffset now)
    {
        ReceivedSyncFile? newest = others
            .Where(other => IsLive(other.File, other.ReceivedAt, now))
            .OrderByDescending(other => HeardAt(other.File.Device, other.ReceivedAt))
            .ThenBy(other => other.File.Device.DeviceId.ToString("D"), StringComparer.Ordinal)
            .FirstOrDefault();
        if (newest is null)
        {
            return null;
        }

        SyncFile file = newest.File;
        SyncRunning running = file.Running!;
        DateTimeOffset? endsAt = running.PhaseEndsAt is DateTimeOffset end ? end + AwayGap(running, now) : null;
        return new GroupSessionLive(
            file.Device.DeviceId, file.Device.Name, file.Device.Kind, endsAt, running.AwaySince is not null, running.PhaseEndsAt is null);
    }

    /// <summary>
    /// A phone session that is quiet (R3): the phone heard from last whose file says a session runs, with no <c>offSince</c>, that would be
    /// live but for being quiet for longer than <see cref="QuietAfter"/> (and for no longer than <see cref="StaleFor"/>); null for none.
    /// Start shows its "last heard" line (<see cref="GroupSessionStale.LastHeardAt"/> is <see cref="HeardAt"/>).
    /// </summary>
    public static GroupSessionStale? StaleElsewhere(IEnumerable<ReceivedSyncFile> others, DateTimeOffset now)
    {
        ReceivedSyncFile? newest = others
            .Where(other => other.File.Device.Kind == SyncDeviceCard.KindPhone
                            && other.File.Running is { OffSince: null } running
                            && now - HeardAt(other.File.Device, other.ReceivedAt) > QuietAfter
                            && now - HeardAt(other.File.Device, other.ReceivedAt) <= StaleFor
                            && PhaseStillOn(running, now))
            .OrderByDescending(other => HeardAt(other.File.Device, other.ReceivedAt))
            .ThenBy(other => other.File.Device.DeviceId.ToString("D"), StringComparer.Ordinal)
            .FirstOrDefault();
        return newest is null
            ? null
            : new GroupSessionStale(newest.File.Device.DeviceId, newest.File.Device.Name, HeardAt(newest.File.Device, newest.ReceivedAt));
    }

    /// <summary>
    /// When the receiver last heard from a device (PS1a review, B2): the sender's own stamp, but never later than the time its file arrived
    /// here (a clock running ahead can't make a file look fresh or newer). A stamp no more than <see cref="AheadTolerance"/> ahead of the
    /// arrival is believed (honest clock skew between devices); on an honest clock this is the stamp.
    /// </summary>
    public static DateTimeOffset HeardAt(SyncDeviceCard device, DateTimeOffset receivedAt)
    {
        return device.WrittenAt > receivedAt + AheadTolerance ? receivedAt : device.WrittenAt;
    }

    /// <summary>
    /// When a device's open session piece ends as a receiver counts it (any device): when it was last heard from (<see cref="HeardAt"/>),
    /// never after <paramref name="now"/>, and never after the moment the file says the user went away (<see cref="SyncRunning.AwaySince"/>:
    /// pause, lunch or the device off), so paused time is never focus on the receiver (owner "cut paused time", PS1a review B1).
    /// </summary>
    public static DateTimeOffset OpenPieceEnd(SyncFile file, DateTimeOffset receivedAt, DateTimeOffset now)
    {
        DateTimeOffset end = HeardAt(file.Device, receivedAt);
        if (now < end)
        {
            end = now;
        }

        return file.Running?.AwaySince is DateTimeOffset away && away < end ? away : end;
    }

    private static bool IsPhoneLive(SyncRunning running, DateTimeOffset writtenAt, DateTimeOffset heardAt, DateTimeOffset now)
    {
        return running.OffSince is null && writtenAt <= now + AheadTolerance && now - heardAt <= QuietAfter && PhaseStillOn(running, now);
    }

    private static bool PhaseStillOn(SyncRunning running, DateTimeOffset now)
    {
        return running.PhaseEndsAt is not DateTimeOffset end || running.AwaySince is not null || now < end;
    }

    // While away the timer is paused, so the session ends that much later than the file's end says.
    private static TimeSpan AwayGap(SyncRunning running, DateTimeOffset now)
    {
        return running.AwaySince is DateTimeOffset since && now > since ? now - since : TimeSpan.Zero;
    }

    // ADR 60: a PC that said it was going off, or whose file went quiet, isn't running a session another PC should wait for.
    private static bool IsPcLive(SyncRunning running, DateTimeOffset heardAt, DateTimeOffset now)
    {
        if (running.OffSince is not null || now - heardAt > QuietAfter)
        {
            return false;
        }

        if (running.PlanningEndsAt is DateTimeOffset planningEnd)
        {
            TimeSpan focusLength = running.PhaseEndsAt is DateTimeOffset end ? end - running.PhaseStartedAt : PlanningWithoutLengthBound;
            return now < planningEnd + focusLength + PlanningSlack;
        }

        // A session with no set end counts only while its PC's file is fresh (the hard rule), so a PC that went off
        // mid-session doesn't hold things back for good.
        return running.PhaseEndsAt is DateTimeOffset phaseEnd ? now < phaseEnd : now - heardAt < NoSetEndFreshness;
    }
}
