using AccountabiliBuddy.App.Services.Reminders;
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
/// session holds them). Round 2 R5.5: other reminder sources (<see cref="IReminderSource"/>, goal reminders) ring through the same
/// queue: their due occurrences follow the task list's in <see cref="Pending"/> (a task's reminder always comes first, so the task
/// reminders ring exactly as before), held by the same session rule, snoozed and dismissed through the same calls, which hand them
/// to their source. They never reach <see cref="Entries"/> (the "Reminders due today" list and the calendar export are task-only).
/// Use from the UI thread only.
/// </summary>
public sealed class TaskReminderService : IDisposable
{
    public const string MissedKey = "tasks.reminderMissed";

    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan MinWait = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MissedKept = TimeSpan.FromDays(3);

    private readonly TaskStore _store;
    private readonly TaskReminderSettingsService _settings;
    private readonly SqliteJsonDocumentStore _documents;
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _timer = new();
    private Dictionary<string, DateTimeOffset> _missed = [];
    private IReadOnlyList<TaskReminderEntry> _entries = [];

    // Round 2 R5.5: reminder sources other than the task list (one per origin), and their occurrences as last read.
    private readonly List<IReminderSource> _sources = [];
    private IReadOnlyList<TaskReminderEntry> _sourceEntries = [];

    // "Ring it now (test)" rings: shown like a due reminder, but nothing a test ring's buttons do is written to the task.
    private readonly List<TaskReminderEntry> _tests = [];

    // Settings' test rings ("Ring one", "Ring 6", "Can't miss"): made-up tasks that exist only while their test ring shows, never stored.
    private readonly Dictionary<Guid, TaskItem> _samples = [];

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

    /// <summary>A focus session is running (set by the app); with "During a focus session" on wait, normal reminders are held meanwhile.</summary>
    public Func<bool> SessionRunning { get; set; } = () => false;

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

