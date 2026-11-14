using System.Security.Cryptography;
using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using AccountabiliBuddy.Core.Tasks;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// The one shared task list on this PC (ADR 76): tasks, labels, saved views and History, the only place they are stored once the
/// startup migration ran (marker document "tasks.model"). Kept as DPAPI-encrypted documents ("tasks.items", "tasks.labels",
/// "tasks.views", "tasks.log", and the account's Tasks settings in "tasks.account"), with two device-local ones: the views' search words ("tasks.viewSearches") and the ids the PC's
/// categories had ("tasks.categoryIds"). The PC's old lists (the next session's list, Later, come-backs, Auto-saved, categories)
/// are views of it (<see cref="TaskFacadeLists"/>) and send their changes here; sync files merge in here and this PC's file is
/// written from here (<see cref="TaskListRules"/>). Titles, notes, steps, tags and reasons are private text: sealed at rest and in
/// the sync files, never logged. All times come from the trusted clock. Use from the UI thread only.
/// </summary>
public sealed class TaskStore
{
    public const string ItemsKey = "tasks.items";
    public const string LabelsKey = "tasks.labels";
    public const string ViewsKey = "tasks.views";
    public const string LogKey = "tasks.log";

    /// <summary>ADR 89: the device-only tags (each with the devices that show it), sealed; synced and backed up with the lists.</summary>
    public const string TagScopesKey = "tasks.tagScopes";

    /// <summary>The account's Tasks settings (default view, picked calendar), sealed; synced and backed up with the lists.</summary>
    public const string AccountKey = "tasks.account";
    public const string ViewSearchesKey = "tasks.viewSearches";
    public const string CategoryIdsKey = "tasks.categoryIds";
    public const string MarkerKey = "tasks.model";

    /// <summary>Stored when a migration attempt of a model version made its database backup, so a failing one backs up once, not on every start.</summary>
    public const string AttemptKey = "tasks.modelAttempt";

    /// <summary>The running session's tasks as they were when it started (device-local state, sealed; see <see cref="HoldRunningAsync"/>).</summary>
    public const string RunningKey = "tasks.running";

    /// <summary>The model the marker names; a later model change migrates again from this one.</summary>
    public const int ModelVersion = 1;

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private TaskLists _lists = TaskLists.Empty;
    private Dictionary<Guid, Guid> _categoryIds = [];
    private Dictionary<Guid, string> _viewSearches = [];

    // The running session's tasks as they were in the list when it started (they leave the shared list then, Q32): a task that goes
    // back to a list during or after the session comes back from here with its number, reminders and everything the checklist can't hold.
    private Dictionary<Guid, SyncTaskItem> _running = [];

    // When the session that held each copy ended (none while it runs): a copy is kept until the session review is done and at least
    // HeldAfterSession has passed, so a task sent back after the session (review, back to Today) still keeps its number.
    private Dictionary<Guid, DateTimeOffset> _heldEndedAt = [];

    /// <summary>How long a held copy stays after its session ended (longer while that session's review is still waiting).</summary>
    public static readonly TimeSpan HeldAfterSession = TimeSpan.FromDays(1);

    // Saves run one after another, so an older list never overwrites a newer one.
    private Task _saving = Task.CompletedTask;

    public TaskStore(SqliteJsonDocumentStore store, IDataProtector protector, TimeProvider time)
    {
        _store = store;
        _protector = protector;
        _time = time;
    }

    /// <summary>This device's id in its sync group; null while it isn't in one. Set by the app.</summary>
    public Func<Guid?> SelfId { get; set; } = () => null;

    /// <summary>The other devices of the group (id and kind), for the numbering device. Set by the app.</summary>
    public Func<IEnumerable<(Guid DeviceId, string Kind)>> OtherDevices { get; set; } = () => [];

    /// <summary>True while the last session's review card is still waiting (held copies stay until it is done). Set by the app.</summary>
    public Func<bool> ReviewPending { get; set; } = () => false;

    /// <summary>
    /// How many actions the next session's This session bin takes when a list adds to it (the Tasks setting, ADR 76; the bin is always
    /// the next session's draft, the running checklist keeps its own cap). Set by the app.
    /// </summary>
    public Func<int> SessionActions { get; set; } = () => Core.Tasks.SessionActions.DefaultCap;

    /// <summary>The task list is the source (the migration ran and its documents read).</summary>
    public bool IsActive { get; private set; }

