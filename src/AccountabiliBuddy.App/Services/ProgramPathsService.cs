using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Where each app's program file is (document "history.appPaths": process name → path), remembered the first time
/// the app is seen in use each run, so History can show it in File Explorer later even when the app isn't running.
/// A remembered file that is gone (the app updated or moved) is looked up again from the running app. Stays on this
/// PC. Use from the UI thread only (watcher events are marshalled here).
/// </summary>
public sealed class ProgramPathsService
{
    public const string DocumentKey = "history.appPaths";

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDesktopShell _shell;
    private Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);

    // Apps already looked up this run, so a window switch doesn't look again.
    private readonly HashSet<string> _looked = new(StringComparer.OrdinalIgnoreCase);

    public ProgramPathsService(SqliteJsonDocumentStore store, IDesktopShell shell)
    {
        _store = store;
        _shell = shell;
    }

    public void LoadBlocking()
    {
        try
        {
            Dictionary<string, string>? saved = Task.Run(() => _store.LoadAsync<Dictionary<string, string>>(DocumentKey)).GetAwaiter().GetResult();
            _paths = new Dictionary<string, string>(saved ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Start(IActivityWatcher watcher)
    {
        watcher.ActivityChanged += (_, snapshot) => Dispatcher.UIThread.Post(() => _ = RememberAsync(snapshot.ProcessName));
    }

    /// <summary>The app's program file: the remembered one if it still exists, else the running app's; null when neither.</summary>
    public async Task<string?> FindAsync(string processName)
    {
        if (_paths.TryGetValue(processName, out string? known) && File.Exists(known))
        {
            return known;
        }

        _looked.Remove(processName);
        return await RememberAsync(processName);
    }

    private async Task<string?> RememberAsync(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName) || !_looked.Add(processName))
        {
            return _paths.GetValueOrDefault(processName);
        }

        string? path = await Task.Run(() => _shell.FindRunningProgram(processName));
        if (path is null || string.Equals(_paths.GetValueOrDefault(processName), path, StringComparison.OrdinalIgnoreCase))
        {
            return path ?? _paths.GetValueOrDefault(processName);
        }

        _paths[processName] = path;
        await _store.SaveAsync(DocumentKey, new SortedDictionary<string, string>(_paths, StringComparer.OrdinalIgnoreCase));
        return path;
    }
}
