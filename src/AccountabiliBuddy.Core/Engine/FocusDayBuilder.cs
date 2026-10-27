using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// The day bar's and History's rule for which focus sessions count, shared with <see cref="Sync.SyncFileBuilder"/> and
/// <see cref="DeepWork"/>. Sessions the user started and ones added by hand count; omitted sessions and old scheduled
/// blocks don't. The session running now counts until now; an older session without an end stopped with the app at an
/// unknown time and is left out.
/// </summary>
public static class FocusDayBuilder
{
    /// <summary>Coming back to the same site within this long is one visit (as on the day bar; sync files too).</summary>
    internal static readonly TimeSpan DriftGap = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The rule for which recorded sessions count, shared with <see cref="Sync.SyncFileBuilder"/>: not omitted ones or
    /// old scheduled blocks, and of the sessions without an end only the newest the app recorded, while it runs.
    /// </summary>
    /// <returns>In the order of <paramref name="records"/>; the running one ends at <paramref name="now"/>.</returns>
    internal static List<(SessionRecord Record, DateTimeOffset End, bool Running)> CountedRecords(
        IReadOnlyList<SessionRecord> records, Func<DateTimeOffset, bool> isOmitted, bool sessionRunning, DateTimeOffset now)
    {
        // The session running now is the newest one the app recorded (an added one never runs).
        int newestRecorded = -1;
        for (int i = 0; i < records.Count && newestRecorded < 0; i++)
        {
            newestRecorded = records[i].Mode == SessionMode.Added ? -1 : i;
        }

        var counted = new List<(SessionRecord Record, DateTimeOffset End, bool Running)>();
        for (int i = 0; i < records.Count; i++)
        {
            SessionRecord record = records[i];
            if (isOmitted(record.StartedAt) || record.Mode == SessionMode.Scheduled)
            {
                continue;
            }

            if (record.EndedAt is DateTimeOffset ended)
            {
                if (ended > record.StartedAt)
                {
                    counted.Add((record, ended, Running: false));
                }
            }
            else if (i == newestRecorded && sessionRunning && now > record.StartedAt)
            {
                counted.Add((record, now, Running: true));
            }
        }

        return counted;
    }

    internal static DateTime Local(DateTimeOffset time, TimeZoneInfo zone)
    {
        return TimeZoneInfo.ConvertTime(time, zone).DateTime;
    }

    internal static DateTimeOffset At(DateTime local, TimeZoneInfo zone)
    {
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
