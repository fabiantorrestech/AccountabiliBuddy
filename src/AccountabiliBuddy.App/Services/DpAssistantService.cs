using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Remembers that the Setup DP Assistant was saved once on this PC (document "dpAssistant", device-local, never synced),
/// so the migration assistant's "set up?" check for it can say so. Use from the UI thread only.
/// </summary>
public sealed class DpAssistantService
{
    private const string DocumentKey = "dpAssistant";

    private readonly SqliteJsonDocumentStore _store;

    public DpAssistantService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    /// <summary>True once the assistant saved a plan.</summary>
    public bool HasRun { get; private set; }

    public void LoadBlocking()
    {
        try
        {
            HasRun = Task.Run(() => _store.LoadAsync<DpAssistantState>(DocumentKey)).GetAwaiter().GetResult()?.HasRun == true;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            HasRun = false;
        }
    }

    public async Task MarkRunAsync()
    {
        HasRun = true;
        await _store.SaveAsync(DocumentKey, new DpAssistantState(true));
    }

    private sealed record DpAssistantState(bool HasRun);
}
