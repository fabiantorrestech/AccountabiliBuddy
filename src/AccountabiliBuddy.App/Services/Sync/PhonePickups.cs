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
        if (For(sessionStart) is not SyncPickup pickup)
        {
            return null;
        }

        string down = SessionStatusFormatter.FormatDuration(TimeSpan.FromMinutes(pickup.LongestDownMinutes));
        string emergency = pickup.EmergencyMinutes > 0
            ? $" · emergency {SessionStatusFormatter.FormatDuration(TimeSpan.FromMinutes(pickup.EmergencyMinutes))}"
            : string.Empty;
        return pickup.Count == 0
            ? $"Phone: not picked up · down {down}{emergency}"
            : $"Phone: picked up {pickup.Count}× · longest down {down}{emergency}";
    }

    /// <summary>True when the session that started at <paramref name="sessionStart"/> was phone-free (so far).</summary>
    public bool IsPhoneFree(DateTimeOffset sessionStart) => For(sessionStart) is { Count: 0 };

    /// <summary>The starts of this PC's sessions the phone saw no pickup in (a running one counts so far).</summary>
    public IReadOnlyList<DateTimeOffset> PhoneFreeSessions()
    {
        return Entries()
            .GroupBy(entry => entry.SessionStart)
            .Select(group => group.MaxBy(entry => entry.CountedUntil)!)
            .Where(entry => entry.Count == 0)
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
