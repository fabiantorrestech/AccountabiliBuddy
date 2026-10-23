using System.Globalization;
using AccountabiliBuddy.Core.Backup;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.Core.Storage;

/// <summary>
/// The activity log in the local database (tables created by migration v2, ADR 8).
/// Readable columns: times, monitor, app (process name), site (domain), the site's section (ADR 17: subreddit, YouTube
/// channel, GitHub repository), classification, profile, session context.
/// Encrypted with <see cref="IDataProtector"/>: window titles, full addresses, screen text, early-end reasons and
/// classify-prompt reasons.
/// Thread-safe: every call opens its own connection.
/// </summary>
public sealed class SqliteActivityLog : IActivityLog
{
    // History shows the top 5 and can expand to the rest; this only caps a pathological day.
    private const int TopEntries = 200;
    private const string ProtectedSuffix = "_protected";

    private static readonly string[] HistoryTables = { "activity_segments", "activity_labels", "session_events" };

    // Columns that identify the same entry; merging a backup skips rows that match a stored row on all of them.
    private static readonly IReadOnlyDictionary<string, string[]> DuplicateKeys = new Dictionary<string, string[]>
    {
        ["activity_segments"] = new[] { "started_at", "monitor_id", "process_name" },
        ["activity_labels"] = new[] { "created_at", "process_name", "verdict" },
        ["session_events"] = new[] { "at", "kind" },
    };

    private readonly LocalDatabase _database;
    private readonly IDataProtector _protector;

    public SqliteActivityLog(LocalDatabase database, IDataProtector protector)
    {
        _database = database;
        _protector = protector;
    }

    public event EventHandler? AllDeleted;

    public async Task RecordSegmentAsync(ActivitySegment segment, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO activity_segments
                (started_at, ended_at, duration_ms, monitor_id, process_name, domain, section, classification, profile_id,
                 in_focus, title_protected, url_protected, ocr_protected)
            VALUES
                ($start, $end, $duration, $monitor, $process, $domain, $section, $classification, $profile,
                 $focus, $title, $url, $ocr);
            """;
        command.Parameters.AddWithValue("$start", Format(segment.Start));
        command.Parameters.AddWithValue("$end", Format(segment.End));
        command.Parameters.AddWithValue("$duration", (long)segment.Duration.TotalMilliseconds);
        command.Parameters.AddWithValue("$monitor", segment.MonitorId);
        command.Parameters.AddWithValue("$process", segment.ProcessName);
        command.Parameters.AddWithValue("$domain", DbValue(DomainOf(segment.Url)));
        command.Parameters.AddWithValue("$section", DbValue(SiteSections.SectionOf(segment.Url, segment.Channel)));
        command.Parameters.AddWithValue("$classification", segment.Classification.ToString());
        command.Parameters.AddWithValue("$profile", DbValue(segment.ProfileId?.ToString()));
        command.Parameters.AddWithValue("$focus", segment.InFocusSession ? 1 : 0);
        // "Titles and addresses not kept" (the Work computer type): only the site and its section (above) are stored.
        command.Parameters.AddWithValue("$title", segment.KeepProtectedText ? ProtectOrNull(segment.WindowTitle) : DBNull.Value);
        command.Parameters.AddWithValue("$url", segment.KeepProtectedText ? ProtectOrNull(segment.Url) : DBNull.Value);
        command.Parameters.AddWithValue("$ocr", segment.KeepProtectedText ? ProtectOrNull(segment.OcrText) : DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordLabelAsync(ActivityLabel label, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO activity_labels
                (created_at, profile_id, source, verdict, lifetime, process_name, domain,
                 title_protected, url_protected, ocr_protected, reason_protected)
            VALUES
                ($at, $profile, $source, $verdict, $lifetime, $process, $domain, $title, $url, $ocr, $reason);
            """;
        command.Parameters.AddWithValue("$at", Format(label.Timestamp));
        command.Parameters.AddWithValue("$profile", label.ProfileId.ToString());
        command.Parameters.AddWithValue("$source", label.Source.ToString());
        command.Parameters.AddWithValue("$verdict", label.Verdict.ToString());
        command.Parameters.AddWithValue("$lifetime", label.Lifetime.ToString());
        command.Parameters.AddWithValue("$process", label.ProcessName);
        command.Parameters.AddWithValue("$domain", DbValue(DomainOf(label.Url)));
        command.Parameters.AddWithValue("$title", label.KeepProtectedText ? ProtectOrNull(label.WindowTitle) : DBNull.Value);
        command.Parameters.AddWithValue("$url", label.KeepProtectedText ? ProtectOrNull(label.Url) : DBNull.Value);
        command.Parameters.AddWithValue("$ocr", label.KeepProtectedText ? ProtectOrNull(label.OcrText) : DBNull.Value);
        command.Parameters.AddWithValue("$reason", ProtectOrNull(label.Reason));
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
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<TimeRange>? leaveOut = null,
        IReadOnlyList<UsageDeletion>? deleted = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TimeRange> skipped = leaveOut ?? Array.Empty<TimeRange>();
        IReadOnlyList<UsageDeletion> removed = deleted ?? Array.Empty<UsageDeletion>();
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);

