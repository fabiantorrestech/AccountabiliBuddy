namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Keeps this device's copy of another device's part (ADR 22). A file speaks for every session of its sender that ends
/// after <see cref="CoveredFrom"/> (a full file for its 14 local days, a QR file from the end of the newest session it
/// leaves out, and either one for all a receiver keeps when the sender has nothing older, as after Delete in Setup): the
/// copy keeps what ended before that and takes the file's sessions and drift for the rest. So the same day arriving
/// twice, by any route, leaves one copy of each session, and an edit, an omission or a deletion on the sender reaches
/// the copy. Only files the sender sealed are merged: a merged copy has gaps where its files didn't meet, so it is never
/// relayed (a carrier passes on the sender's own sealed bytes). Received history is kept for 90 days, like the device's
/// own (ADR 8), and a sender with Activity history off has none (ADR 15).
/// </summary>
public static class SyncMerge
{
    /// <summary>How far back a file that doesn't say what it covers, and has no sessions, speaks for.</summary>
    public static readonly TimeSpan FileSpan = TimeSpan.FromDays(14);

    /// <summary>How long received sessions and drift are kept.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(90);

    /// <param name="stored">This device's copy of the sender's part; null for the first file from it.</param>
    /// <param name="incoming">A file the sender sealed, by any method (also when another device carries it).</param>
    /// <returns>
    /// The new copy, with the incoming file's card and <see cref="SyncFile.CoversFrom"/>, its plan (else the stored one)
    /// and its settings (each one it leaves out kept from the copy); <paramref name="stored"/> itself when the incoming
    /// file was written before it.
    /// </returns>
    /// <exception cref="ArgumentException">The stored copy is another device's.</exception>
    public static SyncFile Merge(SyncFile? stored, SyncFile incoming, DateTimeOffset now)
    {
        if (stored is not null && stored.Device.DeviceId != incoming.Device.DeviceId)
        {
            throw new ArgumentException("The stored copy is another device's.", nameof(stored));
        }

        if (stored is not null && incoming.Device.WrittenAt < stored.Device.WrittenAt)
        {
            return stored;
        }

        var sessions = new List<SyncSession>();
        var drift = new List<SyncDrift>();
        if (!incoming.Device.HistoryOff)
        {
            // The file holds its sessions whole: a stored copy of one with an earlier end (it was running then) goes too.
            DateTimeOffset sessionsFrom = CoveredFrom(incoming);
            if (incoming.Sessions.Count > 0 && incoming.Sessions.Min(session => session.Start) < sessionsFrom)
            {
                sessionsFrom = incoming.Sessions.Min(session => session.Start);
            }

            // A drift mark joined across a session's start can begin before the first session: the marks it covers go.
            DateTimeOffset driftFrom = incoming.Drift.Count > 0 && incoming.Drift.Min(mark => mark.Start) < sessionsFrom
                ? incoming.Drift.Min(mark => mark.Start)
                : sessionsFrom;
            if (stored is not null)
            {
                // A session that was running when the copy was written is taken as ending then, as it was shown.
                sessions.AddRange(stored.Sessions
                    .Select(session => session.End is null ? session with { End = stored.Device.WrittenAt } : session)
                    .Where(session => session.End <= sessionsFrom));
                drift.AddRange(stored.Drift.Where(mark => mark.End <= driftFrom));
            }

            sessions.AddRange(incoming.Sessions);
            drift.AddRange(incoming.Drift);
        }

        DateTimeOffset keptFrom = now - KeptFor;
        return incoming with
        {
            Sessions = sessions
                .Where(session => (session.End ?? incoming.Device.WrittenAt) >= keptFrom)
                .OrderBy(session => session.Start)
                .ToList(),
            Drift = drift.Where(mark => mark.End >= keptFrom).OrderBy(mark => mark.Start).ToList(),
            DayPlan = incoming.DayPlan ?? stored?.DayPlan,
            Documents = MergeDocuments(stored?.Documents ?? [], incoming.Documents),
        };
    }

    /// <summary>
    /// The time <paramref name="file"/> speaks from: its <see cref="SyncFile.CoversFrom"/>; for a file that doesn't say,
    /// its earliest session, else <see cref="FileSpan"/> before it was written.
    /// </summary>
    public static DateTimeOffset CoveredFrom(SyncFile file)
    {
        if (file.CoversFrom is DateTimeOffset coversFrom)
        {
            return coversFrom;
        }

        return file.Sessions.Count > 0 ? file.Sessions.Min(session => session.Start) : file.Device.WrittenAt - FileSpan;
    }

    /// <summary>The incoming settings, plus each stored one it leaves out (a QR file can drop them to fit its codes).</summary>
    private static List<SyncDocument> MergeDocuments(IReadOnlyList<SyncDocument> stored, IReadOnlyList<SyncDocument> incoming)
    {
        var documents = incoming.ToList();
        documents.AddRange(stored.Where(document => incoming.All(sent => sent.Key != document.Key)));
        return documents;
    }
}
