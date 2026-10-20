using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.Core.Storage;

/// <summary>
/// The single local SQLite database (ADR 6). Creates the file and applies schema migrations on first open.
/// Schema version is tracked with SQLite's built-in <c>PRAGMA user_version</c>. Before a migration the file is copied
/// to <see cref="BackupFolder"/>; a database newer than this version is not opened (<see cref="DatabaseTooNewException"/>).
/// </summary>
public sealed class LocalDatabase
{
    public const int CurrentSchemaVersion = 4;

    private const string FileName = "accountabilibuddy.db";
    private const int KeptBackups = 10;

    private readonly string _connectionString;
    private readonly SemaphoreSlim _migrationLock = new(1, 1);
    private bool _migrated;

    public LocalDatabase(string directory)
    {
        Directory.CreateDirectory(directory);
        DatabasePath = Path.Combine(directory, FileName);
        BackupFolder = Path.Combine(directory, "backups");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString();
    }

    public string DatabasePath { get; }

    /// <summary>Copies made before migrations and updates (and before restoring a backup).</summary>
    public string BackupFolder { get; }

    /// <summary>The schema version of the file without migrating it (0 for a new database).</summary>
    public async Task<long> ReadSchemaVersionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a consistent copy of the database (pending writes included) to the backup folder, named
    /// "before-{label}-{time}.db", and keeps only the newest copies.
    /// </summary>
    /// <returns>The copy's path.</returns>
    public async Task<string> BackupAsync(string label, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await BackupAsync(connection, label, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Default location: %LOCALAPPDATA%\AccountabiliBuddy on Windows, ~/.local/share/AccountabiliBuddy on Linux.</summary>
    public static string DefaultDirectory()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "AccountabiliBuddy");
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureMigratedAsync(connection, cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private async Task EnsureMigratedAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (_migrated)
        {
            return;
        }

        await _migrationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_migrated)
            {
                return;
            }

            long version = await ReadSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (version > CurrentSchemaVersion)
            {
                throw new DatabaseTooNewException(version, CurrentSchemaVersion);
            }

            if (version > 0 && version < CurrentSchemaVersion)
            {
                await BackupAsync(connection, $"database-v{CurrentSchemaVersion}", cancellationToken).ConfigureAwait(false);
            }

            if (version < 1)
            {
                await ExecuteAsync(connection, """
                    CREATE TABLE IF NOT EXISTS documents (
                        key        TEXT PRIMARY KEY,
                        json       TEXT NOT NULL,
                        updated_at TEXT NOT NULL
                    );
                    """, cancellationToken).ConfigureAwait(false);
            }

            if (version < 2)
            {
                // M7 activity log (ADR 8). Times are UTC ISO 8601 text ("O" format), so they sort and compare as text.
                // App/site/classification stay readable for totals; *_protected columns hold DPAPI-encrypted text.
                await ExecuteAsync(connection, """
                    CREATE TABLE IF NOT EXISTS activity_segments (
                        id               INTEGER PRIMARY KEY,
                        started_at       TEXT    NOT NULL,
                        ended_at         TEXT    NOT NULL,
                        duration_ms      INTEGER NOT NULL,
                        monitor_id       TEXT    NOT NULL,
                        process_name     TEXT    NOT NULL,
                        domain           TEXT,
                        classification   TEXT    NOT NULL,
                        profile_id       TEXT,
                        in_focus         INTEGER NOT NULL,
                        title_protected  BLOB,
                        url_protected    BLOB,
                        ocr_protected    BLOB
                    );
                    CREATE INDEX IF NOT EXISTS ix_activity_segments_started_at ON activity_segments (started_at);

                    CREATE TABLE IF NOT EXISTS activity_labels (
                        id               INTEGER PRIMARY KEY,
                        created_at       TEXT    NOT NULL,
                        profile_id       TEXT    NOT NULL,
                        source           TEXT    NOT NULL,
                        verdict          TEXT    NOT NULL,
                        process_name     TEXT    NOT NULL,
                        domain           TEXT,
                        title_protected  BLOB,
                        url_protected    BLOB,
                        ocr_protected    BLOB
                    );
                    CREATE INDEX IF NOT EXISTS ix_activity_labels_created_at ON activity_labels (created_at);

                    CREATE TABLE IF NOT EXISTS session_events (
                        id               INTEGER PRIMARY KEY,
                        at               TEXT    NOT NULL,
                        kind             TEXT    NOT NULL,
                        mode             TEXT,
                        profile_id       TEXT,
                        planned_end      TEXT,
                        reason_protected BLOB
                    );
                    CREATE INDEX IF NOT EXISTS ix_session_events_at ON session_events (at);
                    """, cancellationToken).ConfigureAwait(false);
            }

            if (version < 3)
            {
                // Classify-prompt answers keep why (encrypted) and whether they were for one session only.
                await ExecuteAsync(connection, """
                    ALTER TABLE activity_labels ADD COLUMN lifetime TEXT NOT NULL DEFAULT 'Permanent';
                    ALTER TABLE activity_labels ADD COLUMN reason_protected BLOB;
                    """, cancellationToken).ConfigureAwait(false);
            }

            if (version < 4)
            {
                // ADR 17: the section of a site (subreddit, YouTube channel, GitHub repository), readable.
                await ExecuteAsync(connection, "ALTER TABLE activity_segments ADD COLUMN section TEXT;", cancellationToken)
                    .ConfigureAwait(false);
            }

            // Future migrations: if (version < 5) { ... }

            if (version < CurrentSchemaVersion)
            {
                await ExecuteAsync(connection, $"PRAGMA user_version = {CurrentSchemaVersion};", cancellationToken)
                    .ConfigureAwait(false);
            }

            _migrated = true;
        }
        finally
        {
            _migrationLock.Release();
        }
    }

    private async Task<string> BackupAsync(SqliteConnection connection, string label, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(BackupFolder);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string path = Path.Combine(BackupFolder, $"before-{label}-{stamp}.db");
        await using (SqliteCommand command = connection.CreateCommand())
        {
            // VACUUM INTO writes a clean, consistent copy even while the database is in use.
            command.CommandText = "VACUUM INTO $path;";
            command.Parameters.AddWithValue("$path", path);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (FileInfo old in new DirectoryInfo(BackupFolder).GetFiles("before-*.db")
                     .OrderByDescending(file => file.CreationTimeUtc).Skip(KeptBackups))
        {
            old.Delete();
        }

        return path;
    }

    private static async Task<long> ReadSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long version ? version : 0;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
