using System.Globalization;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// ADR 94 on this PC: session notes become To sort tasks (<see cref="SessionNoteTasks"/>). Runs once at start (the first time behind a
/// database backup and the marker document <see cref="MarkerKey"/>; later starts only pick up notes kept while the task list couldn't be
/// used), after every received file or backup that brings live notes, and makes each new Focus-mode line a task directly. Always the tasks
/// first, then the notes' deletion (an ordinary synced tombstone); while the task list isn't open the notes are kept. The conversion never asks
/// for a sync write by itself (<see cref="TaskStoreChangedEventArgs.WritesNothing"/>, <see cref="SessionNotesStore.DeleteConvertedAsync"/>):
/// the tasks and tombstones leave with the next normal write. The log (<see cref="TaskModelMigration.LogFileName"/>) holds counts only, never
/// text. Use from the UI thread only.
/// </summary>
public sealed class SessionNoteConversion
{
    /// <summary>Device-local marker: the first conversion ran (after its backup). Never synced, in no backup.</summary>
    public const string MarkerKey = "tasks.notesConverted";

    private const int MarkerVersion = 1;

    private readonly TaskStore _tasks;
    private readonly SessionNotesStore _notes;
    private readonly SqliteJsonDocumentStore _documents;
    private readonly TimeProvider _time;
    private readonly string _logPath;

    // Set once the first, backed-up conversion is known to have run (its marker written or found): until then nothing converts outside
    // RunAtStartBlocking, so no note becomes a task without the backup before it (notes and new lines are kept as notes until a later start).
    private bool _backedUp;

    public SessionNoteConversion(TaskStore tasks, SessionNotesStore notes, SqliteJsonDocumentStore documents, TimeProvider time, string dataDirectory)
    {
        _tasks = tasks;
        _notes = notes;
        _documents = documents;
        _time = time;
        _logPath = Path.Combine(dataDirectory, TaskModelMigration.LogFileName);
    }

    /// <summary>
    /// At start, once the task list is open: the first time a whole-database backup ("session-notes") is written before any note is changed
    /// and the marker after the conversion; later starts convert only the notes kept since (no backup). Anything that goes wrong keeps the notes
    /// and writes no marker (the next start tries again).
    /// </summary>
    public void RunAtStartBlocking(LocalDatabase database)
    {
        if (!_tasks.IsActive)
        {
            Log("task list not open; session notes kept");
            return;
        }

        try
        {
            bool marked = Task.Run(() => _documents.LoadAsync<Marker>(MarkerKey)).GetAwaiter().GetResult() is { Version: >= MarkerVersion };
            _backedUp = marked;
            // The backup comes before anything is written: the plan stamps notes stored before ADR 87 (and saves them), so it waits for it.
            if (!marked && _notes.Records.Any(note => note.DeletedAt is null && !string.IsNullOrWhiteSpace(note.Text)))
            {
                string backup = Task.Run(() => database.BackupAsync("session-notes")).GetAwaiter().GetResult();
                Log("backup written: " + Path.GetFileName(backup));
            }

            SessionNoteTaskPlan plan = PlanNow();

            if (!plan.IsEmpty)
            {
                // The tasks first; only once they are saved do the notes go. Both saves run off the UI thread, so waiting for them here can't block them.
                // The stores change here, on the UI thread; only their saves are waited for.
                Task saved = _tasks.AddFromNotes(plan, quiet: true);
                Task.Run(() => saved).GetAwaiter().GetResult();
                Task deleted = _notes.DeleteConvertedAsync(plan.Tombstone);
                Task.Run(() => deleted).GetAwaiter().GetResult();
                Log(Describe(plan, "start"));
            }

            if (!marked)
            {
                Task.Run(() => _documents.SaveAsync(MarkerKey, new Marker(MarkerVersion))).GetAwaiter().GetResult();
                _backedUp = true;
                Log("marker written");
            }
        }
        catch (Exception ex)
        {
            // Never the message: it could quote a stored value. The notes stay; the next start tries again.
            Log("session notes not converted (" + ex.GetType().Name + "); notes kept");
        }
    }

    /// <summary>
    /// After a received file or a backup that brought live notes (<c>TaskListSync</c>, a backup's restore): converts every live note
    /// held. Quiet: nothing here asks for a sync write.
    /// </summary>
    public async Task ConvertAsync()
    {
        // No backed-up first run yet (its backup or marker failed): the notes are kept until a later start converts them after its backup.
        if (!_tasks.IsActive || !_backedUp)
        {
            return;
        }

        SessionNoteTaskPlan plan = PlanNow();
        if (plan.IsEmpty)
        {
            return;
        }

        try
        {
            await _tasks.AddFromNotes(plan, quiet: true);
        }
        catch (Exception ex)
        {
            Log("session notes not converted (" + ex.GetType().Name + "); notes kept");
            return;
        }

        await _notes.DeleteConvertedAsync(plan.Tombstone);
        Log(Describe(plan, "received"));
    }

    /// <summary>
    /// A new line typed in Focus mode (ADR 94): a To sort task made in the same shape as a converted note (this PC as its writer, now, the running
    /// session), and no note is written. A user's own change, so it goes out at once like any task edit. While the task list can't be used the
    /// line is kept as a note (converted once it can), as it is until the first, backed-up conversion has run.
    /// </summary>
    public Task AddLineAsync(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return Task.CompletedTask;
        }

        if (!_tasks.IsActive || !_backedUp)
        {
            return _notes.AddAsync(trimmed);
        }

        DateTimeOffset now = _time.GetUtcNow();
        (DateTimeOffset Start, Guid? ProfileId)? session = _notes.CurrentSession();
        var line = new SyncSessionNote(Guid.NewGuid(), trimmed, now, session?.Start, session?.ProfileId, _notes.Self() ?? Guid.Empty, now);
        SessionNoteTaskPlan plan = SessionNoteTasks.Plan([line], _tasks.Lists, TimeZoneInfo.Local, _tasks.SelfDevice, _tasks.IsHeld);
        return _tasks.AddFromNotes(plan, quiet: false);
    }

    /// <summary>
    /// What a session of this PC jotted, for its review card and History (ADR 94): the To sort tasks made from this PC's notes of that session,
    /// after any note still kept (the task list not open). Never another device's notes; never logged.
    /// </summary>
    public IReadOnlyList<string> JottedBetween(DateTimeOffset from, DateTimeOffset to)
    {
        IReadOnlyList<string> kept = _notes.Between(from, to);
        if (!_tasks.IsActive)
        {
            return kept;
        }

        List<string> tasks = SessionNoteTasks.Between(_tasks.Lists, _tasks.SelfDevice, from, to);
        return kept.Count == 0 ? tasks : kept.Concat(tasks).ToList();
    }

    private SessionNoteTaskPlan PlanNow()
    {
        return SessionNoteTasks.Plan(_notes.ForConversion(), _tasks.Lists, TimeZoneInfo.Local, _tasks.SelfDevice, _tasks.IsHeld);
    }

    private static string Describe(SessionNoteTaskPlan plan, string when)
    {
        return $"session notes ({when}): tasks added={plan.Add.Count} notes deleted={plan.Tombstone.Count} already tasks={plan.Tombstone.Count - plan.Add.Count}";
    }

    private void Log(string line)
    {
        try
        {
            File.AppendAllText(_logPath, _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture) + "  " + line + Environment.NewLine);
        }
        catch (IOException)
        {
            // The log is a help, never a reason to stop.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record Marker(int Version);
}
