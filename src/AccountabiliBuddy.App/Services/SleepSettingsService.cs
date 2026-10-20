using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>Loads, saves and announces changes to <see cref="SleepSettings"/>. Use from the UI thread only.</summary>
public sealed class SleepSettingsService
{
    private const string DocumentKey = "sleep";

    private readonly SqliteJsonDocumentStore _store;

    public SleepSettingsService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public SleepSettings Current { get; private set; } = SleepSettings.Default;

    public event EventHandler<SleepSettings>? Changed;

    public void LoadBlocking()
    {
        try
        {
            SleepSettings? loaded = Task.Run(() => _store.LoadAsync<SleepSettings>(DocumentKey)).GetAwaiter().GetResult();
            Current = loaded is null || loaded.Weeknights is null || loaded.Weekends is null ? SleepSettings.Default : loaded;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = SleepSettings.Default;
        }
    }

    public async Task SaveAsync(SleepSettings settings)
    {
        Current = settings;
        Changed?.Invoke(this, settings);
        await _store.SaveAsync(DocumentKey, settings);
    }
}
