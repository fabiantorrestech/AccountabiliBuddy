using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.Core.Storage;

/// <summary>
/// Saves small app-owned JSON documents (not part of the shared profile schema) in the local database's
/// <c>documents</c> table, one row per key.
/// </summary>
public sealed class SqliteJsonDocumentStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly LocalDatabase _database;
    private readonly TimeProvider _timeProvider;

    public SqliteJsonDocumentStore(LocalDatabase database, TimeProvider timeProvider)
    {
        _database = database;
        _timeProvider = timeProvider;
    }

    /// <returns>The stored value, or null if the key has never been saved.</returns>
    public async Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM documents WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);

        object? json = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return json is string text ? JsonSerializer.Deserialize<T>(text, SerializerOptions) : default;
    }

    public async Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        string json = JsonSerializer.Serialize(value, SerializerOptions);

        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO documents (key, json, updated_at)
            VALUES ($key, $json, $updatedAt)
            ON CONFLICT(key) DO UPDATE SET json = excluded.json, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$updatedAt", _timeProvider.GetUtcNow().ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves several documents in one transaction: all of them or none (a store whose documents must agree, e.g. ADR 76's task list
    /// and its History). Each value is written as <see cref="SaveAsync{T}"/> writes it.
    /// </summary>
    public async Task SaveAllAsync(IReadOnlyList<KeyValuePair<string, object>> documents, CancellationToken cancellationToken = default)
    {
        string updatedAt = _timeProvider.GetUtcNow().ToString("O");
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach ((string key, object value) in documents)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO documents (key, json, updated_at)
                VALUES ($key, $json, $updatedAt)
                ON CONFLICT(key) DO UPDATE SET json = excluded.json, updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(value, value.GetType(), SerializerOptions));
            command.Parameters.AddWithValue("$updatedAt", updatedAt);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Every saved key (the device-settings registry's check, ADR 71).</summary>
    public async Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT key FROM documents ORDER BY key;";
        var keys = new List<string>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    /// <summary>Removes the document; a key that was never saved is not an error.</summary>
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM documents WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
