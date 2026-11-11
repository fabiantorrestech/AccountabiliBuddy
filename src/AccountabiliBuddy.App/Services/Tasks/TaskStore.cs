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
/// "tasks.views", "tasks.log"), with two device-local ones: the views' search words ("tasks.viewSearches") and the ids the PC's
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
    public const string ViewSearchesKey = "tasks.viewSearches";
    public const string CategoryIdsKey = "tasks.categoryIds";
    public const string MarkerKey = "tasks.model";

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

    /// <summary>How many actions This session takes when a list adds to it (the Tasks setting, ADR 76). Set by the app.</summary>
    public Func<int> SessionActions { get; set; } = () => Core.Tasks.SessionActions.DefaultCap;

    /// <summary>The running session's checklist; null while none runs. Set by the app (<see cref="SessionBin"/>).</summary>
    public Func<IReadOnlyList<SessionTask>?> RunningChecklist { get; set; } = () => null;

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

    private Guid Self => SelfId() ?? Guid.Empty;

    private DateTimeOffset Now => _time.GetUtcNow();

    private static TimeZoneInfo Zone => TimeZoneInfo.Local;

    /// <summary>Whether the migration ran (its marker is stored).</summary>
    public bool HasMarkerBlocking()
    {
        return Task.Run(() => _store.LoadAsync<Marker>(MarkerKey)).GetAwaiter().GetResult() is { Model: >= ModelVersion };
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
                LoadList<SyncTaskLogItem>(LogKey).Where(item => item.Entry?.Task is not null).ToList());
            _categoryIds = LoadSealed<Dictionary<Guid, Guid>>(CategoryIdsKey) ?? [];
            _viewSearches = LoadSealed<Dictionary<Guid, string>>(ViewSearchesKey) ?? [];
            _running = LoadRunning();
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
        lists = TaskListRules.Normalize(lists, Self, NumberingDevice(), Now);
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
        TaskLists withHeld = WithHeldBack(_lists, LiveIds(changed), out bool heldTaken);
        TaskLists applied = TaskListRules.ApplyLocal(withHeld, changed, Self, Now, Zone, SessionActions());
        Task save = Commit(applied, source, fromOtherDevice: false).Save;
        return heldTaken ? Task.WhenAll(save, SaveRunningAsync()) : save;
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
    /// The session that starts takes these tasks (<paramref name="ids"/>): their copies as they are now are kept on this PC (sealed),
    /// before they leave the shared list, so whatever goes back to a list later comes back whole. Replaces what an earlier session held.
    /// </summary>
    public Task HoldRunningAsync(IEnumerable<Guid> ids)
    {
        if (!IsActive)
        {
            return Task.CompletedTask;
        }

        var wanted = ids.ToHashSet();
        _running = _lists.Tasks.Where(item => item.Task.IsLive && wanted.Contains(item.Id))
            .GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
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
        return SaveRunningAsync();
    }

    /// <summary>The focused steps of the held tasks (This session's focus as it was planned), for the session that starts.</summary>
    public IReadOnlyList<Guid> HeldFocus()
    {
        return _running.Values.SelectMany(item => (item.Task.Steps ?? []).Where(step => step.Focus && !step.IsDone).Select(step => step.Id)).ToList();
    }

    /// <summary>The session ended: nothing is held any more (what it had stays in its history).</summary>
    public Task ReleaseRunningAsync()
    {
        if (_running.Count == 0)
        {
            return Task.CompletedTask;
        }

        _running = [];
        return SaveRunningAsync();
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
            backup.Log.Where(item => item.Entry?.Task is not null).ToList());
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

    /// <summary>
    /// The running session's tasks as the This session bin shows them while it runs (ADR 76): the checklist stays the PC's own
    /// (<see cref="SessionChecklist"/>), so this is a view of it, never stored or synced as tasks. Empty while no session runs.
    /// </summary>
    public IReadOnlyList<TaskItem> SessionBin()
    {
        IReadOnlyList<SessionTask>? running = RunningChecklist();
        if (running is null)
        {
            return [];
        }

        List<TaskLabel> labels = _lists.Labels.Select(item => item.Label).ToList();
        Dictionary<Guid, TaskItem> held = _lists.Tasks.Where(item => item.Task.IsLive).ToDictionary(item => item.Id, item => item.Task);
        DateTimeOffset now = Now;
        return running.Select((task, index) =>
        {
            TaskItem target = held.TryGetValue(task.Id, out TaskItem? own) ? own : new TaskItem(task.Id, task.Title, TaskBin.Session, index + 1, now, now, now);
            return TaskSessionMapper.Apply(target with { Bin = TaskBin.Session, Position = index + 1 }, task, labels);
        }).ToList();
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
        TaskLists normalized = TaskListRules.Normalize(changed, Self, NumberingDevice(), Now);
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

    private static HashSet<Guid> LiveIds(OldTaskFields fields)
    {
        return fields.NextTasks.Where(item => item.DeletedAt is null).Select(item => item.Id)
            .Concat(fields.Later.Where(item => item.DeletedAt is null).Select(item => item.Id))
            .Concat(fields.Parked.Where(item => item.DeletedAt is null).Select(item => item.Id))
            .Concat(fields.AutoSaved.Where(item => item.DeletedAt is null).Select(item => item.Id))
            .ToHashSet();
    }

    private Dictionary<Guid, SyncTaskItem> LoadRunning()
    {
        try
        {
            return (LoadSealed<List<SyncTaskItem>>(RunningKey) ?? []).Where(item => item.Task is not null)
                .GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
        }
        catch (Exception ex) when (ex is JsonException or FormatException or NotSupportedException or System.Security.Cryptography.CryptographicException)
        {
            // Unreadable: the running session's tasks come back as new tasks if they go to a list; nothing else depends on it.
            return [];
        }
    }

    private Task SaveRunningAsync()
    {
        List<SyncTaskItem> held = _running.Values.ToList();
        return _store.SaveAsync(RunningKey, Sealed(held));
    }

    private Stored Sealed<T>(T value)
    {
        return new Stored(Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(value, SyncJson.Options))));
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
