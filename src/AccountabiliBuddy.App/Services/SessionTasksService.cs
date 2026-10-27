using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Guided sessions (ADR 24, 25): the checklist of the next or running session. Tasks added while no session runs are a
/// draft (the quick-session popup and the Session tab share it); starting a session makes it that session's checklist,
/// which survives a restart; ending the session keeps it in history (90 days) and starts an empty draft. Tasks can be in
/// categories (named, coloured; the checklist is grouped under them, no category last). Stored DPAPI-encrypted
/// ("session.tasks", "session.taskHistory", "session.categories"), like notes on added sessions. The draft is the next
/// session's task list, which the user's devices share through the sealed sync files (ADR 48): it is kept apart as items
/// with edit times and tombstones ("session.nextTasks", DPAPI-encrypted) that <see cref="MergeNextTasksAsync"/> merges.
/// The running session's checklist is never merged: it belongs to this PC. Use from the UI thread only.
/// </summary>
public sealed class SessionTasksService : IDisposable
{
    public const string AllDoneReason = "All tasks done";

    /// <summary>Tasks per session (done ones count, subtasks don't); a test session has no limit.</summary>
    public const int MaxTasks = 3;

    /// <summary>The shortest reason for adding a task over the limit.</summary>
    public const int MinOverLimitReasonLength = 10;

    private const string DocumentKey = "session.tasks";
    private const string HistoryKey = "session.taskHistory";
    private const string CategoriesKey = "session.categories";
    private const string OverLimitKey = "session.overLimit";
    private const string NextTasksKey = "session.nextTasks";
    private static readonly TimeSpan HistoryKept = TimeSpan.FromDays(90);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly SessionController _sessions;
    private readonly TimeProvider _time;
    private bool _wasActive;
    private List<SessionChecklist> _history = [];
    private IReadOnlyList<TaskCategory> _categories = [];
    private List<OverLimitEntry> _overLimit = [];

    // The shared next-session list (ADR 48), tombstones included; the draft shows its live items.
    private List<SyncNextTask> _nextTasks = [];

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

    /// <summary>
    /// The come-backs a task is parked to and brought back from; set once the store exists. Null: no parking (the
    /// checklist hides ⚑).
    /// </summary>
    public ComebackStore? Comebacks { get; set; }

    /// <summary>True while the checklist belongs to a running session.</summary>
    public bool IsLive => Current.SessionStart is not null && _sessions.State.IsActive;

    public event EventHandler? Changed;

    /// <summary>The running session's last open task was ticked.</summary>
    public event EventHandler? AllDoneReached;

    /// <summary>
    /// Raised when a real session runs with more than <see cref="MaxTasks"/> tasks (carried over, or a session started
    /// from the phone or watch): the PC shows "Pick 3 for this session".
    /// </summary>
    public event EventHandler? PickRequired;

    /// <summary>
    /// Tasks this session may hold: <see cref="MaxTasks"/> plus the "Add anyway" adds approved for it (those carry a
    /// reason and stay; only carried-over extras are picked down).
    /// </summary>
    public int TaskAllowance => MaxTasks + (Current.SessionStart is DateTimeOffset start ? OverLimitCount(start) : 0);

    /// <summary>A real session runs with more tasks (open and done) than <see cref="TaskAllowance"/>, and the user has not picked yet.</summary>
    public bool NeedsPick => IsLive && _sessions.State.IsRealSession && Current.Tasks.Count > TaskAllowance;

    /// <summary>The open tasks the pick prompt lists, in list order (done ones are never parked).</summary>
    public IReadOnlyList<SessionTask> PickCandidates => Current.Tasks.Where(task => !task.IsDone).ToList();

    /// <summary>How many of the <see cref="PickCandidates"/> may stay: the allowance less the done tasks.</summary>
    public int PickKeepCount => Math.Max(0, TaskAllowance - Current.Tasks.Count(task => task.IsDone));