        TimeSpan focus = TimeSpan.Zero, onTrack = TimeSpan.Zero, distracted = TimeSpan.Zero, unknown = TimeSpan.Zero;
        await using (SqliteCommand totals = connection.CreateCommand())
        {
            totals.CommandText = $"""
                SELECT classification, SUM(duration_ms)
                FROM activity_segments
                WHERE in_focus = 1 AND started_at >= $from AND started_at < $to{LeaveOutFilter(totals, skipped)}{DeletedFilter(totals, removed)}
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
            connection, from, to, "in_focus = 1 AND classification = 'Distracting'", skipped, removed, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<AppSiteUsage> appsAndSites = await AppsAndSitesAsync(connection, from, to, removed, cancellationToken).ConfigureAwait(false);

        return new UsageSummary(focus, onTrack, distracted, unknown, topDistractions, appsAndSites);
    }

    /// <summary>
    /// Time with no rule (Unknown) inside focus sessions in a period, one entry per site (any section) or, without a site,
    /// per app; longest first. For the end-of-session review (redesign phase 2).
    /// </summary>
    public async Task<IReadOnlyList<AppSiteUsage>> GetUnknownUsageAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT MIN(process_name), domain, SUM(duration_ms) AS total
            FROM activity_segments
            WHERE in_focus = 1 AND classification = 'Unknown' AND started_at >= $from AND started_at < $to
            GROUP BY domain, CASE WHEN domain IS NULL THEN process_name END
            ORDER BY total DESC;
            """;
        AddRange(command, from, to);

        var usage = new List<AppSiteUsage>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            usage.Add(new AppSiteUsage(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                TimeSpan.FromMilliseconds(reader.GetInt64(2))));
        }

