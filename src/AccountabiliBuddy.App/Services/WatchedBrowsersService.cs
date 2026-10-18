using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Remembers which browsers' extensions have connected since the browser was granted in Setup
/// (documents key "integrations.browsers.watched"). During focus, windows of a watched browser that the extension
/// does not report count as distracting (<see cref="AccountabiliBuddy.Core.Engine.UnseenBrowserWindow"/>), so turning
/// the extension off or using a private window is not a way out. Revoking the browser in Setup forgets it.
/// Read from the UI thread only.
/// </summary>
public sealed class WatchedBrowsersService : IDisposable
{
    private const string DocumentKey = "integrations.browsers.watched";

    private readonly IBrowserActivitySource _browsers;
    private readonly SqliteJsonDocumentStore _store;
    private readonly HashSet<BrowserKind> _watched = new();

    public WatchedBrowsersService(IBrowserActivitySource browsers, SqliteJsonDocumentStore store)
    {
        _browsers = browsers;
        _store = store;
        _browsers.ConnectionsChanged += OnConnectionsChanged;
    }

    public IReadOnlySet<BrowserKind> Watched => _watched;

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            BrowserKind[]? saved = Task.Run(() => _store.LoadAsync<BrowserKind[]>(DocumentKey)).GetAwaiter().GetResult();
            _watched.UnionWith(saved ?? Array.Empty<BrowserKind>());
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            // Nothing remembered: browsers become watched again as soon as their extension connects.
        }
    }

    /// <summary>Stops watching browsers that are no longer granted (call after a Revoke in Setup).</summary>
    public void ForgetUngranted(IBrowserSetup setup)
    {
        int removed = _watched.RemoveWhere(browser => !setup.GetStatus(browser).IsHostRegistered);
        if (removed > 0)
        {
            SaveAndNotify();
        }
    }

    public void Dispose()
    {
        _browsers.ConnectionsChanged -= OnConnectionsChanged;
    }

    private void OnConnectionsChanged(object? sender, EventArgs e)
    {
        // Raised on the pipe's thread.
        Dispatcher.UIThread.Post(AddConnected);
    }

    private void AddConnected()
    {
        bool added = false;
        foreach (BrowserConnection connection in _browsers.Connections)
        {
            added |= _watched.Add(connection.Browser);
        }

        if (added)
        {
            SaveAndNotify();
        }
    }

    private void SaveAndNotify()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        BrowserKind[] snapshot = _watched.ToArray();
        _ = Task.Run(async () =>
        {
            try
            {
                await _store.SaveAsync(DocumentKey, snapshot);
            }
            catch (SqliteException)
            {
                // Best effort; rebuilt from the next connection.
            }
        });
    }
}
