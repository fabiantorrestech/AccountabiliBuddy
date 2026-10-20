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
    /// <param name="leaveOut">Stretches whose focus time doesn't count (sessions marked as not real).</param>
    Task<UsageSummary> GetSummaryAsync(
        DateTimeOffset from, DateTimeOffset to, IReadOnlyList<TimeRange>? leaveOut = null, CancellationToken cancellationToken = default);

    /// <summary>Session events in [<paramref name="from"/>, <paramref name="to"/>), oldest first, reasons decrypted.</summary>
    Task<IReadOnlyList<SessionEvent>> GetSessionEventsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Number of stored segments, labels and session events together.</summary>
    Task<long> CountEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes everything older than <paramref name="cutoff"/> (retention).</summary>
    Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    /// <summary>"Delete my data": removes every entry and compacts the database file.</summary>
    Task DeleteAllAsync(CancellationToken cancellationToken = default);
}
