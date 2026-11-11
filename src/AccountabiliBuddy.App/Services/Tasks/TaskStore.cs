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

    /// <summary>The model the marker names; a later model change migrates again from this one.</summary>
    public const int ModelVersion = 1;

    /// <summary>The session's places for tasks a list adds to the next session (the three-task rule, until it counts actions).</summary>
    public const int SessionActions = SessionTasksService.MaxTasks;

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private TaskLists _lists = TaskLists.Empty;
    private Dictionary<Guid, Guid> _categoryIds = [];
    private Dictionary<Guid, string> _viewSearches = [];

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
            _lists = lists;
            IsActive = true;
            return true;
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException or NotSupportedException)
        {
            IsActive = false;
            return false;
        }
    }

    /// <summary>
    /// Makes the migration's result the task list: its documents are written first, the marker last, so a run that stops halfway
    /// leaves no marker and the next start migrates again from the old documents (kept as they are).
    /// </summary>
    public void ActivateBlocking(TaskMigrationResult result, IReadOnlyDictionary<Guid, Guid> categoryIds)
    {
        var lists = TaskLists.From(result);
        lists = TaskListRules.Normalize(lists, Self, NumberingDevice(), Now);
        var searches = result.ViewSearches.ToDictionary(pair => pair.Key, pair => pair.Value);
        var ids = categoryIds.ToDictionary(pair => pair.Key, pair => pair.Value);
        Task.Run(async () =>
        {
            await SaveDocumentsAsync(lists, ids, searches);
            await _store.SaveAsync(MarkerKey, new Marker(ModelVersion));
        }).GetAwaiter().GetResult();
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

        TaskLists applied = TaskListRules.ApplyLocal(_lists, changed, Self, Now, Zone, SessionActions);
        return Commit(applied, source, fromOtherDevice: false).Save;
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

        TaskLists? merged = TaskListRules.MergeFile(_lists, file, Self, Now, Zone, SessionActions);
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

        TaskLists applied = TaskListRules.ApplyLocal(_lists, fields, Self, Now, Zone, SessionActions);
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

    private async Task SaveDocumentsAsync(TaskLists lists, Dictionary<Guid, Guid> ids, Dictionary<Guid, string> searches)
    {
        await _store.SaveAsync(ItemsKey, Sealed(lists.Tasks));
        await _store.SaveAsync(LabelsKey, Sealed(lists.Labels));
        await _store.SaveAsync(ViewsKey, Sealed(lists.Views));
        await _store.SaveAsync(LogKey, Sealed(lists.Log));
        await _store.SaveAsync(CategoryIdsKey, Sealed(ids));
        await _store.SaveAsync(ViewSearchesKey, Sealed(searches));
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
