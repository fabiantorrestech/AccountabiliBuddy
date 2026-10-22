using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Setup;

/// <summary>
/// The computer type this PC was set up as ("setup.computerType"), and applying one (<see cref="ComputerTypePreset"/>).
/// Settings → App &amp; data shows it and can apply a type again. Use from the UI thread only.
/// </summary>
public sealed class ComputerTypeService
{
    private const string DocumentKey = "setup.computerType";

    private readonly SqliteJsonDocumentStore _store;
    private readonly PrivacySettingsService _privacy;
    private readonly EnforcementLimitService _limit;
    private readonly SyncSettingsService _sync;
    private readonly UpdateService? _updates;

    public ComputerTypeService(
        SqliteJsonDocumentStore store, PrivacySettingsService privacy, EnforcementLimitService limit, SyncSettingsService sync, UpdateService? updates)
    {
        _store = store;
        _privacy = privacy;
        _limit = limit;
        _sync = sync;
        _updates = updates;
    }

    /// <summary>The type last applied; null before the assistant ran (or on a PC set up before types existed).</summary>
    public ComputerType? Current { get; private set; }

    public event EventHandler? Changed;

    /// <summary>The sync settings, for the summary (the folder chosen and its interval).</summary>
    public SyncSettings Sync => _sync.Current;

    public void LoadBlocking()
    {
        try
        {
            Current = Task.Run(() => _store.LoadAsync<Stored>(DocumentKey)).GetAwaiter().GetResult()?.Type;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = null;
        }
    }

    /// <summary>Sets the type's switches and remembers the type.</summary>
    public async Task ApplyAsync(ComputerType type)
    {
        await ComputerTypePreset.ApplyAsync(ComputerTypePreset.For(type), _privacy, _limit, _sync, _updates);
        Current = type;
        Changed?.Invoke(this, EventArgs.Empty);
        await _store.SaveAsync(DocumentKey, new Stored(type));
    }

    /// <summary>The sync folder chosen in the assistant (a Work computer asks for it); switches the folder method on.</summary>
    public Task SetFolderAsync(string path)
    {
        return _sync.SaveAsync(_sync.Current with { FolderPath = path, FolderOn = true });
    }

    /// <summary>E.g. "Work computer".</summary>
    public static string NameOf(ComputerType type)
    {
        return type switch
        {
            ComputerType.Work => "Work computer",
            ComputerType.LocalOnly => "Local only",
            _ => "Personal computer",
        };
    }

    private sealed record Stored(ComputerType Type);
}
