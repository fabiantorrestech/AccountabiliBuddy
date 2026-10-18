using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Keeps the running session on disk (documents key "session") so killing the app, a crash or a restart does not
/// end it. Saved on every state change and every 30 seconds while a session runs, so the time the app was closed
/// is known to within that interval. Use from the UI thread only.
/// </summary>
public sealed class SessionCheckpointService : IDisposable
{
    private const string DocumentKey = "session";
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(1);

    private readonly SessionController _sessions;
    private readonly SqliteJsonDocumentStore _store;

    // Saves run one after another so an older state never overwrites a newer one.
    private Task _pendingSave = Task.CompletedTask;
    private DateTimeOffset _lastSavedAt = DateTimeOffset.MinValue;

    public SessionCheckpointService(SessionController sessions, SqliteJsonDocumentStore store)
    {
        _sessions = sessions;
        _store = store;
        _sessions.StateChanged += OnStateChanged;
        _sessions.Ticked += OnTicked;
    }

    /// <returns>The session saved before the app last stopped, or null if there is none or it can't be read.</returns>
    public SavedSession? LoadBlocking()
    {
        try
        {
            return Task.Run(() => _store.LoadAsync<SavedSession>(DocumentKey)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _sessions.StateChanged -= OnStateChanged;
        _sessions.Ticked -= OnTicked;
        _pendingSave.Wait(FlushTimeout);
    }

    private void OnStateChanged(object? sender, SessionState state)
    {
        Save();
    }

    private void OnTicked(object? sender, EventArgs e)
    {
        if (_sessions.State.IsActive && _sessions.Now - _lastSavedAt >= HeartbeatInterval)
        {
            Save();
        }
    }

    private void Save()
    {
        DateTimeOffset now = _sessions.Now;
        _lastSavedAt = now;
        var saved = new SavedSession(_sessions.CreateCheckpoint(), now);
        _pendingSave = _pendingSave.ContinueWith(
            async _ =>
            {
                try
                {
                    await _store.SaveAsync(DocumentKey, saved);
                }
                catch (SqliteException)
                {
                    // Best effort: the next state change or heartbeat tries again.
                }
            },
            TaskScheduler.Default).Unwrap();
    }
}
