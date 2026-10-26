using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Guided sessions (ADR 24, 25): the checklist of the next or running session. Tasks added while no session runs are a
/// draft (the quick-session popup and the Session tab share it); starting a session makes it that session's checklist,
/// which survives a restart; ending the session keeps it in history (90 days) and starts an empty draft. Tasks can be in
/// categories (named, coloured; the checklist is grouped under them, no category last). Stored DPAPI-encrypted
/// ("session.tasks", "session.taskHistory", "session.categories"), like notes on added sessions. Use from the UI thread only.
/// </summary>
public sealed class SessionTasksService : IDisposable
{
    public const string AllDoneReason = "All tasks done";

    private const string DocumentKey = "session.tasks";
    private const string HistoryKey = "session.taskHistory";
    private const string CategoriesKey = "session.categories";
    private static readonly TimeSpan HistoryKept = TimeSpan.FromDays(90);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly SessionController _sessions;
    private readonly TimeProvider _time;
    private bool _wasActive;
    private List<SessionChecklist> _history = [];
    private IReadOnlyList<TaskCategory> _categories = [];

    public SessionTasksService(SqliteJsonDocumentStore store, IDataProtector protector, SessionController sessions, TimeProvider time)
    {
        _store = store;
        _protector = protector;
        _sessions = sessions;
        _time = time;
        _wasActive = sessions.State.IsActive;
        _sessions.StateChanged += OnStateChanged;
    }

    /// <summary>The draft (no <see cref="SessionChecklist.SessionStart"/>) or the running session's checklist.</summary>
    public SessionChecklist Current { get; private set; } = SessionChecklist.Empty;

    /// <summary>Checklists of ended sessions, oldest first (90 days).</summary>
    public IReadOnlyList<SessionChecklist> History => _history;

    /// <summary>The task categories in the order the checklist groups them.</summary>
    public IReadOnlyList<TaskCategory> Categories => _categories;

    /// <summary>Offer to end the session when every task is ticked (on by default).</summary>
    public bool NotifyAllDone { get; private set; } = true;

    /// <summary>True while the checklist belongs to a running session.</summary>
    public bool IsLive => Current.SessionStart is not null && _sessions.State.IsActive;

    public event EventHandler? Changed;

    /// <summary>The running session's last open task was ticked.</summary>
    public event EventHandler? AllDoneReached;

    public void LoadBlocking()
    {
        try
        {
            Stored? stored = Task.Run(() => _store.LoadAsync<Stored>(DocumentKey)).GetAwaiter().GetResult();
            Current = Open(stored?.Data) ?? SessionChecklist.Empty;
            NotifyAllDone = stored?.NotifyAllDone ?? true;
            List<StoredHistory>? history = Task.Run(() => _store.LoadAsync<List<StoredHistory>>(HistoryKey)).GetAwaiter().GetResult();
            _history = (history ?? []).Select(item => Open(item.Data)).OfType<SessionChecklist>().ToList();
            StoredCategories? categories = Task.Run(() => _store.LoadAsync<StoredCategories>(CategoriesKey)).GetAwaiter().GetResult();
            _categories = OpenCategories(categories?.Data) ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or JsonException)
        {
            Current = SessionChecklist.Empty;
        }
    }

    /// <summary>After the app resumed (or didn't resume) a session at launch: a checklist whose session is over goes to history.</summary>
    public Task AfterStartupAsync()
    {
        _wasActive = _sessions.State.IsActive;
        return Current.SessionStart is not null && !_sessions.State.IsActive ? ArchiveAsync() : Task.CompletedTask;
    }

    /// <summary>A task in <paramref name="categoryId"/> (null: no category), or a subtask of the task <paramref name="parentId"/>.</summary>
    public Task AddAsync(string title, Guid? parentId, Guid? categoryId = null)
    {
        return SetAsync(Current.Add(title, parentId, categoryId));
    }

