using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.Core.Storage;

/// <summary>
/// The single local SQLite database (ADR 6). Creates the file and applies schema migrations on first open.
/// Schema version is tracked with SQLite's built-in <c>PRAGMA user_version</c>.
/// </summary>
public sealed class LocalDatabase
{
    private const string FileName = "accountabilibuddy.db";
    private const int CurrentSchemaVersion = 1;

    private readonly string _connectionString;
    private readonly SemaphoreSlim _migrationLock = new(1, 1);
    private bool _migrated;

    public LocalDatabase(string directory)
    {
        Directory.CreateDirectory(directory);
        DatabasePath = Path.Combine(directory, FileName);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString();
    }

    public string DatabasePath { get; }

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

            // Future migrations: if (version < 2) { ... }

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