        return usage;
    }

    public async Task<IReadOnlyList<DriftStretch>> GetDriftAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<TimeRange>? leaveOut = null,
        IReadOnlyList<UsageDeletion>? deleted = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TimeRange> skipped = leaveOut ?? Array.Empty<TimeRange>();
        IReadOnlyList<UsageDeletion> removed = deleted ?? Array.Empty<UsageDeletion>();
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT started_at, ended_at, process_name, domain, section
            FROM activity_segments
            WHERE in_focus = 1 AND classification = $distracting AND started_at >= $from AND started_at < $to{LeaveOutFilter(command, skipped)}{DeletedFilter(command, removed)}
            ORDER BY started_at, id;
            """;
        AddRange(command, from, to);
        command.Parameters.AddWithValue("$distracting", nameof(Classification.Distracting));

        var drift = new List<DriftStretch>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            drift.Add(new DriftStretch(
                Parse(reader.GetString(0)),
                Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return drift;
    }

    public async Task AddSessionAsync(DateTimeOffset start, DateTimeOffset end, string? note, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await InsertAddedAsync(connection, transaction, start, end, note, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReplaceAddedSessionAsync(
        DateTimeOffset oldStart, DateTimeOffset start, DateTimeOffset end, string? note, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await DeleteAddedAsync(connection, transaction, oldStart, cancellationToken).ConfigureAwait(false);
        await InsertAddedAsync(connection, transaction, start, end, note, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAddedSessionAsync(DateTimeOffset start, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await DeleteAddedAsync(connection, transaction, start, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertAddedAsync(
        SqliteConnection connection, SqliteTransaction transaction, DateTimeOffset start, DateTimeOffset end, string? note, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO session_events (at, kind, mode, profile_id, planned_end, reason_protected)
            VALUES ($start, 'Started', 'Added', NULL, $end, NULL), ($end, 'Completed', 'Added', NULL, NULL, $note);
            """;
        command.Parameters.AddWithValue("$start", Format(start));
        command.Parameters.AddWithValue("$end", Format(end));
        command.Parameters.AddWithValue("$note", ProtectOrNull(note));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The added session's two events: its start, and the first added end after it. Only mode Added rows.</summary>
    private static async Task DeleteAddedAsync(SqliteConnection connection, SqliteTransaction transaction, DateTimeOffset start, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM session_events
            WHERE mode = 'Added' AND (
                (kind = 'Started' AND at = $start)
                OR (kind <> 'Started' AND at = (SELECT MIN(at) FROM session_events WHERE mode = 'Added' AND kind <> 'Started' AND at > $start)));
            """;
        command.Parameters.AddWithValue("$start", Format(start));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
            // A kind from a newer app version (e.g. restored from its backup) is skipped instead of failing every read.
            if (!Enum.TryParse(reader.GetString(1), out SessionEventKind kind))
            {
                continue;
            }

            events.Add(new SessionEvent(
                Parse(reader.GetString(0)),
                kind,
                // A mode from a newer app version reads as unknown instead of failing (ADR 20).
                reader.IsDBNull(2) || !Enum.TryParse(reader.GetString(2), out SessionMode mode) ? null : mode,
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

        try
        {
            // Rewrites the file so deleted rows do not linger in free pages.
            await using SqliteCommand vacuum = connection.CreateCommand();
            vacuum.CommandText = "VACUUM;";
            await vacuum.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // The entries are gone even when compacting fails.
            AllDeleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Every history row, for a backup. With <paramref name="includeProtected"/> the encrypted columns are
    /// decrypted into the export; otherwise they are left out.
    /// </summary>
    public async Task<ActivityHistoryExport> ExportHistoryAsync(bool includeProtected, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var tables = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>(StringComparer.Ordinal);
        foreach (string table in HistoryTables)
        {
            var rows = new List<IReadOnlyDictionary<string, string?>>();
            await using SqliteCommand command = connection.CreateCommand();
            // Table names are constants, never input.
            command.CommandText = $"SELECT * FROM {table} ORDER BY id;";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var row = new Dictionary<string, string?>(StringComparer.Ordinal);
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    string column = reader.GetName(i);
                    if (column == "id")
                    {
                        continue;
                    }

                    if (column.EndsWith(ProtectedSuffix, StringComparison.Ordinal))
                    {
                        if (includeProtected && !reader.IsDBNull(i))
                        {
                            row[column] = _protector.Unprotect((byte[])reader.GetValue(i));
                        }

                        continue;
                    }

                    row[column] = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                }

                rows.Add(row);
            }

            tables[table] = rows;
        }

        return new ActivityHistoryExport(tables);
    }

    /// <summary>
    /// Restores history rows from a backup in one transaction. <paramref name="replace"/> deletes the current history
    /// first; otherwise rows already stored (same time, app and kind) are skipped. Columns this version doesn't have
    /// are ignored, and encrypted columns are encrypted again for this computer.
    /// </summary>
    /// <returns>The number of rows added.</returns>
    public async Task<int> ImportHistoryAsync(ActivityHistoryExport history, bool replace, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (replace)
        {
            await using SqliteCommand delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM activity_segments; DELETE FROM activity_labels; DELETE FROM session_events;";
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        int added = 0;
        foreach (string table in HistoryTables)
        {
            if (!history.Tables.TryGetValue(table, out IReadOnlyList<IReadOnlyDictionary<string, string?>>? rows))
            {
                continue;
            }

            HashSet<string> columns = await ColumnsOfAsync(connection, transaction, table, cancellationToken).ConfigureAwait(false);
            foreach (IReadOnlyDictionary<string, string?> row in rows)
            {
                // Only column names the table really has are used, so the file can't inject SQL.
                List<string> used = row.Keys.Where(column => column != "id" && columns.Contains(column)).ToList();
                if (used.Count == 0)
                {
                    continue;
                }

                string[] keys = replace ? Array.Empty<string>() : DuplicateKeys[table].Where(used.Contains).ToArray();
                string skipDuplicates = keys.Length == 0
                    ? string.Empty
                    : $" WHERE NOT EXISTS (SELECT 1 FROM {table} WHERE {string.Join(" AND ", keys.Select(key => $"{key} IS $p{used.IndexOf(key)}"))})";

                await using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = $"INSERT INTO {table} ({string.Join(", ", used)}) SELECT {string.Join(", ", used.Select((_, i) => $"$p{i}"))}{skipDuplicates};";
                for (int i = 0; i < used.Count; i++)
                {
                    string? value = row[used[i]];
                    object parameter = value is null ? DBNull.Value
                        : used[i].EndsWith(ProtectedSuffix, StringComparison.Ordinal) ? _protector.Protect(value)
                        : value;
                    insert.Parameters.AddWithValue($"$p{i}", parameter);
                }

                added += await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        // A backup from before sections were kept (ADR 17) brings rows without them.
        await FillMissingSectionsAsync(cancellationToken).ConfigureAwait(false);
        return added;
    }

    /// <summary>
    /// Gives entries recorded before sections were kept (ADR 17) their subreddit or repository, read from their
    /// encrypted address. YouTube channels weren't recorded, so those stay without. Entries that can't be decrypted on
    /// this computer are skipped.
    /// </summary>
    /// <returns>The number of entries that got a section.</returns>
    public async Task<int> FillMissingSectionsAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var found = new List<(long Id, string Section)>();
        await using (SqliteCommand select = connection.CreateCommand())
        {
            select.CommandText = """
                SELECT id, url_protected
                FROM activity_segments
                WHERE section IS NULL AND url_protected IS NOT NULL
                  AND (domain = 'reddit.com' OR domain LIKE '%.reddit.com' OR domain = 'github.com');
                """;
            await using SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string? url;
                try
                {
                    url = _protector.Unprotect((byte[])reader.GetValue(1));
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    continue;
                }

                if (SiteSections.SectionOf(url, channel: null) is string section)
                {
                    found.Add((reader.GetInt64(0), section));
                }
            }
        }

        if (found.Count == 0)
        {
            return 0;
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE activity_segments SET section = $section WHERE id = $id;";
            SqliteParameter sectionParameter = update.Parameters.Add("$section", SqliteType.Text);
            SqliteParameter idParameter = update.Parameters.Add("$id", SqliteType.Integer);
            foreach ((long id, string section) in found)
            {
                sectionParameter.Value = section;
                idParameter.Value = id;
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return found.Count;
    }

    private static async Task<HashSet<string>> ColumnsOfAsync(
        SqliteConnection connection, SqliteTransaction transaction, string table, CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.Ordinal);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({table});";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static async Task<IReadOnlyList<UsageEntry>> TopUsageAsync(
        SqliteConnection connection, DateTimeOffset from, DateTimeOffset to, string filter, IReadOnlyList<TimeRange> leaveOut,
        IReadOnlyList<UsageDeletion> deleted, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        // The filter is one of two constant strings above, never user input.
        command.CommandText = $"""
            SELECT COALESCE(domain, process_name) AS name, section, SUM(duration_ms) AS total
            FROM activity_segments
            WHERE {filter} AND started_at >= $from AND started_at < $to{LeaveOutFilter(command, leaveOut)}{DeletedFilter(command, deleted)}
            GROUP BY name, section
            ORDER BY total DESC
            LIMIT {TopEntries};
            """;
        AddRange(command, from, to);

        var entries = new List<UsageEntry>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new UsageEntry(
                reader.GetString(0),
                TimeSpan.FromMilliseconds(reader.GetInt64(2)),
                reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return entries;
    }

    private static async Task<IReadOnlyList<AppSiteUsage>> AppsAndSitesAsync(
        SqliteConnection connection, DateTimeOffset from, DateTimeOffset to, IReadOnlyList<UsageDeletion> deleted, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT process_name, domain, section, SUM(duration_ms) AS total
            FROM activity_segments
            WHERE started_at >= $from AND started_at < $to{DeletedFilter(command, deleted)}
            GROUP BY process_name, domain, section
            ORDER BY total DESC;
            """;
        AddRange(command, from, to);

        var usage = new List<AppSiteUsage>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            usage.Add(new AppSiteUsage(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                TimeSpan.FromMilliseconds(reader.GetInt64(3)),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return usage;
    }

    /// <summary>" AND NOT (…)" for each stretch to leave out, with its times added as parameters.</summary>
    private static string LeaveOutFilter(SqliteCommand command, IReadOnlyList<TimeRange> leaveOut)
    {
        var filter = new System.Text.StringBuilder();
        for (int i = 0; i < leaveOut.Count; i++)
        {
            filter.Append($" AND NOT (started_at >= $skipFrom{i} AND started_at < $skipTo{i})");
            command.Parameters.AddWithValue($"$skipFrom{i}", Format(leaveOut[i].From));
            command.Parameters.AddWithValue($"$skipTo{i}", Format(leaveOut[i].To));
        }

        return filter.ToString();
    }

    /// <summary>
    /// " AND NOT (…)" for each deletion (app, site in an app, or name as shown; optionally one section of the site), with
    /// its values as parameters.
    /// </summary>
    private static string DeletedFilter(SqliteCommand command, IReadOnlyList<UsageDeletion> deleted)
    {
        var filter = new System.Text.StringBuilder();
        for (int i = 0; i < deleted.Count; i++)
        {
            UsageDeletion deletion = deleted[i];
            string match = (deletion.App, deletion.Site) switch
            {
                (not null, null) => $"lower(process_name) = lower($delApp{i})",
                (not null, not null) => $"lower(process_name) = lower($delApp{i}) AND COALESCE(lower(domain), '') = lower($delSite{i})",
                _ => $"lower(COALESCE(domain, process_name)) = lower($delSite{i})",
            };
            // COALESCE: a NULL comparison would make NOT (…) NULL and drop the site's time outside any section too.
            string section = deletion.Section is null ? string.Empty : $" AND COALESCE(lower(section), '') = lower($delSection{i})";
            filter.Append($" AND NOT ({match}{section} AND started_at >= $delFrom{i} AND started_at < $delTo{i})");
            command.Parameters.AddWithValue($"$delSection{i}", (object?)deletion.Section ?? DBNull.Value);
            command.Parameters.AddWithValue($"$delApp{i}", (object?)deletion.App ?? DBNull.Value);
            command.Parameters.AddWithValue($"$delSite{i}", (object?)deletion.Site ?? DBNull.Value);
            command.Parameters.AddWithValue($"$delFrom{i}", Format(deletion.From));
            command.Parameters.AddWithValue($"$delTo{i}", Format(deletion.To));
        }

        return filter.ToString();
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