    /// <summary>
    /// Parks the tasks of <paramref name="shown"/> (the rows the prompt listed) that are not in <paramref name="keep"/>,
    /// when they are still open. Done tasks and tasks added after the prompt opened are left alone.
    /// </summary>
    public async Task KeepOnlyAsync(IReadOnlySet<Guid> keep, IReadOnlyCollection<Guid> shown)
    {
        foreach (Guid id in shown.Where(id => !keep.Contains(id)))
        {
            if (Current.Tasks.Any(task => task.Id == id && !task.IsDone))
            {
                await ParkAsync(id);
            }
        }
    }

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
            StoredCategories? overLimit = Task.Run(() => _store.LoadAsync<StoredCategories>(OverLimitKey)).GetAwaiter().GetResult();
            _overLimit = OpenOverLimit(overLimit?.Data) ?? [];
            StoredCategories? nextTasks = Task.Run(() => _store.LoadAsync<StoredCategories>(NextTasksKey)).GetAwaiter().GetResult();
            _nextTasks = OpenNextTasks(nextTasks?.Data) ?? [];
            if (Current.SessionStart is null)
            {
                LoadSharedDraftBlocking(firstTime: nextTasks is null);
            }
        }
        catch (Exception ex) when (ex is SqliteException or JsonException)
        {
            Current = SessionChecklist.Empty;
        }
    }

    // The shared list is the truth for the draft once it exists (a merge may have arrived just before a quit); a store from
    // before ADR 48 has none yet, so the draft it has becomes the first version of it.
    private void LoadSharedDraftBlocking(bool firstTime)
    {
        if (firstTime)
        {
            if (!Current.HasTasks)
            {
                return;
            }

            ReconcileNextTasks(Current);
            Task.Run(SaveNextTasksAsync).GetAwaiter().GetResult();
            return;
        }

        bool categoriesAdded = TakeCategoriesFrom(_nextTasks);
        Current = DraftFromShared(Current.CurrentId).Grouped(_categories);
        if (categoriesAdded)
        {
            Task.Run(SaveCategoriesAsync).GetAwaiter().GetResult();
        }
    }

    /// <summary>After the app resumed (or didn't resume) a session at launch: a checklist whose session is over goes to history.</summary>
    public Task AfterStartupAsync()
    {
        _wasActive = _sessions.State.IsActive;
        if (NeedsPick)
        {
            PickRequired?.Invoke(this, EventArgs.Empty); // A restart before the pick: it asks again.
        }

        return Current.SessionStart is not null && !_sessions.State.IsActive ? ArchiveAsync() : Task.CompletedTask;
    }

    /// <summary>A task in <paramref name="categoryId"/> (null: no category), or a subtask of the task <paramref name="parentId"/>.</summary>
    public Task AddAsync(string title, Guid? parentId, Guid? categoryId = null)
    {
        return SetAsync(Current.Add(title, parentId, categoryId));
    }

    /// <summary>True when the next task would be a 4th: the caller asks to park it or for a reason (never in a test session).</summary>
    public bool IsAtTaskLimit => !_sessions.State.IsTest && Current.Tasks.Count >= MaxTasks;

    /// <summary>Parks <paramref name="title"/> as a come-back instead of adding it.</summary>
    public async Task ParkTitleAsync(string title)
    {
        if (Comebacks is not null && title.Trim().Length > 0)
        {
            await Comebacks.AddAsync(title.Trim());
        }
    }

    /// <summary>
    /// Adds a task over the limit. The reason (at least <see cref="MinOverLimitReasonLength"/> characters, false when
    /// shorter) is kept sealed on this PC only, and the add is counted for the session review.
    /// </summary>
    public async Task<bool> AddOverLimitAsync(string title, Guid? categoryId, string reason)
    {
        if (!await RecordOverLimitAsync(reason))
        {
            return false;
        }

        await AddAsync(title, null, categoryId);
        return true;
    }

    private async Task<bool> RecordOverLimitAsync(string reason)
    {
        string trimmed = reason.Trim();
        if (trimmed.Length < MinOverLimitReasonLength)
        {
            return false;
        }

        DateTimeOffset now = _time.GetUtcNow();
        DateTimeOffset cutoff = now - HistoryKept;
        _overLimit = [.. _overLimit.Where(entry => entry.At >= cutoff), new OverLimitEntry(Current.SessionStart, now, trimmed)];
        await SaveOverLimitAsync();
        return true;
    }

    /// <summary>How many tasks past the limit the session that started at <paramref name="start"/> got (within a few seconds of the stamp).</summary>
    public int OverLimitCount(DateTimeOffset start)
    {
        return _overLimit.Count(entry => entry.SessionStart is DateTimeOffset started && (started - start).Duration() <= TimeSpan.FromSeconds(5));
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

    /// <summary>Drag and drop: a task before or after another (taking its category), or a subtask among its task's subtasks.</summary>
    public Task MoveToAsync(Guid id, Guid targetId, bool after)
    {
        return SetAsync(Current.MoveTo(id, targetId, after));
    }

    /// <summary>
    /// Parks a task as a come-back (its title, plus " (+N steps)" for open subtasks) and takes it off the checklist; the
    /// bar moves on when it showed it. Allowed during a session: it loosens nothing.
    /// </summary>
    public async Task ParkAsync(Guid taskId)
    {
        if (Comebacks is null || Current.ParkText(taskId) is not string text)
        {
            return;
        }

        await Comebacks.AddAsync(text);
        await SetAsync(Current.Parked(taskId));
    }

    /// <summary>
    /// A come-back becomes a task at the end of the checklist (no category) and is ticked off the come-backs. Past the
    /// limit it needs an <paramref name="overLimitReason"/> (<see cref="MinOverLimitReasonLength"/> characters, counted
    /// like "Add anyway"); false when it was not added.
    /// </summary>
    public async Task<bool> AddFromComebackAsync(Guid comebackId, string? overLimitReason = null)
    {
        if (Comebacks?.Open.FirstOrDefault(comeback => comeback.Id == comebackId) is not Comeback comeback)
        {
            return false;
        }

        if (IsAtTaskLimit && !await RecordOverLimitAsync(overLimitReason ?? string.Empty))
        {
            return false;
        }

        await AddAsync(comeback.Text, null);
        await Comebacks.TickAsync(comebackId);
        return true;
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

    /// <summary>
    /// Marks the open tasks with these titles done in the checklists of the sessions that started on <paramref name="date"/>
    /// (the shutdown card's ticked "Left open" rows), with the app's trusted time as the done time.
    /// </summary>
    public async Task MarkHistoryDoneAsync(DateOnly date, IReadOnlySet<string> titles)
    {
        DateTimeOffset now = _time.GetUtcNow();
        _history = _history.Select(checklist => checklist.SessionStart is DateTimeOffset start
                && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, _sessions.TimeProvider.LocalTimeZone).DateTime) == date
            ? checklist with
            {
                Tasks = checklist.Tasks.Select(task => !task.IsDone && titles.Contains(task.Title)
                    ? task with { DoneAt = now, Subtasks = task.Subtasks.Select(subtask => subtask with { DoneAt = subtask.DoneAt ?? now }).ToList() }
                    : task).ToList(),
            }
            : checklist).ToList();
        await _store.SaveAsync(HistoryKey, _history.Select(item => new StoredHistory(Seal(item))).ToList());
        Changed?.Invoke(this, EventArgs.Empty);
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
        // A test session neither stamps the draft checklist nor archives it.
        bool active = state.IsRealSession;
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

        if (active)
        {
            DateTimeOffset start = state.PhaseStartedAt ?? _time.GetUtcNow();
            // Over-limit adds made on the draft belong to the session that now starts.
            if (_overLimit.Any(entry => entry.SessionStart is null))
            {
                _overLimit = _overLimit.Select(entry => entry.SessionStart is null ? entry with { SessionStart = start } : entry).ToList();
                _ = SaveOverLimitAsync();
            }

            _ = StartChecklistAsync(start);
        }
        else
        {
            _ = ArchiveAsync();
        }
    }

    private async Task StartChecklistAsync(DateTimeOffset start)
    {
        // The draft's tasks are this session's now: they leave the shared next-session list (ADR 48).
        await TombstoneNextTasksAsync();
        await SetAsync(Current.Started(start));
        if (NeedsPick)
        {
            PickRequired?.Invoke(this, EventArgs.Empty);
        }
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

        // The next session's list: empty, unless another device added tasks to it while this session ran (ADR 48).
        await ShowSharedDraftAsync();
    }

    private async Task SetAsync(SessionChecklist checklist)
    {
        Current = checklist.Grouped(_categories);
        bool shared = Current.SessionStart is null && ReconcileNextTasks(Current);
        Changed?.Invoke(this, EventArgs.Empty);
        await SaveAsync();
        if (shared)
        {
            await SaveNextTasksAsync();
        }
    }

    /// <summary>
    /// The next session's list for this device's sync file (ADR 48): its tasks and the tombstones of the last 30 days,
    /// in list order.
    /// </summary>
    public IReadOnlyList<SyncNextTask> NextTasksForSync()
    {
        DateTimeOffset keptFrom = _time.GetUtcNow() - SharedListMerge.TombstonesKept;
        return _nextTasks
            .Where(task => task.DeletedAt is not DateTimeOffset deletedAt || deletedAt > keptFrom)
            .OrderBy(task => task.Position)
            .ToList();
    }

    /// <summary>
    /// Takes another device's next-session list in (ADR 48): item by item, the newest edit wins and a newer tombstone
    /// removes a task (<see cref="SharedListMerge"/>). The draft shows the result; a running session's checklist is not
    /// touched (it belongs to this PC), the result shows when it ends. Task names are never logged.
    /// </summary>
    /// <returns>True when this device's list changed.</returns>
    public async Task<bool> MergeNextTasksAsync(IReadOnlyList<SyncNextTask> incoming)
    {
        DateTimeOffset now = _time.GetUtcNow();
        List<SyncNextTask> merged = SharedListMerge.Merge(_nextTasks, incoming, now);
        if (merged.SequenceEqual(SharedListMerge.Merge(_nextTasks, [], now), new NextTaskComparer()))
        {
            return false;
        }

        _nextTasks = merged;
        await SaveNextTasksAsync();
        if (Current.SessionStart is null)
        {
            await ShowSharedDraftAsync();
        }

        return true;
    }

    // The draft becomes the live items of the shared list (grouped by this device's categories); nothing is stamped, so a
    // device only changes the shared list by the user's own edits.
    private async Task ShowSharedDraftAsync()
    {
        bool categoriesAdded = TakeCategoriesFrom(_nextTasks);
        Current = DraftFromShared(Current.SessionStart is null ? Current.CurrentId : null).Grouped(_categories);
        if (categoriesAdded)
        {
            await SaveCategoriesAsync();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        await SaveAsync();
    }

    private SessionChecklist DraftFromShared(Guid? currentId)
    {
        List<SessionTask> tasks = _nextTasks
            .Where(task => task.DeletedAt is null)
            .OrderBy(task => task.Position)
            .Select(task => new SessionTask(
                task.Id, task.Title, task.DoneAt, task.Subtasks ?? [],
                task.Category is string name ? _categories.FirstOrDefault(category => string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase))?.Id : null))
            .ToList();
        return new SessionChecklist(null, false, tasks, tasks.Any(task => !task.IsDone && task.Id == currentId) ? currentId : null);
    }

    // A task that comes with a category this device doesn't have gets it, with the name and colour it came with (ADR 24, 48).
    // A colour change counts as an edit of the category: it restamps the category's tasks (ReconcileNextTasks), so the colour on
    // the category's most recently edited task is the latest one, and a category this device already has takes it.
    private bool TakeCategoriesFrom(IEnumerable<SyncNextTask> tasks)
    {
        var categories = _categories.ToList();
        bool changed = false;
        IEnumerable<IGrouping<string, SyncNextTask>> byCategory = tasks
            .Where(task => task.DeletedAt is null && !string.IsNullOrWhiteSpace(task.Category))
            .GroupBy(task => task.Category!.Trim(), StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, SyncNextTask> group in byCategory)
        {
            SyncNextTask newest = group.OrderByDescending(task => task.EditedAt).ThenBy(task => task.Id.ToString("D"), StringComparer.Ordinal).First();
            string? sent = newest.CategoryColor is { Length: 7 } color && color[0] == '#' ? color : null;
            int index = categories.FindIndex(category => string.Equals(category.Name, group.Key, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                categories.Add(new TaskCategory(Guid.NewGuid(), group.Key, sent ?? TaskCategory.Palette[categories.Count % TaskCategory.Palette.Count]));
                changed = true;
            }
            else if (sent is not null && !string.Equals(categories[index].Color, sent, StringComparison.OrdinalIgnoreCase))
            {
                categories[index] = categories[index] with { Color = sent };
                changed = true;
            }
        }

        if (changed)
        {
            _categories = categories;
        }

        return changed;
    }

    /// <summary>
    /// Makes the shared list say what the draft says, stamping only what the user changed: a changed task, a new one, a
    /// moved one (it takes a position between its neighbours), and a tombstone for each task taken off.
    /// </summary>
    /// <returns>True when the shared list changed.</returns>
    private bool ReconcileNextTasks(SessionChecklist draft)
    {
        DateTimeOffset now = _time.GetUtcNow();
        IReadOnlyList<SessionTask> tasks = draft.Labelled(_categories);
        Dictionary<Guid, SyncNextTask> items = _nextTasks.ToDictionary(task => task.Id);
        var inDraft = new HashSet<Guid>();
        bool changed = false;
        double? previous = null;
        for (int index = 0; index < tasks.Count; index++)
        {
            SessionTask task = tasks[index];
            inDraft.Add(task.Id);
            items.TryGetValue(task.Id, out SyncNextTask? existing);
            SyncNextTask? live = existing is { DeletedAt: null } ? existing : null;
            double position = live is not null && (previous is null || live.Position > previous)
                ? live.Position
                : SharedListMerge.PositionBetween(previous, NextKeptPosition(tasks, index, items, previous));
            previous = position;
            bool same = live is not null
                && live.Title == task.Title
                && live.DoneAt == task.DoneAt
                && string.Equals(live.Category, task.Category, StringComparison.Ordinal)
                && string.Equals(live.CategoryColor, task.CategoryColor, StringComparison.OrdinalIgnoreCase)
                && (live.Subtasks ?? []).SequenceEqual(task.Subtasks)
                && live.Position == position;
            if (same)
            {
                continue;
            }

            items[task.Id] = new SyncNextTask(
                task.Id, task.Title, task.DoneAt, task.Category, task.CategoryColor, task.Subtasks.Count > 0 ? task.Subtasks : null, now, null, position);
            changed = true;
        }

        foreach (SyncNextTask gone in items.Values.Where(item => item.DeletedAt is null && !inDraft.Contains(item.Id)).ToList())
        {
            items[gone.Id] = gone.Deleted(now);
            changed = true;
        }

        if (changed)
        {
            _nextTasks = Pruned(items.Values, now);
        }

        return changed;
    }

    // The position of the first task after <index> that keeps its place (its stored position is past <previous>), else null.
    private static double? NextKeptPosition(IReadOnlyList<SessionTask> tasks, int index, Dictionary<Guid, SyncNextTask> items, double? previous)
    {
        for (int next = index + 1; next < tasks.Count; next++)
        {
            if (items.TryGetValue(tasks[next].Id, out SyncNextTask? item) && item.DeletedAt is null && (previous is null || item.Position > previous))
            {
                return item.Position;
            }
        }

        return null;
    }

    private async Task TombstoneNextTasksAsync()
    {
        DateTimeOffset now = _time.GetUtcNow();
        if (_nextTasks.All(task => task.DeletedAt is not null))
        {
            return;
        }

        _nextTasks = Pruned(_nextTasks.Select(task => task.DeletedAt is null ? task.Deleted(now) : task), now);
        await SaveNextTasksAsync();
    }

    private static List<SyncNextTask> Pruned(IEnumerable<SyncNextTask> items, DateTimeOffset now)
    {
        DateTimeOffset keptFrom = now - SharedListMerge.TombstonesKept;
        return items
            .Where(task => task.DeletedAt is not DateTimeOffset deletedAt || deletedAt > keptFrom)
            .OrderBy(task => task.Position)
            .ToList();
    }

    private Task SaveNextTasksAsync()
    {
        string data = Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(_nextTasks, JsonOptions)));
        return _store.SaveAsync(NextTasksKey, new StoredCategories(data));
    }

    private List<SyncNextTask>? OpenNextTasks(string? data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return null;
        }

        try
        {
            string? json = _protector.Unprotect(Convert.FromBase64String(data));
            return json is null ? null : JsonSerializer.Deserialize<List<SyncNextTask>>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    // SyncNextTask holds a list, so it isn't equal by value: compare it by its fields.
    private sealed class NextTaskComparer : IEqualityComparer<SyncNextTask>
    {
        public bool Equals(SyncNextTask? left, SyncNextTask? right)
        {
            return left is not null && right is not null
                && left.Id == right.Id
                && left.EditedAt == right.EditedAt
                && left.DeletedAt == right.DeletedAt
                && left.Position == right.Position
                && left.TieBreak == right.TieBreak;
        }

        public int GetHashCode(SyncNextTask item)
        {
            return item.Id.GetHashCode();
        }
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

    private Task SaveOverLimitAsync()
    {
        string data = Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(_overLimit, JsonOptions)));
        return _store.SaveAsync(OverLimitKey, new StoredCategories(data));
    }

    private List<OverLimitEntry>? OpenOverLimit(string? data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return null;
        }

        try
        {
            string? json = _protector.Unprotect(Convert.FromBase64String(data));
            return json is null ? null : JsonSerializer.Deserialize<List<OverLimitEntry>>(json, JsonOptions);
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

    // A task added past the limit: the session it belongs to (null while still on the draft), when, and the typed reason.
    private sealed record OverLimitEntry(DateTimeOffset? SessionStart, DateTimeOffset At, string Reason);
}
