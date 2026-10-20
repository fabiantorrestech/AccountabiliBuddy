using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Loads and saves <see cref="NoiseSettings"/>. A dragged volume slider changes them many times a second, so
/// <see cref="SaveSoon"/> writes once things settle. Use from the UI thread only.
/// </summary>
public sealed class NoiseSettingsService
{
    private const string DocumentKey = "noise";
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    private readonly SqliteJsonDocumentStore _store;
    private readonly DispatcherTimer _saveTimer;

    public NoiseSettingsService(SqliteJsonDocumentStore store)
    {
        _store = store;
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += async (_, _) =>
        {
            _saveTimer.Stop();
            await WriteAsync();
        };
    }

    public NoiseSettings Current { get; private set; } = NoiseSettings.Default;

    public void LoadBlocking()
    {
        try
        {
            NoiseSettings? loaded = Task.Run(() => _store.LoadAsync<NoiseSettings>(DocumentKey)).GetAwaiter().GetResult();
            Current = loaded is null ? NoiseSettings.Default : Clamped(loaded);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = NoiseSettings.Default;
        }
    }

    public async Task SaveAsync(NoiseSettings settings)
    {
        _saveTimer.Stop();
        Current = Clamped(settings);
        await WriteAsync();
    }

    /// <summary>Takes effect now and is written a moment later.</summary>
    public void SaveSoon(NoiseSettings settings)
    {
        Current = Clamped(settings);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes a pending <see cref="SaveSoon"/> now (when the app quits).</summary>
    public async Task FlushAsync()
    {
        if (_saveTimer.IsEnabled)
        {
            _saveTimer.Stop();
            await WriteAsync();
        }
    }

    private async Task WriteAsync()
    {
        try
        {
            await _store.SaveAsync(DocumentKey, Current);
        }
        catch (SqliteException)
        {
            // The player keeps working; the setting is written with the next change.
        }
    }

    private static NoiseSettings Clamped(NoiseSettings settings)
    {
        return settings with
        {
            Volume = Math.Clamp(settings.Volume, 0, 100),
            PlayPauseKey = settings.PlayPauseKey ?? string.Empty,
            VolumeUpKey = settings.VolumeUpKey ?? string.Empty,
            VolumeDownKey = settings.VolumeDownKey ?? string.Empty,
        };
    }
}
