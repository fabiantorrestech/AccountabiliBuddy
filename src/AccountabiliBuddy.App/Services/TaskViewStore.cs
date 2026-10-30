using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>Loads and saves the Tasks page's filter and sort in the local document "tasks.view" (this PC only, never synced), like "stats.view".</summary>
public sealed class TaskViewStore
{
    private const string DocumentKey = "tasks.view";

    private readonly SqliteJsonDocumentStore _documents;

    public TaskViewStore(SqliteJsonDocumentStore documents)
    {
        _documents = documents;
    }

    /// <summary>The saved choices, or null when none were saved or the document can't be read (the defaults stay).</summary>
    public TaskViewPreferences? Load()
    {
        try
        {
            return Task.Run(() => _documents.LoadAsync<TaskViewPreferences>(DocumentKey)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(TaskViewPreferences preferences)
    {
        try
        {
            await _documents.SaveAsync(DocumentKey, preferences);
        }
        catch (SqliteException)
        {
            // Not remembered this time; nothing else depends on it.
        }
    }
}