    public TaskLists Lists => _lists;

    /// <summary>Label id to the id its category had on this PC.</summary>
    public IReadOnlyDictionary<Guid, Guid> CategoryIds => _categoryIds;

    /// <summary>Old saved filters' search words by view id: this PC only, never synced.</summary>
    public IReadOnlyDictionary<Guid, string> ViewSearches => _viewSearches;

    /// <summary>The PC's task categories: the labels, with their ids here.</summary>
    public IReadOnlyList<TaskCategory> Categories => TaskFacadeLists.Categories(_lists.Labels, _categoryIds);

    public event EventHandler<TaskStoreChangedEventArgs>? Changed;

    /// <summary>A toast for the Tasks pages (their toast shows it): an old list's delete was ignored because another device moved the task.</summary>
    public event EventHandler<TaskToastEventArgs>? Notice;

    private Guid Self => SelfId() ?? Guid.Empty;

    private DateTimeOffset Now => _time.GetUtcNow();

    private static TimeZoneInfo Zone => TimeZoneInfo.Local;

    /// <summary>Whether the migration ran (its marker is stored).</summary>
    public bool HasMarkerBlocking()
    {
        return Task.Run(() => _store.LoadAsync<Marker>(MarkerKey)).GetAwaiter().GetResult() is { Model: >= ModelVersion };
    }