    /// <summary>
    /// A task as the very first of the checklist (the shutdown card's "tomorrow's first task"): it takes the first open
    /// task's category, so grouping keeps it on top.
    /// </summary>
    public Task AddFirstAsync(string title)
    {
        return SetAsync(Current.AddFirst(title));
    }

    /// <summary>Puts a task in a category (null: no category).</summary>
    public Task SetCategoryAsync(Guid taskId, Guid? categoryId)
    {
        return SetAsync(Current.SetCategory(taskId, categoryId));
    }

    /// <summary>A new category with <paramref name="name"/>, in the first palette colour no category has yet; a blank name adds nothing.</summary>
    public Task AddCategoryAsync(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return Task.CompletedTask;
        }

        var used = _categories.Select(category => category.Color).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string color = TaskCategory.Palette.FirstOrDefault(hex => !used.Contains(hex))
                       ?? TaskCategory.Palette[_categories.Count % TaskCategory.Palette.Count];
        return SetCategoriesAsync([.. _categories, new TaskCategory(Guid.NewGuid(), trimmed, color)]);
    }

    /// <summary>A blank name keeps the old one.</summary>
    public Task RenameCategoryAsync(Guid id, string name)
    {
        string trimmed = name.Trim();
        return trimmed.Length == 0
            ? Task.CompletedTask
            : SetCategoriesAsync(_categories.Select(category => category.Id == id ? category with { Name = trimmed } : category).ToList());
    }

    public Task RecolorCategoryAsync(Guid id, string color)
    {
        return SetCategoriesAsync(_categories.Select(category => category.Id == id ? category with { Color = color } : category).ToList());
    }

    /// <summary>Its tasks have no category after it (ended sessions' checklists keep the name and colour they had).</summary>
    public async Task DeleteCategoryAsync(Guid id)
    {
        _categories = _categories.Where(category => category.Id != id).ToList();
        await SaveCategoriesAsync();
        await SetAsync(Current.WithoutCategory(id));
    }

    /// <summary>The checklist's tasks with their category's name and colour, as paired devices get them (ADR 24).</summary>
    public IReadOnlyList<SessionTask> Labelled(SessionChecklist checklist)
    {
        return checklist.Labelled(_categories);
    }

    public Task RemoveAsync(Guid id)
    {
        return SetAsync(Current.Remove(id));
    }

    public Task RenameAsync(Guid id, string title)
    {
        return SetAsync(Current.Rename(id, title));
    }

    /// <summary>The on-screen bar shows this open task or subtask; picking it again goes back to the first open task.</summary>
    public Task PickAsync(Guid id)
    {
        return SetAsync(Current.Pick(id));
    }

    public Task MoveAsync(Guid id, int offset)
    {
        return SetAsync(Current.Move(id, offset));
    }

    public async Task ToggleAsync(Guid id)
    {
        bool wasDone = Current.AllDone;
        await SetAsync(Current.Toggle(id, _time.GetUtcNow()));
        if (!wasDone && Current.AllDone && IsLive)
        {
            AllDoneReached?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task SetNotifyAllDoneAsync(bool notify)
    {
        NotifyAllDone = notify;
        return SaveAsync();
    }

    /// <summary>True when the running session may end now because its checklist is done (ADR 25).</summary>
    public bool CanEndAtOnce => IsLive && Current.CanEndAtOnce(_time.GetUtcNow());

    /// <summary>Ends the running session at once with the reason "All tasks done" (ADR 25); false when it may not.</summary>
    public bool EndAtOnce()
    {
        if (!CanEndAtOnce)
        {
            return false;
        }

        _sessions.EndSession(AllDoneReason);
        return true;
    }

    /// <summary>
    /// The checklist of the session that started at <paramref name="start"/> (running or in history), or null. The
    /// session's record and its checklist are stamped a moment apart, so within a few seconds counts.
    /// </summary>
    public SessionChecklist? ForSession(DateTimeOffset start)
    {
        static bool Near(SessionChecklist checklist, DateTimeOffset at) =>
            checklist.SessionStart is DateTimeOffset started && (started - at).Duration() <= TimeSpan.FromSeconds(5);
        return Current.HasTasks && Near(Current, start) ? Current : _history.LastOrDefault(item => Near(item, start));
    }

    public void Dispose()
    {
        _sessions.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(object? sender, SessionState state)
    {
        bool active = state.IsActive;
        if (active == _wasActive)
        {
            return;
        }

        _wasActive = active;
        if (active && Current.SessionStart is not null)
        {
            // A session resumed after a restart keeps its checklist as it was.
            return;
        }

        _ = active ? SetAsync(Current.Started(state.PhaseStartedAt ?? _time.GetUtcNow())) : ArchiveAsync();
    }

    /// <summary>
    /// Renames a task or subtask of an ended session's checklist (the Notebook), or deletes it when
    /// <paramref name="title"/> is null (a task goes with its subtasks).
    /// </summary>
    public async Task EditHistoryAsync(Guid id, string? title)
    {
        static SessionTask Apply(SessionTask task, Guid id, string? title) => task with
        {
            Subtasks = task.Subtasks
                .Where(subtask => subtask.Id != id || title is not null)
                .Select(subtask => subtask.Id == id ? subtask with { Title = title! } : subtask)
                .ToList(),
        };

        _history = _history.Select(checklist => checklist with
        {
            Tasks = checklist.Tasks
                .Where(task => task.Id != id || title is not null)
                .Select(task => task.Id == id ? task with { Title = title! } : Apply(task, id, title))
                .ToList(),
        }).ToList();
        await _store.SaveAsync(HistoryKey, _history.Select(item => new StoredHistory(Seal(item))).ToList());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ArchiveAsync()
    {
        if (Current.HasTasks)
        {
            DateTimeOffset cutoff = _time.GetUtcNow() - HistoryKept;
            // History keeps each task's category name and colour as they were, so deleting a category later loses nothing.
            SessionChecklist labelled = Current with { Tasks = Labelled(Current) };
            _history = [.. _history.Where(item => item.SessionStart is DateTimeOffset start && start >= cutoff), labelled];
            await _store.SaveAsync(HistoryKey, _history.Select(item => new StoredHistory(Seal(item))).ToList());
        }

        await SetAsync(SessionChecklist.Empty);
    }

    private async Task SetAsync(SessionChecklist checklist)
    {
        Current = checklist.Grouped(_categories);
        Changed?.Invoke(this, EventArgs.Empty);
        await SaveAsync();
    }

    private Task SaveAsync()
    {
        return _store.SaveAsync(DocumentKey, new Stored(Seal(Current), NotifyAllDone));
    }

    private async Task SetCategoriesAsync(IReadOnlyList<TaskCategory> categories)
    {
        _categories = categories;
        await SaveCategoriesAsync();
        // Set again so it's regrouped in the categories' order (and Changed is raised).
        await SetAsync(Current);
    }

    private Task SaveCategoriesAsync()
    {
        string data = Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(_categories, JsonOptions)));
        return _store.SaveAsync(CategoriesKey, new StoredCategories(data));
    }

    private IReadOnlyList<TaskCategory>? OpenCategories(string? data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return null;
        }

        try
        {
            string? json = _protector.Unprotect(Convert.FromBase64String(data));
            return json is null ? null : JsonSerializer.Deserialize<List<TaskCategory>>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    private string Seal(SessionChecklist checklist)
    {
        return Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(checklist, JsonOptions)));
    }

    private SessionChecklist? Open(string? data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return null;
        }

        try
        {
            string? json = _protector.Unprotect(Convert.FromBase64String(data));
            return json is null ? null : JsonSerializer.Deserialize<SessionChecklist>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    private sealed record Stored(string Data, bool NotifyAllDone = true);

    private sealed record StoredHistory(string Data);

    private sealed record StoredCategories(string Data);
}
