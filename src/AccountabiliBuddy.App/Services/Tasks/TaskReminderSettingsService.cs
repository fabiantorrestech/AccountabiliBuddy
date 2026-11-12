using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Tasks;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// Loads and saves <see cref="TaskReminderSettings"/> (document "tasks.reminderSettings", this PC only, not per mode). A ring setting
/// loosens nothing about a focus session (it only decides how a reminder rings), so those changes apply at once. "During a focus
/// session" is the exception (<see cref="SetDuringSessionAsync"/>): "Remind quietly" lets reminders show during a session, the looser
/// direction, so it follows the looser-change rule (waits, refused during a session, takes effect only while none runs); "Wait until the
/// session ends" is stricter and applies at once. <see cref="UpdateAsync"/> never changes it. Use from the UI thread only.
/// </summary>
public sealed class TaskReminderSettingsService
{
    public const string DocumentKey = "tasks.reminderSettings";

    private readonly SqliteJsonDocumentStore _store;
    private readonly TimeProvider _time;
    private readonly Func<bool> _sessionRunning;
    private readonly Func<TimeSpan> _looserWait;

    // One change at a time: each reads the settings it changes and saves them, so a change can't save over another one made meanwhile
    // (e.g. a ring setting's save putting back a "Remind quietly" that a stricter choice had just dropped).
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Without a session or a looser-change wait to follow (checks): "Remind quietly" still waits the default 10 minutes.</summary>
    public TaskReminderSettingsService(SqliteJsonDocumentStore store)
        : this(store, TimeProvider.System, () => false, () => TimeSpan.FromMinutes(10))
    {
    }

    /// <param name="time">The trusted clock.</param>
    /// <param name="sessionRunning">A focus session is running: a looser "During a focus session" is refused.</param>
    /// <param name="looserWait">How long a looser change waits: the looser-change wait of the configuration in effect.</param>
    public TaskReminderSettingsService(SqliteJsonDocumentStore store, TimeProvider time, Func<bool> sessionRunning, Func<TimeSpan> looserWait)
    {
        _store = store;
        _time = time;
        _sessionRunning = sessionRunning;
        _looserWait = looserWait;
    }