    /// <summary>
    /// A restored backup's category ids or view searches (<see cref="CategoryIdsKey"/>, <see cref="ViewSearchesKey"/>): written, then
    /// read back into the store's own copy so its next save doesn't put the old one back. A document that can't be read leaves the
    /// current one in place. Returns no note.
    /// </summary>
    public async Task<string?> RestoreDeviceMapAsync(string key, JsonElement value)
    {
        await _saving;
        await _store.SaveAsync(key, value);
        if (!IsActive)
        {
            return null;
        }

        try
        {
            if (key == CategoryIdsKey)
            {
                _categoryIds = LoadSealed<Dictionary<Guid, Guid>>(CategoryIdsKey) ?? [];
            }
            else if (key == ViewSearchesKey)
            {
                _viewSearches = LoadSealed<Dictionary<Guid, string>>(ViewSearchesKey) ?? [];
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException)
        {
            // Not readable here (made on another PC): keep what is in memory and write it back over the file.
            await Save();
        }

        return null;
    }

    /// <summary>Whether this model version's migration attempt already wrote its backup.</summary>
    public bool HasAttemptedBlocking()
    {
        return Task.Run(() => _store.LoadAsync<Marker>(AttemptKey)).GetAwaiter().GetResult() is { Model: >= ModelVersion };
    }

    /// <summary>Records that this model version's migration attempt wrote its backup.</summary>
    public void RecordAttemptBlocking()
    {
        Task.Run(() => _store.SaveAsync(AttemptKey, new Marker(ModelVersion))).GetAwaiter().GetResult();
    }

    /// <summary>Reads the task list after a migration; false (and not active) when a document can't be read.</summary>
    public bool LoadBlocking()
    {
        try
        {
            var lists = new TaskLists(
                LoadList<SyncTaskItem>(ItemsKey).Where(item => item.Task is not null).ToList(),
                LoadList<SyncTaskLabel>(LabelsKey).Where(item => item.Label is not null).ToList(),
                LoadList<SyncTaskView>(ViewsKey).Where(item => item.View is not null).ToList(),
                LoadList<SyncTaskLogItem>(LogKey).Where(item => item.Entry?.Task is not null).ToList(),
                LoadAccount(),
                LoadTagScopes());
            _categoryIds = LoadSealed<Dictionary<Guid, Guid>>(CategoryIdsKey) ?? [];
            _viewSearches = LoadSealed<Dictionary<Guid, string>>(ViewSearchesKey) ?? [];
            (_running, _heldEndedAt) = LoadRunning();
            _lists = lists;
            IsActive = true;
            return true;
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException or NotSupportedException or System.Security.Cryptography.CryptographicException)
        {
            IsActive = false;
            return false;
        }
    }

    /// <summary>
    /// Makes the migration's result the task list: its documents and the marker are written in one transaction, so a run that stops
    /// halfway leaves no marker and the next start migrates again from the old documents (kept as they are).
    /// </summary>
    public void ActivateBlocking(TaskMigrationResult result, IReadOnlyDictionary<Guid, Guid> categoryIds)
    {
        var lists = TaskLists.From(result);
        lists = TaskListRules.Normalize(lists, Self, NumberingDevice(), Now, Zone);
        var searches = result.ViewSearches.ToDictionary(pair => pair.Key, pair => pair.Value);
        var ids = categoryIds.ToDictionary(pair => pair.Key, pair => pair.Value);
        // The documents and the marker in one transaction: a run that stops halfway leaves neither.
        Task.Run(() => SaveDocumentsAsync(lists, ids, searches, withMarker: true)).GetAwaiter().GetResult();
        _lists = lists;
        _categoryIds = ids;
        _viewSearches = searches;
        IsActive = true;
    }

    /// <summary>
    /// Changes one of the PC's old lists made (<paramref name="changed"/>: the changed items only, tombstones for what went), from
    /// <paramref name="source"/>. Applied at once; the returned task is the save.
    /// </summary>
    public Task ApplyLocal(object source, OldTaskFields changed)
    {
        if (!IsActive || changed.NextTasks.Count + changed.Later.Count + changed.Parked.Count + changed.AutoSaved.Count == 0)
        {
            return Task.CompletedTask;
        }

        // A running session's task that goes to a list (Move to Later, Park) comes back with what it had, under its own id.
        foreach (TaskItem stale in TaskListRules.StaleDeletes(changed, _lists.Tasks, Now))
        {
            Notice?.Invoke(this, new TaskToastEventArgs(TaskToasts.MovedElsewhere(stale), canUndo: false, explains: true));
        }

        TaskLists withHeld = WithHeldBack(_lists, LiveIds(changed), out bool heldTaken);
        TaskLists applied = TaskListRules.ApplyLocal(withHeld, changed, Self, Now, Zone, SessionActions());
        // A delete from one of the old lists is filed in History too (ADR 76), except a running session's tasks leaving the list.
        applied = TaskHistoryRules.WithDeletions(withHeld, applied, DeletedIds(changed), Now);
        Task save = Commit(applied, source, fromOtherDevice: false).Save;
        return heldTaken ? Task.WhenAll(save, SaveRunningAsync()) : save;
    }

    /// <summary>
    /// A change the user's task actions make (<see cref="TaskActionsService"/>, the views, the tidy-up): <paramref name="change"/> gets the
    /// lists, this device's id and the time and returns the new lists, which are normalized, saved and shown by every view
    /// (<paramref name="source"/> null: all of them, the sender too).
    /// </summary>
    /// <returns>Whether the lists changed.</returns>
    public async Task<bool> ChangeAsync(object? source, Func<TaskLists, Guid, DateTimeOffset, TaskLists> change)
    {
        if (!IsActive)
        {
            return false;
        }

        (bool changed, Task save) = Commit(change(_lists, Self, Now), source, fromOtherDevice: false);
        await save;
        return changed;
    }

    /// <summary>The search words of a saved view (private, this PC only); blank removes them.</summary>
    public Task SetViewSearchAsync(Guid viewId, string? search)
    {
        if (!IsActive)
        {
            return Task.CompletedTask;
        }

        string text = (search ?? string.Empty).Trim();
        if (text.Length == 0 ? !_viewSearches.Remove(viewId) : _viewSearches.TryGetValue(viewId, out string? held) && held == text)
        {
            return _saving;
        }

        if (text.Length > 0)
        {
            _viewSearches[viewId] = text;
        }

        return Save();
    }

    /// <summary>
    /// Tasks put in the list where <paramref name="wanted"/> says (ADR 76; <see cref="TaskListRules.Place"/>): a move keeps the task's
    /// id and number; a running session's task comes back from what it had when the session started. Saved, and the views told
    /// (<paramref name="source"/> null: every view reads it again, its sender too).
    /// </summary>
    public Task PlaceAsync(object? source, IReadOnlyList<TaskItem> wanted)
    {
        if (!IsActive || wanted.Count == 0)
        {
            return Task.CompletedTask;
        }

        TaskLists placed = TaskListRules.Place(_lists, wanted, _running, Self, Now);
        bool heldTaken = false;
        foreach (TaskItem task in wanted)
        {
            heldTaken |= _running.Remove(task.Id);
        }

        Task save = Commit(placed, source, fromOtherDevice: false).Save;
        return heldTaken ? Task.WhenAll(save, SaveRunningAsync()) : save;
    }

    /// <summary>
    /// Running checklist tasks go back to the list in <paramref name="bin"/> (Queued after a swap or the pick), each at the end of it,
    /// with their number and everything they had when the session started (<see cref="TaskListRules.FromChecklist"/>).
    /// </summary>
    public Task PutBackAsync(object? source, IReadOnlyList<SessionTask> tasks, TaskBin bin)
    {
        if (!IsActive || tasks.Count == 0)
        {
            return Task.CompletedTask;
        }

        List<TaskLabel> labels = _lists.Labels.Select(item => item.Label).ToList();
        double position = TaskListRules.EndOf(_lists, bin);
        DateTimeOffset now = Now;
        var wanted = tasks.Select((task, index) =>
        {
            TaskItem? held = _running.TryGetValue(task.Id, out SyncTaskItem? kept) ? kept.Task
                : _lists.Tasks.FirstOrDefault(item => item.Id == task.Id && item.Task.IsLive)?.Task;
            return TaskListRules.FromChecklist(task, held, bin, position + index, labels, now);
        }).ToList();
        return PlaceAsync(source, wanted);
    }

    /// <summary>
    /// The reverse of <see cref="PutBackAsync"/> for one task the running checklist keeps after all (ticked there meanwhile): its list copy
    /// is held with the session's again and leaves the list (a tombstone, no History entry: the checklist has it).
    /// </summary>
    public Task TakeBackAsync(object? source, Guid id)
    {
        if (!IsActive || _lists.Tasks.FirstOrDefault(item => item.Id == id && item.Task.IsLive) is not SyncTaskItem item)
        {
            return Task.CompletedTask;
        }

        _running[id] = item;
        _heldEndedAt.Remove(id);
        DateTimeOffset at = SharedListMerge.NextStamp(Now, item);
        TaskLists taken = _lists with { Tasks = _lists.Tasks.Select(other => other.Id == id ? other.Deleted(at) : other).ToList() };
        return Task.WhenAll(Commit(taken, source, fromOtherDevice: false).Save, SaveRunningAsync());
    }

    /// <summary>
    /// The session that starts takes these tasks (<paramref name="ids"/>): their copies as they are now are kept on this PC (sealed),
    /// before they leave the shared list, so whatever goes back to a list later comes back whole. Added to what is held already (a
    /// start run again after a restart finds the tasks gone from the list and keeps the copies it made the first time).
    /// </summary>
    public Task HoldRunningAsync(IEnumerable<Guid> ids)
    {
        if (!IsActive)
        {
            return Task.CompletedTask;
        }

        var wanted = ids.ToHashSet();
        foreach (SyncTaskItem item in _lists.Tasks.Where(item => item.Task.IsLive && wanted.Contains(item.Id)))
        {
            _running[item.Id] = item;
            _heldEndedAt.Remove(item.Id);
        }

        PruneHeld();
        return SaveRunningAsync();
    }

    /// <summary>
    /// A task joins the running session from a list (Later, To sort): its copy as it is now is kept with the session's others before
    /// it leaves the list, so it can come back whole.
    /// </summary>
    public Task AlsoHoldAsync(Guid id)
    {
        if (!IsActive || _lists.Tasks.FirstOrDefault(item => item.Id == id && item.Task.IsLive) is not SyncTaskItem item)
        {
            return Task.CompletedTask;
        }

        _running[id] = item;
        _heldEndedAt.Remove(id);
        return SaveRunningAsync();
    }

    /// <summary>Whether a copy of this task is held for a session (running, or ended and not yet let go): undo leaves such a task alone.</summary>
    public bool IsHeld(Guid id)
    {
        return _running.ContainsKey(id);
    }

    /// <summary>The focused steps of the held tasks of the session that runs (This session's focus as it was planned), for its start.</summary>
    public IReadOnlyList<Guid> HeldFocus()
    {
        return _running.Where(pair => !_heldEndedAt.ContainsKey(pair.Key))
            .SelectMany(pair => (pair.Value.Task.Steps ?? []).Where(step => step.Focus && !step.IsDone).Select(step => step.Id)).ToList();
    }

    /// <summary>
    /// The session ended: its held copies are kept until its review is done and at least <see cref="HeldAfterSession"/> has passed
    /// (a task sent back from its record then keeps its number, <see cref="CarryHeldAsync"/>), then they go.
    /// </summary>
    public Task ReleaseRunningAsync()
    {
        DateTimeOffset now = Now;
        bool changed = false;
        foreach (Guid id in _running.Keys.Where(id => !_heldEndedAt.ContainsKey(id)).ToList())
        {
            _heldEndedAt[id] = now;
            changed = true;
        }

        changed |= PruneHeld();
        return changed ? SaveRunningAsync() : Task.CompletedTask;
    }

    /// <summary>
    /// A task of an ended session's record went back to a list as a copy (<paramref name="toId"/>): it takes what the original
    /// (<paramref name="fromId"/>) had when its session started and the record can't hold (number, reminders, highlight, can't miss,
    /// due, calendar), and the held copy goes. Nothing changes when nothing is held or the copy isn't in the list.
    /// </summary>
    public Task CarryHeldAsync(Guid fromId, Guid toId)
    {
        if (!IsActive || !_running.TryGetValue(fromId, out SyncTaskItem? held)
            || _lists.Tasks.FirstOrDefault(item => item.Id == toId && item.Task.IsLive) is not SyncTaskItem copy)
        {
            return Task.CompletedTask;
        }

        TaskItem original = held.Task;
        TaskItem wanted = copy.Task with
        {
            Number = original.Number ?? copy.Task.Number,
            FormerNumbers = original.FormerNumbers,
            Reminders = original.Reminders,
            Highlight = original.Highlight,
            CantMiss = original.CantMiss,
            DueOn = original.DueOn,
            DueAt = original.DueAt,
            Calendar = original.Calendar,
        };
        _running.Remove(fromId);
        _heldEndedAt.Remove(fromId);
        TaskLists placed = TaskListRules.Place(_lists, [wanted], null, Self, Now);
        return Task.WhenAll(Commit(placed, null, fromOtherDevice: false).Save, SaveRunningAsync());
    }

    // Held copies of ended sessions past their time (and past their review) go; true when any went.
    private bool PruneHeld()
    {
        DateTimeOffset now = Now;
        bool reviewWaiting = ReviewPending();
        List<Guid> gone = _heldEndedAt.Where(pair => now - pair.Value >= HeldAfterSession && !reviewWaiting).Select(pair => pair.Key)
            .Concat(_heldEndedAt.Keys.Where(id => !_running.ContainsKey(id)))
            .Distinct().ToList();
        foreach (Guid id in gone)
        {
            _running.Remove(id);
            _heldEndedAt.Remove(id);
        }

        return gone.Count > 0;
    }

    /// <summary>The PC's category list as it now is (added, renamed, recoloured, reordered, taken off), from <paramref name="source"/>.</summary>
    public Task ApplyCategories(object source, IReadOnlyList<TaskCategory> categories)
    {
        if (!IsActive)
        {
            return Task.CompletedTask;
        }

        (TaskLists applied, Dictionary<Guid, Guid> ids) = TaskCategoryEdits.Apply(_lists, categories, _categoryIds, Self, Now);
        bool idsChanged = ids.Count != _categoryIds.Count || ids.Any(pair => !_categoryIds.TryGetValue(pair.Key, out Guid held) || held != pair.Value);
        _categoryIds = ids;
        return Commit(applied, source, fromOtherDevice: false, forceSave: idsChanged).Save;
    }

    /// <summary>Another device's file read in (its task parts, or its old fields); true when the list changed.</summary>
    public async Task<bool> MergeFileAsync(SyncFile file)
    {
        if (!IsActive)
        {
            return false;
        }

        TaskLists? merged = TaskListRules.MergeFile(_lists, file, Self, Now, Zone, SessionActions());
        if (merged is null)
        {
            return false;
        }

        (bool changed, Task save) = Commit(merged, null, fromOtherDevice: true);
        await save;
        return changed;
    }

    /// <summary>This PC's file with the task list's parts, new and old (the old ones rebuilt here, never stored).</summary>
    public SyncFile WithParts(SyncFile file)
    {
        return IsActive ? TaskListRules.WithParts(file, _lists, Now, Zone) : file;
    }

    /// <summary>
    /// A backup's task list taken in (ADR 71): merged as another device's would be. With <paramref name="restore"/>, the backup's
    /// live tasks, labels and views are stamped now first, so they come back over later edits and deletions (nothing made since goes).
    /// </summary>
    /// <returns>How many live tasks the backup held.</returns>
    public async Task<int> ImportAsync(TaskLists backup, bool restore)
    {
        if (!IsActive)
        {
            return 0;
        }

        DateTimeOffset now = Now;
        Guid self = Self;
        TaskLists incoming = !restore ? backup : new TaskLists(
            backup.Tasks.Where(item => item.Task?.IsLive == true)
                .Select(item => new SyncTaskItem(item.Task with { EditedAt = now, TouchedAt = now }, TaskFieldStamps.AllAt(new TaskFieldStamp(now, self))))
                .ToList(),
            backup.Labels.Where(item => item.Label?.IsLive == true).Select(item => new SyncTaskLabel(item.Label with { EditedAt = now })).ToList(),
            backup.Views.Where(item => item.View?.IsLive == true).Select(item => new SyncTaskView(item.View with { EditedAt = now })).ToList(),
            backup.Log.Where(item => item.Entry?.Task is not null).ToList(),
            TagScopes: backup.TagScopes?.Where(scope => scope.DeletedAt is null).Select(scope => scope with { EditedAt = now }).ToList());
        await Commit(TaskListRules.MergeLists(_lists, incoming, self, now), null, fromOtherDevice: true).Save;
        return incoming.Tasks.Count(item => item.Task.IsLive);
    }

    /// <summary>An older backup's old lists taken in as edits by this PC (with <paramref name="restore"/>, stamped now first).</summary>
    public Task ImportOldAsync(OldTaskFields fields)
    {
        if (!IsActive)
        {
            return Task.CompletedTask;
        }

        TaskLists applied = TaskListRules.ApplyLocal(_lists, fields, Self, Now, Zone, SessionActions());
        return Commit(applied, null, fromOtherDevice: true).Save;
    }

    /// <summary>The numbering device of the group (ADR 76): this PC while it is in none.</summary>
    public Guid? NumberingDevice()
    {
        Guid self = Self;
        if (self == Guid.Empty)
        {
            return Guid.Empty;
        }

        return TaskNumbering.NumberingDevice(OtherDevices().Append((self, TaskNumbering.PcKind)));
    }

    // Normalizes and keeps the change; saves and tells the views only when something changed.
    private (bool Changed, Task Save) Commit(TaskLists changed, object? source, bool fromOtherDevice, bool forceSave = false)
    {
        TaskLists normalized = TaskListRules.Normalize(changed, Self, NumberingDevice(), Now, Zone);
        bool differs = normalized.Signature() != _lists.Signature();
        if (!differs && !forceSave)
        {
            return (false, _saving);
        }

        _lists = normalized;
        Task save = Save();
        if (differs)
        {
            Changed?.Invoke(this, new TaskStoreChangedEventArgs(source, fromOtherDevice));
        }

        return (differs, save);
    }

    private Task Save()
    {
        TaskLists lists = _lists;
        Dictionary<Guid, Guid> ids = new(_categoryIds);
        Dictionary<Guid, string> searches = new(_viewSearches);
        // After the saves asked for before it, whatever became of them; a failed save still fails the task it returns.
        _saving = _saving.ContinueWith(_ => SaveDocumentsAsync(lists, ids, searches), TaskScheduler.Default).Unwrap();
        return _saving;
    }

    // One transaction: the tasks, History and labels always agree (a tick moves a task into History, an untick back).
    private Task SaveDocumentsAsync(TaskLists lists, Dictionary<Guid, Guid> ids, Dictionary<Guid, string> searches, bool withMarker = false)
    {
        var documents = new List<KeyValuePair<string, object>>
        {
            new(ItemsKey, Sealed(lists.Tasks)),
            new(LabelsKey, Sealed(lists.Labels)),
            new(ViewsKey, Sealed(lists.Views)),
            new(LogKey, Sealed(lists.Log)),
            new(AccountKey, Sealed(lists.Account ?? SyncTaskAccountSettings.Empty)),
            new(TagScopesKey, Sealed(lists.TagScopes ?? [])),
            new(CategoryIdsKey, Sealed(ids)),
            new(ViewSearchesKey, Sealed(searches)),
        };
        if (withMarker)
        {
            documents.Add(new(MarkerKey, new Marker(ModelVersion)));
        }

        return _store.SaveAllAsync(documents);
    }

    // Tombstones of held tasks that a change brings back are replaced by the held copies first (the change is then an edit of them).
    private TaskLists WithHeldBack(TaskLists lists, IReadOnlySet<Guid> ids, out bool taken)
    {
        taken = false;
        if (_running.Count == 0 || ids.Count == 0)
        {
            return lists;
        }

        var tasks = lists.Tasks.ToList();
        for (int index = 0; index < tasks.Count; index++)
        {
            Guid id = tasks[index].Id;
            if (!tasks[index].Task.IsLive && ids.Contains(id) && _running.Remove(id, out SyncTaskItem? held))
            {
                tasks[index] = held;
                taken = true;
            }
        }

        return taken ? lists with { Tasks = tasks } : lists;
    }

    // The tasks the old lists deleted (not a running session's, which only leave the list while it runs).
    private HashSet<Guid> DeletedIds(OldTaskFields fields)
    {
        return fields.NextTasks.Where(item => item.DeletedAt is not null).Select(item => item.Id)
            .Concat(fields.Later.Where(item => item.DeletedAt is not null).Select(item => item.Id))
            .Concat(fields.Parked.Where(item => item.DeletedAt is not null).Select(item => item.Id))
            .Concat(fields.AutoSaved.Where(item => item.DeletedAt is not null).Select(item => item.Id))
            .Where(id => !_running.ContainsKey(id))
            .ToHashSet();
    }

    private static HashSet<Guid> LiveIds(OldTaskFields fields)
    {
        return fields.NextTasks.Where(item => item.DeletedAt is null).Select(item => item.Id)
            .Concat(fields.Later.Where(item => item.DeletedAt is null).Select(item => item.Id))
            .Concat(fields.Parked.Where(item => item.DeletedAt is null).Select(item => item.Id))
            .Concat(fields.AutoSaved.Where(item => item.DeletedAt is null).Select(item => item.Id))
            .ToHashSet();
    }

    private (Dictionary<Guid, SyncTaskItem> Items, Dictionary<Guid, DateTimeOffset> EndedAt) LoadRunning()
    {
        try
        {
            HeldDocument? held = LoadSealed<HeldDocument>(RunningKey);
            Dictionary<Guid, SyncTaskItem> items = (held?.Items ?? []).Where(item => item?.Task is not null)
                .GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
            return (items, held?.EndedAt is { } ended ? new Dictionary<Guid, DateTimeOffset>(ended) : []);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or NotSupportedException or System.Security.Cryptography.CryptographicException)
        {
            // Unreadable: the running session's tasks come back as new tasks if they go to a list; nothing else depends on it.
            return ([], []);
        }
    }

    // In turn with the other saves (as Save), so an older copy never lands after a newer one.
    private Task SaveRunningAsync()
    {
        var held = new HeldDocument(
            _running.Values.ToList(),
            _heldEndedAt.Where(pair => _running.ContainsKey(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
        Stored sealedHeld = Sealed(held);
        _saving = _saving.ContinueWith(_ => _store.SaveAsync(RunningKey, sealedHeld), TaskScheduler.Default).Unwrap();
        return _saving;
    }

    // The held copies and when each one's session ended (none while it runs).
    private sealed record HeldDocument(List<SyncTaskItem> Items, Dictionary<Guid, DateTimeOffset>? EndedAt);

    private Stored Sealed<T>(T value)
    {
        return new Stored(Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(value, SyncJson.Options))));
    }

    // Two settings that come back from the other devices: one that can't be read here starts empty rather than closing the task list.
    private SyncTaskAccountSettings? LoadAccount()
    {
        try
        {
            return TaskAccountSettingsMerge.Clean(LoadSealed<SyncTaskAccountSettings>(AccountKey));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or NotSupportedException or System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    // ADR 89's tag scopes: a document that can't be read here starts empty (every tag shown) rather than closing the task list; the other devices'
    // files bring the scopes back.
    private List<SyncTagScope>? LoadTagScopes()
    {
        try
        {
            return TagScopeMerge.Merge(TaskScopes.Clean(LoadList<SyncTagScope>(TagScopesKey)), [], Now);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or NotSupportedException or System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    private List<T> LoadList<T>(string key)
    {
        return LoadSealed<List<T>>(key) ?? [];
    }

    private T? LoadSealed<T>(string key)
        where T : class
    {
        Stored? stored = Task.Run(() => _store.LoadAsync<Stored>(key)).GetAwaiter().GetResult();
        if (stored is null)
        {
            return null;
        }

        string json = _protector.Unprotect(Convert.FromBase64String(stored.Data))
            ?? throw new FormatException("A task document couldn't be opened.");
        return JsonSerializer.Deserialize<T>(json, SyncJson.Options);
    }

    private sealed record Stored(string Data);

    private sealed record Marker(int Model);
}
