using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>Loads, saves and announces changes to <see cref="PrivacySettings"/>. Use from the UI thread only.</summary>
public sealed class PrivacySettingsService
{
    private const string DocumentKey = "privacy";

    private readonly SqliteJsonDocumentStore _store;

    public PrivacySettingsService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public PrivacySettings Current { get; private set; } = PrivacySettings.Default;

    public event EventHandler<PrivacySettings>? Changed;

    public void LoadBlocking()
    {
        try
        {
            Current = Task.Run(() => _store.LoadAsync<PrivacySettings>(DocumentKey)).GetAwaiter().GetResult()
                ?? PrivacySettings.Default;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            // Unreadable settings fall back to the safe defaults (screen reading off).
            Current = PrivacySettings.Default;
        }
    }

    public Task SetScreenReadingAsync(bool enabled)
    {
        return UpdateAsync(Current with { ScreenReadingEnabled = enabled });
    }

    public Task SetActivityLogAsync(bool enabled)
    {
        return UpdateAsync(Current with { ActivityLogEnabled = enabled });
    }

    private async Task UpdateAsync(PrivacySettings settings)
    {
        Current = settings;
        Changed?.Invoke(this, settings);
        await _store.SaveAsync(DocumentKey, settings);
    }
}
