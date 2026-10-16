using AccountabiliBuddy.Core.Ports;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.Core.Storage;

/// <summary>Stores the <see cref="ProfileDocument"/> as JSON in the local database (ADR 7).</summary>
public sealed class SqliteProfileStore : IProfileStore
{
    private const string DocumentKey = "profiles";

    private readonly LocalDatabase _database;
    private readonly TimeProvider _timeProvider;

    public SqliteProfileStore(LocalDatabase database, TimeProvider timeProvider)
    {
        _database = database;
        _timeProvider = timeProvider;
    }

    public async Task<ProfileDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM documents WHERE key = $key;";
        command.Parameters.AddWithValue("$key", DocumentKey);

        object? json = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return json is string text
            ? ProfileDocumentSerializer.Deserialize(text)
            : ProfileDocument.CreateDefault();
    }

    public async Task SaveAsync(ProfileDocument document, CancellationToken cancellationToken = default)
    {
        string json = ProfileDocumentSerializer.Serialize(document);

        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO documents (key, json, updated_at)
            VALUES ($key, $json, $updatedAt)
            ON CONFLICT(key) DO UPDATE SET json = excluded.json, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$key", DocumentKey);
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$updatedAt", _timeProvider.GetUtcNow().ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
