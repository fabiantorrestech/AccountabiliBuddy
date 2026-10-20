using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>Loads, saves and announces changes to <see cref="OverlaySettings"/>. Use from the UI thread only.</summary>
public sealed class OverlaySettingsService
{
    private const string DocumentKey = "overlay";

    private readonly SqliteJsonDocumentStore _store;

    public OverlaySettingsService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public OverlaySettings Current { get; private set; } = OverlaySettings.Default;

    public event EventHandler<OverlaySettings>? Changed;

    public void LoadBlocking()
    {
        try
        {
            OverlaySettings? loaded = Task.Run(() => _store.LoadAsync<OverlaySettings>(DocumentKey)).GetAwaiter().GetResult();
            Current = loaded is null
                ? OverlaySettings.Default
                : WithDefaultTimer(loaded with
                {
                    Opacity = Math.Clamp(loaded.Opacity, OverlaySettings.MinOpacity, 1),
                    Profiles = loaded.Profiles ?? OverlaySettings.Default.Profiles,
                });
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = OverlaySettings.Default;
        }
    }

    /// <summary>
    /// Settings saved before there was a default timer: a profile with its timer off now follows the default
    /// (which starts off, so nothing changes until the default is turned on); one with it on keeps its own choice.
    /// </summary>
    private static OverlaySettings WithDefaultTimer(OverlaySettings settings)
    {
        if (settings.DefaultTimer is not null)
        {
            return settings;
        }

        Dictionary<Guid, ProfileOverlay> profiles = settings.Profiles.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Enabled ? entry.Value : entry.Value with { UseDefault = true });
        return settings with { Profiles = profiles, DefaultTimer = ProfileOverlay.Off };
    }

    /// <summary>
    /// Saves the look (style, size, opacity, marks and options), the default timer and the timeline; the position and
    /// per-profile choices are kept.
    /// </summary>
    public Task SaveLookAsync(Func<OverlaySettings, OverlaySettings> change)
    {
        OverlaySettings changed = change(Current);
        return SaveAsync(changed with
        {
            Opacity = Math.Clamp(changed.Opacity, OverlaySettings.MinOpacity, 1),
            Divisions = Math.Max(0, changed.Divisions),
            QuickHideSeconds = Math.Clamp(changed.QuickHideSeconds, OverlaySettings.MinQuickHideSeconds, OverlaySettings.MaxQuickHideSeconds),
            MaximizedAppDrop = Math.Clamp(changed.MaximizedAppDrop, 0, OverlaySettings.MaxMaximizedAppDrop),
            X = Current.X,
            Y = Current.Y,
            Profiles = Current.Profiles,
        });
    }

    /// <summary>Where the timer was dragged to; null puts it back in the default corner.</summary>
    public Task SavePositionAsync(int? x, int? y)
    {
        return SaveAsync(Current with { X = x, Y = y });
    }

    /// <summary>Replaces the per-profile choices; profiles left out have the timer off.</summary>
    public Task SaveProfilesAsync(IReadOnlyDictionary<Guid, ProfileOverlay> profiles)
    {
        return SaveAsync(Current with { Profiles = profiles });
    }

    private async Task SaveAsync(OverlaySettings settings)
    {
        Current = settings;
        Changed?.Invoke(this, settings);
        await _store.SaveAsync(DocumentKey, settings);
    }
}
