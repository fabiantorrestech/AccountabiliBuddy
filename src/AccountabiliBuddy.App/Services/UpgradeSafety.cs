using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Protects the data across updates. At launch, before anything reads the data: a database written by a newer
/// version is refused, and the first launch of a new version saves a copy of the database to the backups folder
/// (database migrations make their own copy too). Profiles migrate in memory when loaded, so the copy holds the old form.
/// </summary>
public static class UpgradeSafety
{
    private const string DocumentKey = "app.version";

    /// <returns>Why the data can't be opened, or null when it can.</returns>
    public static string? CheckBlocking(LocalDatabase database)
    {
        long version = Task.Run(() => database.ReadSchemaVersionAsync()).GetAwaiter().GetResult();
        return version > LocalDatabase.CurrentSchemaVersion
            ? new DatabaseTooNewException(version, LocalDatabase.CurrentSchemaVersion).Message
            : null;
    }

    /// <summary>Copies the database when this version runs with it for the first time (not for brand-new data).</summary>
    /// <returns>The copy's path, or null when none was needed.</returns>
    public static string? BackupIfNewVersionBlocking(LocalDatabase database, SqliteJsonDocumentStore documents, string appVersion, TimeProvider time)
    {
        return Task.Run(async () =>
        {
            bool hadData = await database.ReadSchemaVersionAsync() > 0;
            try
            {
                AppVersionRecord? last = await documents.LoadAsync<AppVersionRecord>(DocumentKey);
                if (last?.Version == appVersion)
                {
                    return null;
                }

                string? copy = hadData ? await database.BackupAsync($"version-{appVersion}") : null;
                await documents.SaveAsync(DocumentKey, new AppVersionRecord(appVersion, time.GetUtcNow()));
                return copy;
            }
            catch (Exception ex) when (ex is SqliteException or IOException or System.Text.Json.JsonException)
            {
                // Best effort: the app still starts; migrations keep their own copy.
                return null;
            }
        }).GetAwaiter().GetResult();
    }
}
