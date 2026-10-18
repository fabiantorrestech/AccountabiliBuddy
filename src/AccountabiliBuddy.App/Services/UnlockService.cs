using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Runs unlocks (documents key "unlock"): after the chosen option's wait, the running session ends (logged as ended
/// early with the reason) and scheduled blocks are held until the relock time, or until "Lock again" when there is
/// none. Kept on disk, so restarting the app neither skips nor resets the wait. Sessions the user starts during an
/// unlock run normally. Use from the UI thread only.
/// </summary>
public sealed class UnlockService : IDisposable
{
    private const string DocumentKey = "unlock";

    private readonly SessionController _sessions;
    private readonly SqliteJsonDocumentStore _store;

    // Saves run one after another so an older state never overwrites a newer one.
    private Task _pendingSave = Task.CompletedTask;

    public UnlockService(SessionController sessions, SqliteJsonDocumentStore store)
    {
        _sessions = sessions;
        _store = store;
        _sessions.Ticked += OnTicked;
    }

    /// <summary>The unlock that is waiting or in effect; null when there is none.</summary>
    public Unlock? Current { get; private set; }

    public UnlockStatus Status => Current?.StatusAt(_sessions.Now) ?? UnlockStatus.None;

    /// <summary>Raised when an unlock is requested, cancelled, takes effect or relocks.</summary>
    public event EventHandler? Changed;

    /// <summary>Loads a saved unlock and holds the schedule if it is in effect. Call before the sessions start.</summary>
    public void LoadBlocking()
    {
        try
        {
            Current = Task.Run(() => _store.LoadAsync<Unlock>(DocumentKey)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = null;
        }

        if (Current is not null && Status == UnlockStatus.None)
        {
            SetCurrent(null);
        }

        ApplyHold();
    }

    /// <param name="relockAt">When the blocks come back; null keeps them lifted until <see cref="LockAgain"/>.</param>
    /// <returns>False if an unlock is already waiting or in effect.</returns>
    public bool Request(UnlockOption option, DateTimeOffset? relockAt, string reason)
    {
        if (Status != UnlockStatus.None)
        {
            return false;
        }

        DateTimeOffset now = _sessions.Now;
        SetCurrent(new Unlock(option.Name, now, now + option.Wait, relockAt, reason));

        // A zero wait takes effect at once rather than on the next tick.
        Advance();
        return true;
    }

    /// <summary>Stops a countdown that is still running.</summary>
    public void Cancel()
    {
        if (Status == UnlockStatus.Waiting)
        {
            SetCurrent(null);
        }
    }

    /// <summary>Ends an unlock that is in effect: scheduled blocks can start again.</summary>
    public void LockAgain()
    {
        if (Status == UnlockStatus.Unlocked)
        {
            SetCurrent(null);
            ApplyHold();
        }
    }

    public void Dispose()
    {
        _sessions.Ticked -= OnTicked;
    }

    private void OnTicked(object? sender, EventArgs e)
    {
        Advance();
    }

    private void Advance()
    {
        if (Current is not Unlock unlock)
        {
            return;
        }

        switch (unlock.StatusAt(_sessions.Now))
        {
            case UnlockStatus.Unlocked when !unlock.Activated:
                SetCurrent(unlock with { Activated = true });
                ApplyHold();
                if (_sessions.State.IsActive)
                {
                    // The hold decides when scheduled blocks come back, so "Lock again" restores them at once.
                    _sessions.EndSession($"Unlocked ({unlock.OptionName}): {unlock.Reason}", suppressSchedule: false);
                }

                break;

            case UnlockStatus.None:
                // Relock time reached.
                SetCurrent(null);
                ApplyHold();
                break;
        }
    }

    private void ApplyHold()
    {
        DateTimeOffset? holdUntil = Current is { } unlock && Status == UnlockStatus.Unlocked
            ? unlock.RelockAt ?? DateTimeOffset.MaxValue
            : null;
        _sessions.HoldSchedule(holdUntil);
    }

    private void SetCurrent(Unlock? unlock)
    {
        Current = unlock;
        Changed?.Invoke(this, EventArgs.Empty);

        _pendingSave = _pendingSave.ContinueWith(
            async _ =>
            {
                try
                {
                    // Stored as JSON null when there is no unlock.
                    await _store.SaveAsync(DocumentKey, unlock);
                }
                catch (SqliteException)
                {
                    // Best effort; the in-memory state still applies until the app closes.
                }
            },
            TaskScheduler.Default).Unwrap();
    }
}
