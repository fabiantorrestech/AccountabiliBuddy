using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>Loads, saves and announces changes to <see cref="DayBarSettings"/>. Use from the UI thread only.</summary>
public sealed class DayBarSettingsService
{
    private const string DocumentKey = "daybar";

    private readonly SqliteJsonDocumentStore _store;

    public DayBarSettingsService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public DayBarSettings Current { get; private set; } = DayBarSettings.Default;

    public event EventHandler<DayBarSettings>? Changed;

    public void LoadBlocking()
    {
        try
        {
            Current = Task.Run(() => _store.LoadAsync<DayBarSettings>(DocumentKey)).GetAwaiter().GetResult()
                ?? DayBarSettings.Default;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = DayBarSettings.Default;
        }
    }

    public async Task SaveAsync(DayBarSettings settings)
    {
        Current = settings;
        Changed?.Invoke(this, settings);
        await _store.SaveAsync(DocumentKey, settings);
    }
}
