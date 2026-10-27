using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The user's phone's pickups during this PC's sessions (ADR 26), from the phones' sync files: how often it was
/// unlocked, the longest stretch it stayed down, which sessions were phone-free, and the daily phone-free streak.
/// </summary>
public sealed class PhonePickups
{
    // A session's start as the PC wrote it and as the phone echoes it back can differ by rounding.
    private static readonly TimeSpan StartTolerance = TimeSpan.FromSeconds(5);

    /// <summary>More than this many minutes in a pocket and a session isn't phone-free (the phone's "No phone-free credit" penalty).</summary>
    public const int PocketLimitMinutes = 5;

    // The phone's pocket switch arrived with 0.1.63; an older phone has none, so its pocket minutes never cost the credit.
    private static readonly Version PocketSwitchSince = new(0, 1, 63);

    private readonly ReceivedDevicesService _received;
    private readonly SyncIdentityService _identity;
    private readonly TimeProvider _time;

    public PhonePickups(ReceivedDevicesService received, SyncIdentityService identity, TimeProvider time)
    {
        _received = received;
        _identity = identity;
        _time = time;
    }

    /// <summary>True when a phone in the group counts pickups (it has Usage access and sends them).</summary>
    public bool IsCounting => Entries().Any() || _received.Devices.Any(device => device.File.Device.Kind == SyncDeviceCard.KindPhone && device.File.Pickups is not null);

    /// <summary>The phone's newest count for this PC's session that started at <paramref name="sessionStart"/>; null when none came.</summary>
    public SyncPickup? For(DateTimeOffset sessionStart)
    {
        return Entries()
            .Where(entry => (entry.SessionStart - sessionStart).Duration() <= StartTolerance)
            .MaxBy(entry => entry.CountedUntil);
    }

    /// <summary>E.g. "Phone: not picked up · down 34m" or "Phone: picked up 2× · longest down 18m"; null when nothing came.</summary>
    public string? Describe(DateTimeOffset sessionStart)
    {
        string? others = DescribeOtherPcs(sessionStart);
        string? placement = DescribePlacement(sessionStart);
        if (placement is not null)
        {
            others = others is null ? placement : $"{placement} · {others}";
        }

        if (For(sessionStart) is not SyncPickup pickup)
        {
            return others;
        }

        string down = SessionStatusFormatter.FormatDuration(TimeSpan.FromMinutes(pickup.LongestDownMinutes));
        string emergency = pickup.EmergencyMinutes > 0
            ? $" · emergency {SessionStatusFormatter.FormatDuration(TimeSpan.FromMinutes(pickup.EmergencyMinutes))}"
            : string.Empty;
        string phone = pickup.Count == 0
            ? $"Phone: not picked up · down {down}{emergency}"
            : $"Phone: picked up {pickup.Count}× · longest down {down}{emergency}";
        return others is null ? phone : $"{phone} · {others}";
    }

    /// <summary>E.g. "out 2× · pocket 14 min" from the phone's placement for the session; null when the phone sent none.</summary>
    public string? DescribePlacement(DateTimeOffset sessionStart)
    {
        if (PhonePlacementLookup.Newest(_received.Devices, sessionStart) is not { } found)
        {
            return null;
        }

        SyncPlacement placement = found.Placement;
        string pocket = placement.PocketMinutes > 0
            ? $" · pocket {placement.PocketMinutes} min{(PocketCostsCredit(found) ? " (no phone-free credit)" : string.Empty)}"
            : string.Empty;
        return $"out {placement.OutCount}×{pocket}";
    }

    /// <summary>E.g. "DESK-PC: used 2×" for each other PC that went back to during the session (design K); null for none.</summary>
    public string? DescribeOtherPcs(DateTimeOffset sessionStart)
    {
        Guid me = _identity.DeviceId;
        List<string> parts = _received.Devices
            .Where(device => device.File.Device.Kind == SyncDeviceCard.KindPc && device.File.Device.DeviceId != me)
            .Select(device => (device.File.Device.Name, Pickup: (device.File.Pickups ?? [])
                .Where(entry => entry.DeviceId == me && (entry.SessionStart - sessionStart).Duration() <= StartTolerance)
                .MaxBy(entry => entry.CountedUntil)))
            .Where(entry => entry.Pickup is { Count: > 0 })
            .Select(entry => $"{entry.Name}: used {entry.Pickup!.Count}×")
            .ToList();
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>
    /// True when the session that started at <paramref name="sessionStart"/> was phone-free (so far): no pickups and no more
    /// than <see cref="PocketLimitMinutes"/> minutes in a pocket. The phone sends 0 pocket minutes when its penalty is off.
    /// </summary>
    public bool IsPhoneFree(DateTimeOffset sessionStart) => For(sessionStart) is { Count: 0 } && !TooLongInPocket(sessionStart);

    private bool TooLongInPocket(DateTimeOffset sessionStart)
    {
        return PhonePlacementLookup.Newest(_received.Devices, sessionStart) is { } found && PocketCostsCredit(found);
    }

    // Only a phone whose card says 0.1.63 or later has the pocket switch; an unreadable version counts as older.
    private static bool PocketCostsCredit((SyncPlacement Placement, DateTimeOffset WrittenAt, string AppVersion) found)
    {
        return found.Placement.PocketMinutes > PocketLimitMinutes
            && Version.TryParse(found.AppVersion, out Version? version)
            && version >= PocketSwitchSince;
    }

    /// <summary>The starts of this PC's sessions the phone saw no pickup in (a running one counts so far).</summary>
    public IReadOnlyList<DateTimeOffset> PhoneFreeSessions()
    {
        return Entries()
            .GroupBy(entry => entry.SessionStart)
            .Select(group => group.MaxBy(entry => entry.CountedUntil)!)
            .Where(entry => entry.Count == 0 && !TooLongInPocket(entry.SessionStart))
            .Select(entry => entry.SessionStart)
            .ToList();
    }

    /// <summary>E.g. "Phone-free: 2 sessions today · 3-day streak"; empty when no phone counts.</summary>
    public string SummaryText()
    {
        if (!IsCounting)
        {
            return string.Empty;
        }

        TimeZoneInfo zone = _time.LocalTimeZone;
        DateTime today = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), zone).Date;
        HashSet<DateTime> days = PhoneFreeSessions().Select(start => TimeZoneInfo.ConvertTime(start, zone).Date).ToHashSet();
        int todayCount = PhoneFreeSessions().Count(start => TimeZoneInfo.ConvertTime(start, zone).Date == today);
        // Today counts once it has one; until then the streak runs to yesterday.
        DateTime day = days.Contains(today) ? today : today.AddDays(-1);
        int streak = 0;
        while (days.Contains(day))
        {
            streak++;
            day = day.AddDays(-1);
        }

        string sessions = todayCount == 1 ? "1 session today" : $"{todayCount} sessions today";
        return streak >= 2 ? $"Phone-free: {sessions} · {streak}-day streak" : $"Phone-free: {sessions}";
    }

    private IEnumerable<SyncPickup> Entries()
    {
        Guid me = _identity.DeviceId;
        return _received.Devices
            .Where(device => device.File.Device.Kind == SyncDeviceCard.KindPhone)
            .SelectMany(device => device.File.Pickups ?? [])
            .Where(entry => entry.DeviceId == me);
    }
}
