using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Which other device of the group has a focus session live, from the sync files this PC has received (ADR 85, rules R1 to R3). The
/// one door the PC's Start paths, title bar, Today, the lunch auto-start and the start nudge ask. The rule itself is
/// <see cref="GroupSessions"/> (PC files by the PC rule, phone files by the phone rule); this only feeds it the active devices
/// (never a removed one) with their arrival times, on the app's trusted clock. Cheap enough to ask every second.
/// </summary>
public sealed class SessionsElsewhere
{
    private readonly ReceivedDevicesService _received;
    private readonly TimeProvider _time;

    public SessionsElsewhere(ReceivedDevicesService received, TimeProvider time)
    {
        _received = received;
        _time = time;
    }

    /// <summary>The session live on another device now (R2), or null. Several live: the one heard from last.</summary>
    public GroupSessionLive? Live => LiveAmong(_received.Active, _time.GetUtcNow());

    /// <summary>R3: a phone's session that went quiet; Start is back with its "last heard" line. Null while one is live.</summary>
    public GroupSessionStale? Stale => Live is null ? GroupSessions.StaleElsewhere(Files(_received.Active), _time.GetUtcNow()) : null;

    /// <summary>The live session among <paramref name="devices"/> (every received file counts, a sender's own included), or null.</summary>
    public static GroupSessionLive? LiveAmong(IEnumerable<ReceivedDevice> devices, DateTimeOffset now)
    {
        return GroupSessions.LiveElsewhere(Files(devices), now);
    }

    private static IEnumerable<ReceivedSyncFile> Files(IEnumerable<ReceivedDevice> devices)
    {
        return devices.Select(device => new ReceivedSyncFile(device.File, device.ReceivedAt));
    }
}
