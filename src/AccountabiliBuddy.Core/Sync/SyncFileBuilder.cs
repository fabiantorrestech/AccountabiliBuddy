using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Builds the PC's own sync file (ADR 22) from its history, and the smaller one its QR codes carry. Sessions count as
/// on the day bar and in History (<see cref="FocusDayBuilder"/>'s rule), but travel whole: each receiver cuts them at
/// its own midnights. Only readable fields go: times, profile names, apps, sites and their sections (ADR 15).
/// </summary>
public static class SyncFileBuilder
{
    /// <summary>
    /// The file for the last 14 days: the counted sessions that reach into them (the running one with no end), the
    /// drift in them joined into one mark per visit, the plan and the shared settings. The days are local, from the
    /// midnight before now − 14 × 24 hours, so a file without sessions speaks only for days it covers
    /// (<see cref="SyncMerge.FileSpan"/>). With Activity history off (<see cref="SyncDeviceCard.HistoryOff"/>) it carries
    /// no sessions or drift.
    /// </summary>
    /// <param name="card">The device's card; the file is written at <paramref name="now"/>, whatever its time says.</param>
    /// <param name="records">Newest first (<see cref="SessionHistoryBuilder.Build"/>), covering the 14 days and the day before them.</param>
    /// <param name="drift">Drift inside counted sessions in those days (omitted and scheduled time already left out).</param>
    /// <param name="sessionRunning">True while a session runs: the newest recorded session without an end is it.</param>
    public static SyncFile Build(
        SyncDeviceCard card,
        IReadOnlyList<SessionRecord> records,
        IReadOnlyList<DriftStretch> drift,
        Func<DateTimeOffset, bool> isOmitted,
        Func<Guid?, string?> profileName,
        bool sessionRunning,
        SyncDayPlan? dayPlan,
        IReadOnlyList<SyncDocument> documents,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        SyncDeviceCard written = card with { WrittenAt = now };
        if (card.HistoryOff)
        {
            return new SyncFile(SyncFile.CurrentFormatVersion, written, [], [], dayPlan, documents);
        }

        DateTimeOffset from = StartOfDay(now - SyncMerge.FileSpan, zone);
        List<SyncSession> sessions = FocusDayBuilder.CountedRecords(records, isOmitted, sessionRunning, now)
            .Where(counted => counted.End > from)
            .OrderBy(counted => counted.Record.StartedAt)
            .Select(counted =>
            {
                bool added = counted.Record.Mode == SessionMode.Added;
                return new SyncSession(counted.Record.StartedAt, counted.Running ? null : counted.End,
                    added ? null : profileName(counted.Record.ProfileId), added);
            })
            .ToList();
        List<SyncDrift> marks = DriftMarks.Merge(drift, FocusDayBuilder.DriftGap)
            .Where(mark => mark.End > from)
            .Select(mark => new SyncDrift(mark.Start, mark.End, mark.ProcessName, mark.Domain, mark.Section))
            .ToList();
        return new SyncFile(SyncFile.CurrentFormatVersion, written, sessions, marks, dayPlan, documents);
    }

    /// <summary>
    /// Today's part of <paramref name="file"/> for QR codes: the sessions that reach into the local day it was written
    /// on, and the drift from their start. With none today the newest session goes instead, so the file still speaks
    /// only from that session on (<see cref="SyncMerge"/>). When the sealed file needs more than
    /// <paramref name="maxCodes"/> codes of <see cref="SyncQr.BytesPerCode"/> bytes, the oldest drift goes first, then
    /// the settings (the biggest first), then the plan, and last the oldest sessions (the newest one always stays).
    /// </summary>
    /// <param name="zone">The device's time zone, for "today".</param>
    public static SyncFile ForQr(SyncFile file, TimeZoneInfo zone, int maxCodes = SyncQr.MaxCodes)
    {
        int maxBytes = maxCodes * SyncQr.BytesPerCode;
        DateTimeOffset writtenAt = file.Device.WrittenAt;
        DateTimeOffset todayStart = StartOfDay(writtenAt, zone);
        List<SyncSession> sessions = file.Sessions
            .Where(session => (session.End ?? writtenAt) > todayStart)
            .OrderBy(session => session.Start)
            .ToList();
        if (sessions.Count == 0 && file.Sessions.Count > 0)
        {
            sessions.Add(file.Sessions.MaxBy(session => session.Start)!);
        }

        DateTimeOffset from = sessions.Count > 0 ? sessions[0].Start : todayStart;
        SyncFile today = file with
        {
            Sessions = sessions,
            Drift = file.Drift.Where(mark => mark.Start >= from).OrderBy(mark => mark.Start).ToList(),
        };
        if (Fits(today, maxBytes))
        {
            return today;
        }

        int driftKept = NewestThatFit(today.Drift.Count, minimum: 0, count => Fits(today with { Drift = Newest(today.Drift, count) }, maxBytes));
        today = today with { Drift = Newest(today.Drift, driftKept) };
        foreach (SyncDocument document in today.Documents.OrderByDescending(document => document.Json.Length).ToList())
        {
            if (Fits(today, maxBytes))
            {
                return today;
            }

            today = today with { Documents = today.Documents.Where(kept => !ReferenceEquals(kept, document)).ToList() };
        }

        if (Fits(today, maxBytes))
        {
            return today;
        }

        today = today with { DayPlan = null };
        int sessionsKept = NewestThatFit(today.Sessions.Count, minimum: Math.Min(1, today.Sessions.Count),
            count => Fits(today with { Sessions = Newest(today.Sessions, count) }, maxBytes));
        return today with { Sessions = Newest(today.Sessions, sessionsKept) };
    }

    private static bool Fits(SyncFile file, int maxBytes)
    {
        return SyncFileCodec.SealedLength(file) <= maxBytes;
    }

    /// <summary>
    /// The most of the newest items that fit, at least <paramref name="minimum"/>, searched by halves: fewer items
    /// never seal much larger. The count returned has been checked to fit, unless it's the minimum.
    /// </summary>
    private static int NewestThatFit(int available, int minimum, Func<int, bool> fits)
    {
        if (fits(available))
        {
            return available;
        }

        int fitting = minimum;
        int tooMany = available;
        while (tooMany - fitting > 1)
        {
            int middle = fitting + (tooMany - fitting) / 2;
            if (fits(middle))
            {
                fitting = middle;
            }
            else
            {
                tooMany = middle;
            }
        }

        return fitting;
    }

    /// <returns>The last <paramref name="count"/> items (the list is in time order).</returns>
    private static List<T> Newest<T>(IReadOnlyList<T> items, int count)
    {
        return items.Skip(items.Count - count).ToList();
    }

    /// <summary>Local midnight of the day <paramref name="time"/> falls on in <paramref name="zone"/>.</summary>
    private static DateTimeOffset StartOfDay(DateTimeOffset time, TimeZoneInfo zone)
    {
        return FocusDayBuilder.At(FocusDayBuilder.Local(time, zone).Date, zone);
    }
}
