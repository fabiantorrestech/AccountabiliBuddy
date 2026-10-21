using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Local usage log and label store (ADR 6, ADR 8). Feeds the History tab now and V2 suggestions and classifier
/// training later. Implementations keep only aggregate-friendly fields (app, site, classification, times) readable;
/// titles, addresses, screen text and reasons are stored encrypted.
/// </summary>
public interface IActivityLog
{
    Task RecordSegmentAsync(ActivitySegment segment, CancellationToken cancellationToken = default);

    Task RecordLabelAsync(ActivityLabel label, CancellationToken cancellationToken = default);

    Task RecordSessionEventAsync(SessionEvent sessionEvent, CancellationToken cancellationToken = default);

    /// <summary>Totals for segments that started in [<paramref name="from"/>, <paramref name="to"/>).</summary>
    /// <param name="leaveOut">Stretches whose focus time doesn't count (omitted sessions).</param>
    /// <param name="deleted">App or site time the user deleted from History; it counts nowhere.</param>
    Task<UsageSummary> GetSummaryAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<TimeRange>? leaveOut = null,
        IReadOnlyList<UsageDeletion>? deleted = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Distracting time inside focus sessions, for segments that started in [<paramref name="from"/>,
    /// <paramref name="to"/>), oldest first. Readable columns only.
    /// </summary>
    /// <param name="leaveOut">Stretches whose time doesn't count (omitted sessions).</param>
    /// <param name="deleted">App or site time the user deleted from History; it counts nowhere.</param>
    Task<IReadOnlyList<DriftStretch>> GetDriftAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<TimeRange>? leaveOut = null,
        IReadOnlyList<UsageDeletion>? deleted = null,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a past focus session by hand (ADR 20): a started and a completed event of mode Added; the note is encrypted.</summary>
    Task AddSessionAsync(DateTimeOffset start, DateTimeOffset end, string? note, CancellationToken cancellationToken = default);

    /// <summary>Replaces the added session that starts at <paramref name="oldStart"/>; recorded sessions are never touched.</summary>
    Task ReplaceAddedSessionAsync(DateTimeOffset oldStart, DateTimeOffset start, DateTimeOffset end, string? note, CancellationToken cancellationToken = default);

    /// <summary>Deletes the added session that starts at <paramref name="start"/>; recorded sessions are never touched.</summary>
    Task DeleteAddedSessionAsync(DateTimeOffset start, CancellationToken cancellationToken = default);

    /// <summary>Session events in [<paramref name="from"/>, <paramref name="to"/>), oldest first, reasons decrypted.</summary>
    Task<IReadOnlyList<SessionEvent>> GetSessionEventsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Number of stored segments, labels and session events together.</summary>
    Task<long> CountEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes everything older than <paramref name="cutoff"/> (retention).</summary>
    Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    /// <summary>"Delete my data": removes every entry and compacts the database file.</summary>
    Task DeleteAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised once <see cref="DeleteAllAsync"/> removed every entry, so what was built from the history is built again;
    /// on the thread that finished the deletion, which may not be the UI thread.
    /// </summary>
    event EventHandler? AllDeleted;
}
