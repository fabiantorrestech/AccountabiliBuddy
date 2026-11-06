using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>Loads, saves and announces changes to <see cref="SessionSoundSettings"/>. Use from the UI thread only.</summary>
public sealed class SessionSoundSettingsService
{
    private const string DocumentKey = "sounds";

    private readonly SqliteJsonDocumentStore _store;

    public SessionSoundSettingsService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public SessionSoundSettings Current { get; private set; } = SessionSoundSettings.Default;

    public event EventHandler<SessionSoundSettings>? Changed;

    /// <summary>ADR 69: the sounds are kept per mode. Set by the app before loading; null = one copy.</summary>
    public ModeSettings? Modes { get; init; }

    public void LoadBlocking()
    {
        try
        {
            string key = Modes?.ReadKeyBlocking(DocumentKey, SessionSoundSettings.Default) ?? DocumentKey;
            SessionSoundSettings? loaded = Task.Run(() => _store.LoadAsync<SessionSoundSettings>(key)).GetAwaiter().GetResult();
            Current = loaded is null ? SessionSoundSettings.Default : Clamped(loaded);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = SessionSoundSettings.Default;
        }
    }

    public async Task SaveAsync(SessionSoundSettings settings)
    {
        Current = Clamped(settings);
        Changed?.Invoke(this, Current);
        await _store.SaveAsync(Modes?.Key(DocumentKey) ?? DocumentKey, Current);
    }

    /// <summary>The mode switched or the compare view changed a value (ADR 69): reads the active mode's copy again.</summary>
    public void ReloadForMode()
    {
        LoadBlocking();
        Changed?.Invoke(this, Current);
    }

    private static SessionSoundSettings Clamped(SessionSoundSettings settings)
    {
        return settings with
        {
            FocusEndSound = settings.FocusEndSound ?? SessionSoundSettings.Default.FocusEndSound,
            BreakEndSound = settings.BreakEndSound ?? SessionSoundSettings.Default.BreakEndSound,
            Volume = Math.Clamp(settings.Volume, 0, 100),
        };
    }
}
