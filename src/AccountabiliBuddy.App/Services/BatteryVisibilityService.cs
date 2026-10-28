using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Keeps which devices' batteries show where (Settings → Devices &amp; sync → Batteries): per device in the collapsed overlay,
/// the Ctrl+Alt panel and the app, plus "Always show a device below 20%" and "Hide a device once it's full". Document
/// "batteryVisibility", this PC only. Stored now; the overlay and panel read it. Use from the UI thread only.
/// </summary>
public sealed class BatteryVisibilityService
{
    private const string DocumentKey = "batteryVisibility";

    private readonly SqliteJsonDocumentStore _store;

    public BatteryVisibilityService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public BatteryVisibilitySettings Current { get; private set; } = new();

    /// <summary>Raised after a change was kept.</summary>
    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            Current = Task.Run(() => _store.LoadAsync<BatteryVisibilitySettings>(DocumentKey)).GetAwaiter().GetResult() ?? new BatteryVisibilitySettings();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = new BatteryVisibilitySettings();
        }
    }

    public Task SetOptionsAsync(bool alwaysShowBelow20, bool hideWhenFull)
    {
        return SaveAsync(Current with { AlwaysShowBelow20 = alwaysShowBelow20, HideWhenFull = hideWhenFull });
    }

    public Task SetPlacesAsync(string deviceKey, BatteryPlaces places)
    {
        var devices = new Dictionary<string, BatteryPlaces>(Current.Devices ?? []) { [deviceKey] = places };
        return SaveAsync(Current with { Devices = devices });
    }

    private async Task SaveAsync(BatteryVisibilitySettings settings)
    {
        Current = settings;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            await _store.SaveAsync(DocumentKey, settings);
        }
        catch (SqliteException)
        {
            // Not remembered this time; the choice holds until the app closes.
        }
    }
}