    /// <summary>
    /// Round 2 R5.5: rings another source's reminders through this engine (goal reminders). Its due occurrences come after the task
    /// list's in <see cref="Pending"/>; a change it announces looks again at once.
    /// </summary>
    public void AddSource(IReminderSource source)
    {
        if (_sources.Any(known => known.Origin == source.Origin))
        {
            throw new InvalidOperationException($"A reminder source for {source.Origin} is already added.");
        }

        _sources.Add(source);
        source.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>The source of a reminder that isn't a task's; null for a task's.</summary>
    public IReminderSource? SourceOf(TaskReminderEntry entry)
    {
        return entry.Origin == TaskReminderOrigin.Task ? null : _sources.FirstOrDefault(source => source.Origin == entry.Origin);
    }

    /// <summary>The task's title, for showing (private text: never logged); null when the task is gone.</summary>
    public string? TitleOf(Guid taskId)
    {
        if (_samples.TryGetValue(taskId, out TaskItem? sample))
        {
            return sample.Title;
        }

        return _store.Lists.Tasks.FirstOrDefault(item => item.Id == taskId && item.Task.IsLive)?.Task.Title;
    }

    /// <summary>The task, for showing its chips and for Open; null when it is gone.</summary>
    public TaskItem? TaskOf(Guid taskId)
    {
        if (_samples.TryGetValue(taskId, out TaskItem? sample))
        {
            return sample;
        }

        TaskItem? task = _store.Lists.Tasks.FirstOrDefault(item => item.Id == taskId && item.Task.IsLive)?.Task;
        return task;
    }

    /// <summary>The task's label (name and colour, for the card's dot); null for "No label" or a label that is gone.</summary>
    public TaskLabel? LabelOf(Guid? labelId)
    {
        return labelId is Guid id
            ? _store.Lists.Labels.Select(item => item.Label).FirstOrDefault(label => label.IsLive && label.Id == id)
            : null;
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

        // Held reminders (a session runs, "Wait until the session ends") stay unhandled and are not Pending, so no ring time counts for
        // them; Refresh on the session's end (the app calls it) queues them in due order, also after a restart.
        List<TaskReminderEntry> due = TaskReminderQueue.Ringable(
            TaskReminderQueue.Due(_entries, now).Where(entry => !_missed.ContainsKey(entry.Key)), SessionRunning(), _settings.Current.DuringSession);
        // A test ring the user asked for shows first, also during a session; it leaves once acted on, or when its task is gone.
        _tests.RemoveAll(test => TaskOf(test.TaskId) is null);
        due.InsertRange(0, _tests);

        // Other sources' reminders (goal reminders) after every task reminder, by the same due and session rules; never Can't miss.
        var sourceEntries = new List<TaskReminderEntry>();
        foreach (IReminderSource source in _sources)
        {
            IReadOnlyList<TaskReminderEntry> fromSource = source.Entries(now);
            sourceEntries.AddRange(fromSource);
            due.AddRange(TaskReminderQueue.Ringable(TaskReminderQueue.Due(fromSource, now), SessionRunning(), _settings.Current.DuringSession));
        }

        _sourceEntries = sourceEntries;
        if (!due.Select(entry => entry.Key).SequenceEqual(Pending.Select(entry => entry.Key)))
        {
            Pending = due;
            PendingChanged?.Invoke(this, EventArgs.Empty);
        }

        Schedule(now);
    }

    /// <summary>
    /// "Ring it now (test)" in the Reminders &amp; due date menu: the task's reminder rings at once as it would (full screen when it is
    /// Can't miss), so the user hears and sees it. Dismiss, Snooze and Work on it now only close a test ring: nothing is written to the
    /// task and no session is touched.
    /// </summary>
    public void RingTest(Guid taskId)
    {
        if (TaskOf(taskId) is not TaskItem task)
        {
            return;
        }

        DateTimeOffset now = Now;
        _tests.RemoveAll(test => test.TaskId == taskId);
        _tests.Add(new TaskReminderEntry(taskId, Guid.NewGuid(), TaskReminder.AtStart, now, TaskReminderStatus.Up, now, 0, task.CantMiss));
        Refresh();
    }

    /// <summary>
    /// Settings' test rings (Alerts &amp; on-screen › Task reminders): made-up tasks ring at once, one after another in the order given,
    /// with the current settings (a Can't miss one full screen). Like <see cref="RingTest"/>, their buttons only close them: the tasks
    /// were never in the list, nothing is written and no session is touched. A new call replaces the samples still showing.
    /// </summary>
    public void RingSamples(IReadOnlyList<TaskItem> samples)
    {
        _tests.RemoveAll(test => _samples.ContainsKey(test.TaskId));
        _samples.Clear();
        DateTimeOffset now = Now;
        foreach (TaskItem sample in samples)
        {
            _samples[sample.Id] = sample;
            _tests.Add(new TaskReminderEntry(sample.Id, Guid.NewGuid(), TaskReminder.AtStart, now, TaskReminderStatus.Up, now, 0, sample.CantMiss));
        }

        Refresh();
    }

    /// <summary>Rings it again at <paramref name="until"/> (a snooze moment, see <see cref="TaskSnooze"/>).</summary>
    public async Task SnoozeAsync(TaskReminderEntry entry, DateTimeOffset until)
    {
        if (EndTest(entry))
        {
            return;
        }

        if (SourceOf(entry) is IReminderSource source)
        {
            await source.SnoozeAsync(entry, until);
            Refresh();
            return;
        }

        await _store.ChangeAsync(null, (lists, self, now) => TaskReminderActions.Snooze(lists, entry, until, now, self).Lists);
        Refresh();
    }

    public async Task DismissAsync(TaskReminderEntry entry)
    {
        if (EndTest(entry))
        {
            return;
        }

        if (SourceOf(entry) is IReminderSource source)
        {
            await source.DismissAsync(entry);
            Refresh();
            return;
        }

        await _store.ChangeAsync(null, (lists, self, now) => TaskReminderActions.Dismiss(lists, entry, now, self).Lists);
        Refresh();
    }

    /// <summary>
    /// "Start a session" / "Work on it now": the task goes into This session through the session's own add (its cap holds) and the
    /// reminder counts as started. A full session raises <see cref="SessionFull"/>, leaves the task where it is and the reminder up
    /// (it keeps ringing until the user acts on it; a swap in the dialog doesn't handle it). A session isn't started or changed here:
    /// the normal start path is the user's.
    /// </summary>
    /// <returns>True when the task is now in This session (false for a test ring, a full session, no task actions, or another source's reminder).</returns>
    public async Task<bool> StartAsync(TaskReminderEntry entry)
    {
        if (entry.Origin != TaskReminderOrigin.Task || EndTest(entry))
        {
            return false;
        }

        // Written before the move: in a running session the task the move takes in is held by the session (out of the list), so its
        // reminder could no longer be written. When it finds no room, the state it had is put back.
        TaskReminderOccurrence? before = OccurrenceOf(entry);
        await _store.ChangeAsync(null, (lists, self, now) => TaskReminderActions.Start(lists, entry, now, self).Lists);
        bool movedIn = false;
        if (Actions is not null)
        {
            TaskMoveOutcome outcome = await Actions.MoveToSessionAsync([entry.TaskId]);
            movedIn = outcome.NeedsRoom.Count == 0;
        }

        if (!movedIn)
        {
            await _store.ChangeAsync(null, (lists, self, now) => TaskReminderActions.Unstart(lists, entry, before, now, self).Lists);
        }

        Refresh();
        if (!movedIn && Actions is not null)
        {
            SessionFull?.Invoke(this, entry.TaskId);
        }

        return movedIn;
    }

    /// <summary>
    /// "Add to Queued" on the snoozed-3-times nudge: the task moves to Queued (the task actions' move, undoable like any move) and this
    /// reminder is handled as dismissed. A deliberate move, so it doesn't wait out dismiss protection (only Dismiss waits). Never for a
    /// Can't miss: only its own wait and hold dismiss it (the nudge isn't offered for one).
    /// </summary>
    public async Task QueueAsync(TaskReminderEntry entry)
    {
        if (entry.CantMiss || entry.Origin != TaskReminderOrigin.Task || EndTest(entry))
        {
            return;
        }

        if (Actions is not null)
        {
            await Actions.MoveAsync([entry.TaskId], TaskBin.Queued);
        }

        await _store.ChangeAsync(null, (lists, self, now) => TaskReminderActions.Dismiss(lists, entry, now, self).Lists);
        Refresh();
    }

    // The occurrence's state as the task holds it now (null: none written yet).
    private TaskReminderOccurrence? OccurrenceOf(TaskReminderEntry entry)
    {
        return TaskOf(entry.TaskId)?.Reminders?.FirstOrDefault(reminder => reminder.Id == entry.ReminderId)
            ?.Occurrences?.FirstOrDefault(occurrence => occurrence.Due == entry.Due);
    }

    /// <summary>The ring time ran out with nobody acting: it is not rung again (and stays on the task as it is).</summary>
    public void RingTimedOut(TaskReminderEntry entry)
    {
        if (EndTest(entry))
        {
            return;
        }

        if (SourceOf(entry) is IReminderSource source)
        {
            // Its source keeps it (not the task list's "tasks.reminderMissed"): at once in memory, saved after; the next one comes to the front.
            _ = source.RingTimedOutAsync(entry);
            Refresh();
            return;
        }

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

    // A test ring acted on (or rung out) just leaves; true when it was one.
    private bool EndTest(TaskReminderEntry entry)
    {
        if (_tests.RemoveAll(test => test.Key == entry.Key) == 0)
        {
            return false;
        }

        if (_tests.All(test => test.TaskId != entry.TaskId))
        {
            _samples.Remove(entry.TaskId);
        }

        Refresh();
        return true;
    }

    private void Schedule(DateTimeOffset now)
    {
        TimeSpan wait = MaxWait;
        if (TaskReminderQueue.NextRingAt(_entries.Concat(_sourceEntries), now) is DateTimeOffset next)
        {
            wait = TimeSpan.FromTicks(Math.Min(MaxWait.Ticks, Math.Max(MinWait.Ticks, (next - now).Ticks)));
        }

        _timer.Stop();
        _timer.Interval = wait;
        _timer.Start();
    }
}
