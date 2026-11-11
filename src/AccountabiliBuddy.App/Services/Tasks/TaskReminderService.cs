using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// Task reminders on this PC (ADR 76, spec 05 §7 and §8): works out which reminders of the shared task list are due (the trusted
/// clock, <see cref="TaskReminderQueue"/>), keeps <see cref="Pending"/> up to date, and writes what the user does (snooze, dismiss,
/// start) into the task, so it syncs and a reminder handled on another device stops ringing here once the change arrives. Looks again
/// on every change of the task store, of the settings, after the computer wakes (<see cref="Refresh"/>) and when the next reminder
/// is due. This PC rings every reminder for itself whatever a label's "Rings on" says (spec 02). A reminder that rang until its
/// ring time ran out is remembered (document "tasks.reminderMissed", ids and times only) and not rung again, so a restart doesn't
/// repeat it; it stays on the task for the "Reminders due today" list. Reminders of a running session's own tasks don't ring (the
/// session holds them). Use from the UI thread only.
/// </summary>
public sealed class TaskReminderService : IDisposable
{
    public const string MissedKey = "tasks.reminderMissed";

    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinWait = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MissedKept = TimeSpan.FromDays(3);

    private readonly TaskStore _store;
    private readonly TaskReminderSettingsService _settings;
    private readonly SqliteJsonDocumentStore _documents;
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _timer = new();
    private Dictionary<string, DateTimeOffset> _missed = [];
    private IReadOnlyList<TaskReminderEntry> _entries = [];

    public TaskReminderService(TaskStore store, TaskReminderSettingsService settings, SqliteJsonDocumentStore documents, TimeProvider time)
    {
        _store = store;
        _settings = settings;
        _documents = documents;
        _time = time;
        _timer.Tick += (_, _) => Refresh();
    }

    /// <summary>The task actions, for "Work on it now" (the session's own add, which keeps the cap); set by the app.</summary>
    public TaskActionsService? Actions { get; set; }

    /// <summary>The reminders ringing now, in ring order (Can't miss first).</summary>
    public IReadOnlyList<TaskReminderEntry> Pending { get; private set; } = [];

    /// <summary>Every occurrence of the task list's reminders and where it stands (for the "Reminders due today" list).</summary>
    public IReadOnlyList<TaskReminderEntry> Entries => _entries;

    /// <summary><see cref="Pending"/> changed: a reminder came due, was handled here or on another device, or its task went.</summary>
    public event EventHandler? PendingChanged;

    /// <summary>"Work on it now" found no room in This session (its cap): the task id, for the "This session is full" dialog.</summary>
    public event EventHandler<Guid>? SessionFull;

    public TimeZoneInfo Zone => _time.LocalTimeZone;

    public DateTimeOffset Now => _time.GetUtcNow();

    public void LoadBlocking()
    {
        try
        {
            _missed = Task.Run(() => _documents.LoadAsync<Dictionary<string, DateTimeOffset>>(MissedKey)).GetAwaiter().GetResult() ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _missed = [];
        }
    }

    public void Start()
    {
        _store.Changed += (_, _) => Refresh();
        _settings.Changed += (_, _) => Refresh();
        Refresh();
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    /// <summary>The task's title, for showing (private text: never logged); null when the task is gone.</summary>
    public string? TitleOf(Guid taskId)
    {
        return _store.Lists.Tasks.FirstOrDefault(item => item.Id == taskId && item.Task.IsLive)?.Task.Title;
    }

    /// <summary>The task, for showing its chips and for Open; null when it is gone.</summary>
    public TaskItem? TaskOf(Guid taskId)
    {
        TaskItem? task = _store.Lists.Tasks.FirstOrDefault(item => item.Id == taskId && item.Task.IsLive)?.Task;
        return task;
    }

    /// <summary>Works out what is due now and when to look next; raises <see cref="PendingChanged"/> when the due list changed.</summary>
    public void Refresh()
    {
        DateTimeOffset now = Now;
        if (_store.IsActive)
        {
            IEnumerable<TaskItem> tasks = _store.Lists.Tasks.Where(item => !_store.IsHeld(item.Id)).Select(item => item.Task);
            _entries = TaskReminderQueue.Entries(tasks, Zone);
        }
        else
        {
            _entries = [];
        }

        List<TaskReminderEntry> due = TaskReminderQueue.Due(_entries, now).Where(entry => !_missed.ContainsKey(entry.Key)).ToList();
        if (!due.Select(entry => entry.Key).SequenceEqual(Pending.Select(entry => entry.Key)))
        {
            Pending = due;
            PendingChanged?.Invoke(this, EventArgs.Empty);
        }

        Schedule(now);
    }

    /// <summary>Rings it again at <paramref name="until"/> (a snooze moment, see <see cref="TaskSnooze"/>).</summary>
    public async Task SnoozeAsync(TaskReminderEntry entry, DateTimeOffset until)
    {
        await _store.ChangeAsync(null, (lists, self, now) => TaskReminderActions.Snooze(lists, entry, until, now, self).Lists);
        Refresh();
    }

    public async Task DismissAsync(TaskReminderEntry entry)
    {
        await _store.ChangeAsync(null, (lists, self, now) => TaskReminderActions.Dismiss(lists, entry, now, self).Lists);
        Refresh();
    }

    /// <summary>
    /// "Start a session" / "Work on it now": the reminder counts as started and the task goes into This session through the session's
    /// own add (its cap holds: a full session raises <see cref="SessionFull"/> and leaves the task where it is). A session isn't
    /// started or changed here: the normal start path is the user's.
    /// </summary>
    public async Task StartAsync(TaskReminderEntry entry)
    {
        await _store.ChangeAsync(null, (lists, self, now) => TaskReminderActions.Start(lists, entry, now, self).Lists);
        if (Actions is not null)
        {
            TaskMoveOutcome outcome = await Actions.MoveToSessionAsync([entry.TaskId]);
            if (outcome.NeedsRoom.Count > 0)
            {
                SessionFull?.Invoke(this, entry.TaskId);
            }
        }

        Refresh();
    }

    /// <summary>The ring time ran out with nobody acting: it is not rung again (and stays on the task as it is).</summary>
    public void RingTimedOut(TaskReminderEntry entry)
    {
        DateTimeOffset now = Now;
        _missed[entry.Key] = now;
        foreach (string key in _missed.Where(pair => now - pair.Value > MissedKept).Select(pair => pair.Key).ToList())
        {
            _missed.Remove(key);
        }

        try
        {
            Task.Run(() => _documents.SaveAsync(MissedKey, _missed)).GetAwaiter().GetResult();
        }
        catch (SqliteException)
        {
            // Rung again after a restart at worst.
        }

        Refresh();
    }

    private void Schedule(DateTimeOffset now)
    {
        TimeSpan wait = MaxWait;
        if (TaskReminderQueue.NextRingAt(_entries, now) is DateTimeOffset next)
        {
            wait = TimeSpan.FromTicks(Math.Min(MaxWait.Ticks, Math.Max(MinWait.Ticks, (next - now).Ticks)));
        }

        _timer.Stop();
        _timer.Interval = wait;
        _timer.Start();
    }
}
