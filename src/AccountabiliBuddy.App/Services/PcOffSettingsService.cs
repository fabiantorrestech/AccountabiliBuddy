using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// What happens to a session when the PC goes off during it (ADR 60). Device-local (documents "session.pcOff", "session.pcOff.pending"),
/// not synced and not backed up. Keeping the original end is looser, since going off then uses up the session: it waits the
/// looser-change delay, is refused during a focus session, and only applies with no session running. Pausing the timer and the notice apply at once.
/// Use from the UI thread only.
/// </summary>
public sealed class PcOffSettingsService
{
    private const string DocumentKey = "session.pcOff";
    private const string PendingKey = "session.pcOff.pending";

    private readonly SqliteJsonDocumentStore _store;
    private readonly TimeProvider _time;
    private readonly SessionController _sessions;
    private readonly Func<TimeSpan> _looserWait;

    public PcOffSettingsService(SqliteJsonDocumentStore store, TimeProvider time, SessionController sessions, Func<TimeSpan> looserWait)
    {
        _store = store;
        _time = time;
        _sessions = sessions;
        _looserWait = looserWait;
    }

    public PcOffSettings Current { get; private set; } = PcOffSettings.Default;

    /// <summary>When keeping the original end takes effect, while that waits; null otherwise.</summary>
    public DateTimeOffset? KeepEndPendingAt { get; private set; }

    public bool InFocus => _sessions.IsEnforcing;

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            Current = Task.Run(() => _store.LoadAsync<PcOffSettings>(DocumentKey)).GetAwaiter().GetResult() ?? PcOffSettings.Default;
            KeepEndPendingAt = Task.Run(() => _store.LoadAsync<DateTimeOffset?>(PendingKey)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = PcOffSettings.Default;
            KeepEndPendingAt = null;
        }
    }

    /// <returns>False when it was refused (keeping the end during a focus session).</returns>
    public async Task<bool> SetKeepOriginalEndAsync(bool keep)
    {
        if (!keep)
        {
            KeepEndPendingAt = null;
            await _store.DeleteAsync(PendingKey);
            await SaveAsync(Current with { KeepOriginalEnd = false });
            return true;
        }

        if (Current.KeepOriginalEnd || KeepEndPendingAt is not null)
        {
            return true;
        }

        if (InFocus)
        {
            return false;
        }

        KeepEndPendingAt = _time.GetUtcNow() + _looserWait();
        await _store.SaveAsync<DateTimeOffset?>(PendingKey, KeepEndPendingAt);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public Task SetTellMeAsync(bool tellMe)
    {
        return tellMe == Current.TellMe ? Task.CompletedTask : SaveAsync(Current with { TellMe = tellMe });
    }

    /// <summary>Applies a waiting change once its time has come and no session is running. Runs every second.</summary>
    public async Task ApplyPendingIfDueAsync()
    {
        if (KeepEndPendingAt is not DateTimeOffset at || _sessions.State.IsActive || _time.GetUtcNow() < at)
        {
            return;
        }

        KeepEndPendingAt = null;
        await _store.DeleteAsync(PendingKey);
        await SaveAsync(Current with { KeepOriginalEnd = true });
    }

    private async Task SaveAsync(PcOffSettings settings)
    {
        Current = settings;
        await _store.SaveAsync(DocumentKey, settings);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
