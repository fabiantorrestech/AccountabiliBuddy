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
    /// midnight before now − 14 × 24 hours, so they always hold <see cref="SyncMerge.FileSpan"/>; the file speaks from
    /// that midnight (<see cref="SyncFile.CoversFrom"/>), or for all a receiver keeps when the device has no older
    /// session. With Activity history off (<see cref="SyncDeviceCard.HistoryOff"/>) it carries no sessions or drift.
    /// </summary>
    /// <param name="card">The device's card; the file is written at <paramref name="now"/>, whatever its time says.</param>
    /// <param name="records">Newest first (<see cref="SessionHistoryBuilder.Build"/>), covering the 14 days and the day before them.</param>
    /// <param name="drift">Drift inside counted sessions in those days (omitted and scheduled time already left out).</param>
    /// <param name="sessionRunning">True while a session runs: the newest recorded session without an end is it.</param>
    /// <param name="oldestSessionStart">
    /// The start of the oldest session record the device still keeps, of any kind; null when it keeps none. When it isn't
    /// older than the 14 days (after Delete in Setup, or on a new device), the file holds every session the device has
    /// and speaks for all a receiver keeps (<see cref="SyncMerge.KeptFor"/>), so receivers drop what was deleted.
    /// </param>
    /// <param name="tasksOf">A session's guided checklist by its start (ADR 24), or null for none.</param>
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
        TimeZoneInfo zone,
        DateTimeOffset? oldestSessionStart,
        Func<DateTimeOffset, IReadOnlyList<SessionTask>?>? tasksOf = null)
    {
        SyncDeviceCard written = card with { WrittenAt = now };
        if (card.HistoryOff)
        {
            return new SyncFile(SyncFile.CurrentFormatVersion, written, [], [], dayPlan, documents);
        }

        DateTimeOffset from = StartOfDay(now - SyncMerge.FileSpan, zone);
        DateTimeOffset coversFrom = oldestSessionStart is DateTimeOffset oldest && oldest < from ? from : now - SyncMerge.KeptFor;
        List<SyncSession> sessions = FocusDayBuilder.CountedRecords(records, isOmitted, sessionRunning, now)
            .Where(counted => counted.End > from)
            .OrderBy(counted => counted.Record.StartedAt)
            .SelectMany(counted =>
            {
                bool added = counted.Record.Mode == SessionMode.Added;
                string? profile = added ? null : profileName(counted.Record.ProfileId);
                IReadOnlyList<SessionTask>? tasks = tasksOf?.Invoke(counted.Record.StartedAt);
                if (counted.Running || counted.Record.Away is not { Count: > 0 } away)
                {
                    return [new SyncSession(counted.Record.StartedAt, counted.Running ? null : counted.End, profile, added, tasks)];
                }

                // An ended session goes in the pieces the user was there for: time stepped away or marked
                // distracted in History comes out on the other devices too. Its checklist goes with the first piece.
                return SessionGaps.Pieces(counted.Record.StartedAt, counted.End, away)
                    .Select((piece, index) => new SyncSession(piece.Start, piece.End, profile, added, index == 0 ? tasks : null));
            })
            .Where(session => session.End is not DateTimeOffset end || end > from)
            .ToList();
        List<SyncDrift> marks = DriftMarks.Merge(drift, FocusDayBuilder.DriftGap)
            .Where(mark => mark.End > from)
            .Select(mark => new SyncDrift(mark.Start, mark.End, mark.ProcessName, mark.Domain, mark.Section))
            .ToList();
        return new SyncFile(SyncFile.CurrentFormatVersion, written, sessions, marks, dayPlan, documents, coversFrom);
    }

    /// <summary>
    /// Today's part of <paramref name="file"/> for QR codes: the sessions that reach into the local day it was written
    /// on, and their drift. It speaks from the end of the newest session it leaves out (as far back as the file when it
    /// leaves none out), so receivers keep the earlier days. When the sealed file needs more than
    /// <paramref name="maxCodes"/> codes of <see cref="SyncQr.BytesPerCode"/> bytes, the oldest drift goes first, then
    /// the settings (the biggest first), then the plan, and last the oldest sessions (the newest one always stays).
    /// </summary>
    /// <param name="zone">The device's time zone, for "today".</param>
    public static SyncFile ForQr(SyncFile file, TimeZoneInfo zone, int maxCodes = SyncQr.MaxCodes)
    {
        int maxBytes = maxCodes * SyncQr.BytesPerCode;
        // Calendar events stay out of QR codes (the phone reads its calendars from Drive or the link instead).
        file = file with { Events = null };
        DateTimeOffset writtenAt = file.Device.WrittenAt;
        DateTimeOffset todayStart = StartOfDay(writtenAt, zone);
        List<SyncSession> sessions = file.Sessions
            .Where(session => EndOf(session, writtenAt) > todayStart)
            .OrderBy(session => session.Start)
            .ToList();
        DateTimeOffset coversFrom = Latest(SyncMerge.CoveredFrom(file), file.Sessions
            .Where(session => EndOf(session, writtenAt) <= todayStart)
            .Select(session => EndOf(session, writtenAt)));

        // The drift in what the file speaks for, read as SyncMerge reads it (from the first session when it starts earlier).
        DateTimeOffset driftFrom = sessions.Count > 0 && sessions[0].Start < coversFrom ? sessions[0].Start : coversFrom;
        SyncFile today = file with
        {
            Sessions = sessions,
            Drift = file.Drift.Where(mark => mark.End > driftFrom).OrderBy(mark => mark.Start).ToList(),
            CoversFrom = coversFrom,
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
            count => Fits(WithNewestSessions(today, count), maxBytes));
        return WithNewestSessions(today, sessionsKept);
    }

    /// <summary>
    /// <paramref name="today"/> with only its newest <paramref name="count"/> sessions: it then speaks from the end of the
    /// newest one it leaves out.
    /// </summary>
    private static SyncFile WithNewestSessions(SyncFile today, int count)
    {
        IEnumerable<DateTimeOffset> leftOutEnds = today.Sessions
            .Take(today.Sessions.Count - count)
            .Select(session => EndOf(session, today.Device.WrittenAt));
        return today with { Sessions = Newest(today.Sessions, count), CoversFrom = Latest(SyncMerge.CoveredFrom(today), leftOutEnds) };
    }

    /// <summary>A session's end; the running one ends when the file was written.</summary>
    private static DateTimeOffset EndOf(SyncSession session, DateTimeOffset writtenAt)
    {
        return session.End ?? writtenAt;
    }

    /// <returns>The latest of <paramref name="time"/> and <paramref name="others"/>.</returns>
    private static DateTimeOffset Latest(DateTimeOffset time, IEnumerable<DateTimeOffset> others)
    {
        return others.Aggregate(time, (latest, other) => other > latest ? other : latest);
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