    public TaskReminderSettings Current { get; private set; } = TaskReminderSettings.Default;

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            TaskReminderSettings? loaded = Task.Run(() => _store.LoadAsync<TaskReminderSettings>(DocumentKey)).GetAwaiter().GetResult();
            Current = loaded is null ? TaskReminderSettings.Default : Clamped(loaded);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = TaskReminderSettings.Default;
        }
    }

    /// <summary>Changes the ring and calendar settings at once. "During a focus session" and its waiting change are kept as they are.</summary>
    public async Task UpdateAsync(Func<TaskReminderSettings, TaskReminderSettings> change)
    {
        await _gate.WaitAsync();
        try
        {
            TaskReminderSettings before = Current;
            TaskReminderSettings changed = Clamped(change(before)) with
            {
                DuringSession = before.DuringSession,
                PendingDuringSession = before.PendingDuringSession,
                PendingDuringSessionFrom = before.PendingDuringSessionFrom,
            };
            await SaveAsync(changed);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// "During a focus session". "Wait until the session ends" is stricter: at once, and it drops a waiting "Remind quietly". "Remind
    /// quietly" is looser: refused while a session runs, otherwise it waits the looser-change wait (kept in the document, so a restart
    /// doesn't skip it) and takes effect at the first moment after it with no session running (<see cref="ApplyPendingIfReady"/>).
    /// </summary>
    /// <returns>False when it was refused (Remind quietly while a session runs).</returns>
    public async Task<bool> SetDuringSessionAsync(TaskReminderDuringSession wanted)
    {
        await _gate.WaitAsync();
        try
        {
            return await SetDuringSessionLockedAsync(wanted);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> SetDuringSessionLockedAsync(TaskReminderDuringSession wanted)
    {
        TaskReminderSettings current = Current;
        if (wanted == TaskReminderDuringSession.WaitUntilEnd || current.DuringSession == wanted)
        {
            await SaveAsync(current with { DuringSession = wanted, PendingDuringSession = null, PendingDuringSessionFrom = null });
            return true;
        }

        if (_sessionRunning())
        {
            return false;
        }

        if (current.PendingDuringSession == wanted && current.PendingDuringSessionFrom is not null)
        {
            return true;
        }

        await SaveAsync(current with { PendingDuringSession = wanted, PendingDuringSessionFrom = _time.GetUtcNow() + _looserWait() });
        return true;
    }

    /// <summary>A waiting "During a focus session" change takes effect once its wait is over and no session runs (the app calls this every tick).</summary>
    public void ApplyPendingIfReady()
    {
        if (!IsPendingReady(Current))
        {
            return;
        }

        _ = ApplyPendingAsync();
    }

    private async Task ApplyPendingAsync()
    {
        await _gate.WaitAsync();
        try
        {
            // Read again inside the gate: a change made meanwhile (a stricter choice dropping it) wins.
            TaskReminderSettings current = Current;
            if (IsPendingReady(current))
            {
                await SaveAsync(current with { DuringSession = current.PendingDuringSession!.Value, PendingDuringSession = null, PendingDuringSessionFrom = null });
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsPendingReady(TaskReminderSettings current)
    {
        return current.PendingDuringSession is not null
            && current.PendingDuringSessionFrom is DateTimeOffset from
            && _time.GetUtcNow() >= from
            && !_sessionRunning();
    }

    private async Task SaveAsync(TaskReminderSettings changed)
    {
        if (changed == Current)
        {
            return;
        }

        await _store.SaveAsync(DocumentKey, changed);
        Current = changed;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clears the old device-local picked calendar once the account took it over.</summary>
    public Task ClearLegacyPickedCalendarAsync()
    {
        return UpdateAsync(current => current with { PickedCalendarId = null });
    }

    private static TaskReminderSettings Clamped(TaskReminderSettings settings)
    {
        string[] rules =
        [
            TaskReminderSettings.NoNewReminder, TaskReminder.AtStart, TaskReminder.TenMinutesBefore, TaskReminder.OneHourBefore, TaskReminder.DayBeforeAtNine,
        ];
        return settings with
        {
            Volume = Math.Clamp(settings.Volume, TaskReminderRingPolicy.MinVolume, 100),
            DismissProtectSeconds = Nearest(TaskReminderRingPolicy.ProtectChoices, settings.DismissProtectSeconds),
            StopAfterMinutes = Nearest(TaskReminderRingPolicy.StopAfterChoices, settings.StopAfterMinutes),
            SnoozeShortcuts = settings.SnoozeShortcuts is { Count: 3 } ? settings.SnoozeShortcuts : null,
            NewReminderRule = rules.Contains(settings.NewReminderRule) ? settings.NewReminderRule : TaskReminder.AtStart,
            NewRemindersTo = Enum.IsDefined(settings.NewRemindersTo) ? settings.NewRemindersTo : TaskCalendarDestination.GoogleCalendar,
            PickedCalendarId = string.IsNullOrWhiteSpace(settings.PickedCalendarId) ? null : settings.PickedCalendarId,
            DateOnlyEvent = Enum.IsDefined(settings.DateOnlyEvent) ? settings.DateOnlyEvent : TaskDateOnlyEvent.TimedAtNine,
            DuringSession = Enum.IsDefined(settings.DuringSession) ? settings.DuringSession : TaskReminderDuringSession.WaitUntilEnd,
            PendingDuringSession = settings.PendingDuringSession is { } pending && Enum.IsDefined(pending) && settings.PendingDuringSessionFrom is not null ? pending : null,
            PendingDuringSessionFrom = settings.PendingDuringSession is { } waiting && Enum.IsDefined(waiting) ? settings.PendingDuringSessionFrom : null,
        };
    }

    private static int Nearest(IReadOnlyList<int> choices, int value)
    {
        return choices.OrderBy(choice => Math.Abs(choice - value)).First();
    }
}
