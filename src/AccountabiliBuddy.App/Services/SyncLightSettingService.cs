using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Keeps the sync light's setting (Settings → Devices &amp; sync → Sync light): always, only when something is wrong, or off; and which
/// sections of the title-bar menu are unfolded (Tasks redesign 9.25). Display only; it changes nothing about syncing. Document
/// "syncLight", this PC only. Use from the UI thread only.
/// </summary>
public sealed class SyncLightSettingService
{
    private const string DocumentKey = "syncLight";

    private readonly SqliteJsonDocumentStore _store;

    private SyncLightSettings _current = new();

    public SyncLightSettingService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public SyncLightMode Mode => _current.Mode;

    /// <summary>The title-bar menu's device list is unfolded.</summary>
    public bool DevicesOpen => _current.DevicesOpen;

    /// <summary>The title-bar menu's theme list is unfolded.</summary>
    public bool ThemesOpen => _current.ThemesOpen;

    /// <summary>Raised after the mode changed.</summary>
    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            SyncLightSettings? saved = Task.Run(() => _store.LoadAsync<SyncLightSettings>(DocumentKey)).GetAwaiter().GetResult();
            _current = saved is null ? new SyncLightSettings()
                : Enum.IsDefined(saved.Mode) ? saved : saved with { Mode = SyncLightMode.Always };
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _current = new SyncLightSettings();
        }
    }

    public async Task SetAsync(SyncLightMode mode)
    {
        if (mode == Mode)
        {
            return;
        }

        _current = _current with { Mode = mode };
        Changed?.Invoke(this, EventArgs.Empty);
        await SaveAsync();
    }

    /// <summary>Remembers which of the title-bar menu's sections are unfolded (no <see cref="Changed"/>: the light doesn't change).</summary>
    public async Task SetFoldsAsync(bool devicesOpen, bool themesOpen)
    {
        if (devicesOpen == DevicesOpen && themesOpen == ThemesOpen)
        {
            return;
        }

        _current = _current with { DevicesOpen = devicesOpen, ThemesOpen = themesOpen };
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        try
        {
            await _store.SaveAsync(DocumentKey, _current);
        }
        catch (Exception ex)
        {
            // Not remembered this time (any failure, as the save is fire-and-forget); the light and the menu still follow the choice until the app closes.
            System.Diagnostics.Debug.WriteLine($"syncLight not saved: {ex.Message}");
        }
    }
}
