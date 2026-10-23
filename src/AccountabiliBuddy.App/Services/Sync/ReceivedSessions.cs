using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The running log across devices (ADR 22): the other devices' focus sessions in a period, how much focus time they
/// add to this PC's own (time already covered by a local session counts once), and each device's shade on the day bar.
/// </summary>
public static class ReceivedSessions
{
    // Shades of the Focus row's teal family, so another device's focus reads as focus but tells devices apart.
    private static readonly string[] Shades = ["#0E7490", "#0369A1", "#4338CA", "#6D28D9", "#047857", "#B45309"];

    /// <summary>Every received session touching [<paramref name="from"/>, <paramref name="to"/>), by start.</summary>
    public static IReadOnlyList<ReceivedSession> In(IReadOnlyList<ReceivedDevice> devices, DateTimeOffset from, DateTimeOffset to)
    {
        return devices
            .SelectMany(device => device.File.Sessions.Select(session => new ReceivedSession(
                device.File.Device.DeviceId,
                device.File.Device.Name,
                session.Start,
                session.End ?? device.File.Device.WrittenAt,
                session.Profile,
                session.Added)))
            .Where(session => session.End > from && session.Start < to && session.End > session.Start)
            .OrderBy(session => session.Start)
            .ToList();
    }

    /// <summary>The received devices' sessions, each one still open ending when its device's file was written.</summary>
    public static IEnumerable<AccountabiliBuddy.Core.Sync.SyncSession> Closed(IReadOnlyList<ReceivedDevice> devices)
    {
        return devices.SelectMany(device => device.File.Sessions.Select(session =>
            session.End is null ? session with { End = device.File.Device.WrittenAt } : session));
    }

    /// <summary>
    /// The focus time the received sessions add inside [<paramref name="from"/>, <paramref name="to"/>) on top of
    /// <paramref name="local"/>: only what no local session (or another received one) already covers.
    /// </summary>
    public static TimeSpan ExtraTime(IEnumerable<TimeRange> local, IReadOnlyList<ReceivedSession> received, DateTimeOffset from, DateTimeOffset to)
    {
        List<TimeRange> own = local.ToList();
        IEnumerable<TimeRange> all = own.Concat(received.Select(session => new TimeRange(session.Start, session.End)));
        return Covered(all, from, to) - Covered(own, from, to);
    }

    /// <summary>A device's shade on the Focus row, "#RRGGBB", the same for it every time.</summary>
    public static string ColorOf(Guid deviceId)
    {
        byte[] bytes = deviceId.ToByteArray();
        return Shades[(bytes[0] ^ bytes[15]) % Shades.Length];
    }

    /// <summary>The length of the union of <paramref name="ranges"/> inside [<paramref name="from"/>, <paramref name="to"/>): overlaps once.</summary>
    public static TimeSpan Covered(IEnumerable<TimeRange> ranges, DateTimeOffset from, DateTimeOffset to)
    {
        TimeSpan total = TimeSpan.Zero;
        DateTimeOffset? start = null;
        DateTimeOffset end = from;
        foreach (TimeRange range in ranges
                     .Select(range => new TimeRange(range.From > from ? range.From : from, range.To < to ? range.To : to))
                     .Where(range => range.To > range.From)
                     .OrderBy(range => range.From))
        {
            if (start is null || range.From > end)
            {
                if (start is DateTimeOffset open)
                {
                    total += end - open;
                }

                start = range.From;
                end = range.To;
            }
            else if (range.To > end)
            {
                end = range.To;
            }
        }

        return start is DateTimeOffset last ? total + (end - last) : total;
    }
}
