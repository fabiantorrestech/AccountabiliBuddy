using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Keeps the sync light's setting (Settings → Devices &amp; sync → Sync light): always, only when something is wrong, or off.
/// Display only; it changes nothing about syncing. Document "syncLight", this PC only. Use from the UI thread only.
/// </summary>
public sealed class SyncLightSettingService
{
    private const string DocumentKey = "syncLight";

    private readonly SqliteJsonDocumentStore _store;

    public SyncLightSettingService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public SyncLightMode Mode { get; private set; } = SyncLightMode.Always;

    /// <summary>Raised after the mode changed.</summary>
    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            SyncLightSettings? saved = Task.Run(() => _store.LoadAsync<SyncLightSettings>(DocumentKey)).GetAwaiter().GetResult();
            Mode = saved is not null && Enum.IsDefined(saved.Mode) ? saved.Mode : SyncLightMode.Always;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Mode = SyncLightMode.Always;
        }
    }

    public async Task SetAsync(SyncLightMode mode)
    {
        if (mode == Mode)
        {
            return;
        }

        Mode = mode;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            await _store.SaveAsync(DocumentKey, new SyncLightSettings(mode));
        }
        catch (Exception ex)
        {
            // Not remembered this time (any failure, as the save is fire-and-forget); the light still follows the choice until the app closes.
            System.Diagnostics.Debug.WriteLine($"syncLight not saved: {ex.Message}");
        }
    }
}
