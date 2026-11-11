using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Tasks;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// Loads and saves <see cref="TaskSettings"/> (document "tasks.settings", this PC only). Fewer actions per session is stricter and
/// applies at once; more is a looser change: it is refused during a focus session and otherwise waits like the other looser changes
/// (<see cref="Core.Domain.LockSettings.LooseningDelay"/>, counted by the trusted clock and kept in the document so a restart doesn't
/// skip it), and it takes effect only while no session runs. A running session never sees a change either way: it keeps the number
/// it started with (<see cref="Core.Domain.SessionChecklist.ActionCap"/>). Use from the UI thread only.
/// </summary>
public sealed class TaskSettingsService
{
    public const string DocumentKey = "tasks.settings";

    private readonly SqliteJsonDocumentStore _store;
    private readonly TimeProvider _time;
    private readonly SessionController _sessions;
    private readonly Func<TimeSpan> _looserWait;

    /// <param name="looserWait">How long a looser change waits: the looser-change wait of the configuration in effect.</param>
    public TaskSettingsService(SqliteJsonDocumentStore store, TimeProvider time, SessionController sessions, Func<TimeSpan> looserWait)
    {
        _store = store;
        _time = time;
        _sessions = sessions;
        _looserWait = looserWait;
        _sessions.Ticked += (_, _) => ApplyPendingIfReady();
    }

    public TaskSettings Current { get; private set; } = TaskSettings.Default;

    /// <summary>How many actions the next session holds (2 to 5).</summary>
    public int SessionActions => Core.Tasks.SessionActions.ClampCap(Current.SessionActions);

    public bool NextStepTakesSlot => Current.NextStepTakesSlot;

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            TaskSettings? loaded = Task.Run(() => _store.LoadAsync<TaskSettings>(DocumentKey)).GetAwaiter().GetResult();
            Current = loaded is null ? TaskSettings.Default : Clamped(loaded);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = TaskSettings.Default;
        }
    }

    /// <summary>
    /// The number of actions a session holds. Fewer applies at once (and drops a waiting larger number); more waits the looser-change
    /// wait and is refused during a focus session.
    /// </summary>
    /// <returns>False when it was refused (a larger number during a focus session).</returns>
    public async Task<bool> SetSessionActionsAsync(int actions)
    {
        int wanted = Core.Tasks.SessionActions.ClampCap(actions);
        if (wanted <= SessionActions)
        {
            await SaveAsync(Current with { SessionActions = wanted, PendingSessionActions = null, PendingFrom = null });
            return true;
        }

        if (_sessions.IsEnforcing)
        {
            return false;
        }

        // A wait already running for the same number keeps its time.
        if (Current.PendingSessionActions == wanted && Current.PendingFrom is not null)
        {
            return true;
        }

        await SaveAsync(Current with { PendingSessionActions = wanted, PendingFrom = _time.GetUtcNow() + _looserWait() });
        return true;
    }

    /// <summary>"The next step takes the slot" (on by default). Either way it loosens nothing: it only says which step is in focus.</summary>
    public Task SetNextStepTakesSlotAsync(bool on)
    {
        return on == Current.NextStepTakesSlot ? Task.CompletedTask : SaveAsync(Current with { NextStepTakesSlot = on });
    }

    private void ApplyPendingIfReady()
    {
        if (Current.PendingSessionActions is not int pending || Current.PendingFrom is not DateTimeOffset from
            || _time.GetUtcNow() < from || _sessions.State.IsActive)
        {
            return;
        }

        _ = SaveAsync(Current with { SessionActions = pending, PendingSessionActions = null, PendingFrom = null });
    }

    private async Task SaveAsync(TaskSettings settings)
    {
        Current = Clamped(settings);
        await _store.SaveAsync(DocumentKey, Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static TaskSettings Clamped(TaskSettings settings)
    {
        return settings with
        {
            SessionActions = Core.Tasks.SessionActions.ClampCap(settings.SessionActions),
            PendingSessionActions = settings.PendingSessionActions is int pending ? Core.Tasks.SessionActions.ClampCap(pending) : null,
        };
    }
}
