using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>Loads, saves and announces changes to <see cref="SyncSettings"/>. Use from the UI thread only.</summary>
public sealed class SyncSettingsService
{
    private const string DocumentKey = "sync.settings";

    private readonly SqliteJsonDocumentStore _store;

    public SyncSettingsService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public SyncSettings Current { get; private set; } = SyncSettings.Default;

    public event EventHandler<SyncSettings>? Changed;

    public void LoadBlocking()
    {
        try
        {
            Current = Task.Run(() => _store.LoadAsync<SyncSettings>(DocumentKey)).GetAwaiter().GetResult() ?? SyncSettings.Default;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = SyncSettings.Default;
        }
    }

    public async Task SaveAsync(SyncSettings settings)
    {
        Current = settings;
        Changed?.Invoke(this, settings);
        await _store.SaveAsync(DocumentKey, settings);
    }
}
