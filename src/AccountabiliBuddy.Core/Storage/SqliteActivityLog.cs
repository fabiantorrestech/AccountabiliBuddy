using System.Globalization;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.Core.Storage;

/// <summary>
/// The activity log in the local database (tables created by migration v2, ADR 8).
/// Readable columns: times, monitor, app (process name), site (domain), classification, profile, session context.
/// Encrypted with <see cref="IDataProtector"/>: window titles, full addresses, screen text and early-end reasons.
/// Thread-safe: every call opens its own connection.
/// </summary>
public sealed class SqliteActivityLog : IActivityLog
{
    private const int TopEntries = 5;

    private readonly LocalDatabase _database;
    private readonly IDataProtector _protector;

    public SqliteActivityLog(LocalDatabase database, IDataProtector protector)
    {
        _database = database;
        _protector = protector;
    }

    public async Task RecordSegmentAsync(ActivitySegment segment, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO activity_segments
                (started_at, ended_at, duration_ms, monitor_id, process_name, domain, classification, profile_id,
                 in_focus, title_protected, url_protected, ocr_protected)
            VALUES
                ($start, $end, $duration, $monitor, $process, $domain, $classification, $profile,
                 $focus, $title, $url, $ocr);
            """;
        command.Parameters.AddWithValue("$start", Format(segment.Start));
        command.Parameters.AddWithValue("$end", Format(segment.End));
        command.Parameters.AddWithValue("$duration", (long)segment.Duration.TotalMilliseconds);
        command.Parameters.AddWithValue("$monitor", segment.MonitorId);
        command.Parameters.AddWithValue("$process", segment.ProcessName);
        command.Parameters.AddWithValue("$domain", DbValue(DomainOf(segment.Url)));
        command.Parameters.AddWithValue("$classification", segment.Classification.ToString());
        command.Parameters.AddWithValue("$profile", DbValue(segment.ProfileId?.ToString()));
        command.Parameters.AddWithValue("$focus", segment.InFocusSession ? 1 : 0);
        command.Parameters.AddWithValue("$title", ProtectOrNull(segment.WindowTitle));
        command.Parameters.AddWithValue("$url", ProtectOrNull(segment.Url));
        command.Parameters.AddWithValue("$ocr", ProtectOrNull(segment.OcrText));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordLabelAsync(ActivityLabel label, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO activity_labels
                (created_at, profile_id, source, verdict, process_name, domain, title_protected, url_protected, ocr_protected)
            VALUES
                ($at, $profile, $source, $verdict, $process, $domain, $title, $url, $ocr);
            """;
        command.Parameters.AddWithValue("$at", Format(label.Timestamp));
        command.Parameters.AddWithValue("$profile", label.ProfileId.ToString());
        command.Parameters.AddWithValue("$source", label.Source.ToString());
        command.Parameters.AddWithValue("$verdict", label.Verdict.ToString());
        command.Parameters.AddWithValue("$process", label.ProcessName);
        command.Parameters.AddWithValue("$domain", DbValue(DomainOf(label.Url)));
        command.Parameters.AddWithValue("$title", ProtectOrNull(label.WindowTitle));
        command.Parameters.AddWithValue("$url", ProtectOrNull(label.Url));
        command.Parameters.AddWithValue("$ocr", ProtectOrNull(label.OcrText));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordSessionEventAsync(SessionEvent sessionEvent, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO session_events (at, kind, mode, profile_id, planned_end, reason_protected)
            VALUES ($at, $kind, $mode, $profile, $plannedEnd, $reason);
            """;
        command.Parameters.AddWithValue("$at", Format(sessionEvent.At));
        command.Parameters.AddWithValue("$kind", sessionEvent.Kind.ToString());
        command.Parameters.AddWithValue("$mode", DbValue(sessionEvent.Mode?.ToString()));
        command.Parameters.AddWithValue("$profile", DbValue(sessionEvent.ProfileId?.ToString()));
        command.Parameters.AddWithValue("$plannedEnd", DbValue(sessionEvent.PlannedEnd is { } end ? Format(end) : null));
        command.Parameters.AddWithValue("$reason", ProtectOrNull(sessionEvent.Reason));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<UsageSummary> GetSummaryAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        TimeSpan focus = TimeSpan.Zero, onTrack = TimeSpan.Zero, distracted = TimeSpan.Zero, unknown = TimeSpan.Zero;
        await using (SqliteCommand totals = connection.CreateCommand())
        {
            totals.CommandText = """
                SELECT classification, SUM(duration_ms)
                FROM activity_segments
                WHERE in_focus = 1 AND started_at >= $from AND started_at < $to
                GROUP BY classification;
                """;
            AddRange(totals, from, to);
            await using SqliteDataReader reader = await totals.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                TimeSpan time = TimeSpan.FromMilliseconds(reader.GetInt64(1));
                focus += time;
                switch (reader.GetString(0))
                {
                    case nameof(Classification.OnTrack):
                        onTrack += time;
                        break;
                    case nameof(Classification.Distracting):
                        distracted += time;
                        break;
                    default:
                        unknown += time;
                        break;
                }
            }
        }

        IReadOnlyList<UsageEntry> topDistractions = await TopUsageAsync(
            connection, from, to, "in_focus = 1 AND classification = 'Distracting'", cancellationToken).ConfigureAwait(false);
        IReadOnlyList<UsageEntry> topOverall = await TopUsageAsync(
            connection, from, to, "1 = 1", cancellationToken).ConfigureAwait(false);

        return new UsageSummary(focus, onTrack, distracted, unknown, topDistractions, topOverall);
    }

    public async Task<IReadOnlyList<SessionEvent>> GetSessionEventsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT at, kind, mode, profile_id, planned_end, reason_protected
            FROM session_events
            WHERE at >= $from AND at < $to
            ORDER BY at, id;
            """;
        AddRange(command, from, to);

        var events = new List<SessionEvent>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            events.Add(new SessionEvent(
                Parse(reader.GetString(0)),
                Enum.Parse<SessionEventKind>(reader.GetString(1)),
                reader.IsDBNull(2) ? null : Enum.Parse<SessionMode>(reader.GetString(2)),
                reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)),
                reader.IsDBNull(4) ? null : Parse(reader.GetString(4)),
                reader.IsDBNull(5) ? null : _protector.Unprotect((byte[])reader.GetValue(5))));
        }

        return events;
    }

    public async Task<long> CountEntriesAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT (SELECT COUNT(*) FROM activity_segments)
                 + (SELECT COUNT(*) FROM activity_labels)
                 + (SELECT COUNT(*) FROM session_events);
            """;
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long count ? count : 0;
    }

    public async Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM activity_segments WHERE started_at < $cutoff;
            DELETE FROM activity_labels WHERE created_at < $cutoff;
            DELETE FROM session_events WHERE at < $cutoff;
            """;
        command.Parameters.AddWithValue("$cutoff", Format(cutoff));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (SqliteCommand delete = connection.CreateCommand())
        {
            delete.CommandText = """
                DELETE FROM activity_segments;
                DELETE FROM activity_labels;
                DELETE FROM session_events;
                """;
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Rewrites the file so deleted rows do not linger in free pages.
        await using SqliteCommand vacuum = connection.CreateCommand();
        vacuum.CommandText = "VACUUM;";
        await vacuum.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<UsageEntry>> TopUsageAsync(
        SqliteConnection connection, DateTimeOffset from, DateTimeOffset to, string filter, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        // The filter is one of two constant strings above, never user input.
        command.CommandText = $"""
            SELECT COALESCE(domain, process_name) AS name, SUM(duration_ms) AS total
            FROM activity_segments
            WHERE {filter} AND started_at >= $from AND started_at < $to
            GROUP BY name
            ORDER BY total DESC
            LIMIT {TopEntries};
            """;
        AddRange(command, from, to);

        var entries = new List<UsageEntry>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new UsageEntry(reader.GetString(0), TimeSpan.FromMilliseconds(reader.GetInt64(1))));
        }

        return entries;
    }

    private static void AddRange(SqliteCommand command, DateTimeOffset from, DateTimeOffset to)
    {
        command.Parameters.AddWithValue("$from", Format(from));
        command.Parameters.AddWithValue("$to", Format(to));
    }

    private object ProtectOrNull(string? text)
    {
        return string.IsNullOrEmpty(text) ? DBNull.Value : _protector.Protect(text);
    }

    private static object DbValue(string? value)
    {
        return value is null ? DBNull.Value : value;
    }

    private static string? DomainOf(string? url)
    {
        return url is null ? null : UrlNormalizer.GetHost(url);
    }

    // Always UTC in round-trip format, so stored times sort correctly as text.
    private static string Format(DateTimeOffset time)
    {
        return time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset Parse(string text)
    {
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
