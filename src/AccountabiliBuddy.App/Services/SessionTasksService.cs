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
/// The running session's checklist is never merged: it belongs to this PC. A task also has an optional estimate, note and
/// planned day part (ADR 50) that travel with it, and "Move to Later" hands it to the <see cref="LaterStore"/>. Use from the
/// UI thread only.
/// </summary>
public sealed partial class SessionTasksService : IDisposable
{
    public const string AllDoneReason = "All tasks done";

    /// <summary>Tasks per session (done ones count, subtasks don't); a test session has no limit.</summary>
    public const int MaxTasks = 3;

    /// <summary>The shortest reason for adding a task over the limit.</summary>
    public const int MinOverLimitReasonLength = 10;

    // What another device may send for the running session (ADR 48): bounded, so one request can't bloat the checklist.
    private const int MaxRemoteText = 500;
    private const int MaxRemoteSteps = 30;
    private static readonly TimeSpan LastMinute = TimeSpan.FromMinutes(1);

    private const string DocumentKey = "session.tasks";
    private const string HistoryKey = "session.taskHistory";
    private const string CategoriesKey = "session.categories";
    private const string OverLimitKey = "session.overLimit";
    private const string NextTasksKey = "session.nextTasks";
    private const string MovedToLaterKey = "session.movedToLater";

    // Two positions closer than this are renumbered (halving a gap again and again runs out of precision).
    private const double MinPositionGap = 1e-9;
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

    // Tasks of ended sessions that were moved to Later: the session's record stays as it was, and they're left out of
    // "Unfinished from past sessions". Only ids and when (no text); kept as long as history is.
    private List<MovedEntry> _movedToLater = [];

    public SessionTasksService(SqliteJsonDocumentStore store, IDataProtector protector, SessionController sessions, TimeProvider time)
    {
        _store = store;
        _protector = protector;
        _sessions = sessions;
        _time = time;
        _wasActive = sessions.State.IsActive;
        _sessions.StateChanged += OnStateChanged;
        _sessions.Ticked += OnTicked;
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

    /// <summary>
    /// The Later list (ADR 50) a task is moved to and brought back from; set once the store exists. Its changes raise
    /// <see cref="Changed"/>, so pages redraw and the sync file is written again. Null: no Later list.
    /// </summary>
    public LaterStore? Later
    {
        get => _later;
        set
        {
            _later = value;
            if (value is not null)
            {
                value.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private LaterStore? _later;

    /// <summary>
    /// Where a planned day part starts (the plan's own time for that part, else the time it was planned with), as a local
    /// wall-clock time; set by the app, which knows the plan. Null: the time the task was planned with.
    /// </summary>
    public Func<PlannedSlot, DateTime>? PlannedStartOf { get; set; }

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
            _movedToLater = Task.Run(() => _store.LoadAsync<List<MovedEntry>>(MovedToLaterKey)).GetAwaiter().GetResult() ?? [];
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
    /// Adds a task another device asked for to the running session's checklist (ADR 48), by the same rules as the PC's own
    /// add: a 4th task (past <see cref="TaskAllowance"/>, which counts done ones) is parked to Come back to instead, and
    /// the answer says so. Refused when no real session runs here and in the last minute of a phase. A category named in
    /// the request is matched by id, else by name, else made (with the colour sent when it is a valid "#RRGGBB").
    /// </summary>
    /// <param name="id">The id the other device chose for the task; a task already here with it counts as done.</param>
    public async Task<RemoteTaskResult> AddRemoteAsync(Guid id, SyncTaskRequest request)
    {
        if (RemoteRefusal() is string refusal)
        {
            return RemoteTaskResult.Refused(refusal);
        }

        string title = (request.Title ?? string.Empty).Trim();
        if (title.Length == 0 || title.Length > MaxRemoteText || StepsInvalid(request.Steps))
        {
            return RemoteTaskResult.NotValid("The task needs a title and each step a text of up to 500 characters (30 steps at most).");
        }

        if (DetailsInvalid(request))
        {
            return RemoteTaskResult.NotValid("An estimate is 0 to 1440 minutes and a note up to 4000 characters.");
        }

        if (id != Guid.Empty && Current.Tasks.Any(task => task.Id == id))
        {
            return RemoteTaskResult.Applied();
        }

        List<string> steps = (request.Steps ?? []).Select(step => step.Title.Trim()).Where(step => step.Length > 0).ToList();
        if (Current.Tasks.Count >= TaskAllowance)
        {
            // Past the limit a task is parked, never added: only the PC's own "Add anyway" (with a reason) goes over.
            if (Comebacks is null)
            {
                return RemoteTaskResult.Refused($"This session already has {TaskAllowance} tasks.");
            }

            await Comebacks.AddAsync(steps.Count > 0 ? $"{title} (+{steps.Count} {(steps.Count == 1 ? "step" : "steps")})" : title);
            return RemoteTaskResult.Applied($"Parked: {TaskAllowance} tasks this session");
        }

        Guid? categoryId = await ResolveRemoteCategoryAsync(request);
        await SetAsync(Current.AddWithId(id, title, categoryId, steps, request.EstimateMinutes, request.Note));
        return RemoteTaskResult.Applied();
    }

    /// <summary>
    /// Renames a task of the running session or replaces its steps, as another device asked (ADR 48). Ticks stay as they
    /// are (ticking has its own action) and a done task is changed like any other. Refused when no real session runs here,
    /// in the last minute of a phase, and for a task that is no longer on the checklist.
    /// </summary>
    public async Task<RemoteTaskResult> EditRemoteAsync(Guid id, SyncTaskRequest request)
    {
        if (RemoteRefusal() is string refusal)
        {
            return RemoteTaskResult.Refused(refusal);
        }

        bool badTitle = request.Title is not null && (request.Title.Trim().Length == 0 || request.Title.Trim().Length > MaxRemoteText);
        if (badTitle || StepsInvalid(request.Steps))
        {
            return RemoteTaskResult.NotValid("A title and each step need a text of up to 500 characters (30 steps at most).");
        }

        if (DetailsInvalid(request))
        {
            return RemoteTaskResult.NotValid("An estimate is 0 to 1440 minutes and a note up to 4000 characters.");
        }

        if (Current.Tasks.All(task => task.Id != id))
        {
            return RemoteTaskResult.Refused("That task is no longer on this session's list.");
        }

        IReadOnlyList<SessionStepEdit>? steps = request.Steps?.Select(step => new SessionStepEdit(step.Id, step.Title)).ToList();
        SessionChecklist edited = Current.Edit(id, request.Title, steps);
        // ADR 50: an estimate or a note changes nothing that loosens a rule, so it is allowed like a rename (null keeps, 0 or blank clears).
        if (request.EstimateMinutes is int estimate)
        {
            edited = edited.SetEstimate(id, estimate);
        }

        if (request.Note is not null)
        {
            edited = edited.SetNote(id, request.Note);
        }

        await SetAsync(edited);
        return RemoteTaskResult.Applied();
    }

    /// <summary>
    /// Puts the running session's tasks in the order another device sent (ADR 48). The checklist stays grouped by category,
    /// so the order counts within each category. Refused when no real session runs here and in the last minute of a phase.
    /// </summary>
    public async Task<RemoteTaskResult> ReorderRemoteAsync(IReadOnlyList<Guid>? order)
    {
        if (RemoteRefusal() is string refusal)
        {
            return RemoteTaskResult.Refused(refusal);
        }

        if (order is not { Count: > 0 })
        {
            return RemoteTaskResult.NotValid("The new order was empty.");
        }

        await SetAsync(Current.InOrder(order));
        return RemoteTaskResult.Applied();
    }

    // Another device changes only a real session that runs on this PC, and not in the last minute of a phase (the PC's own
    // end-of-phase moment). A countdown that is over (planning waits for "Ready?") is not "the last minute".
    private string? RemoteRefusal()
    {
        SessionState state = _sessions.State;
        if (!IsLive || !state.IsRealSession)
        {
            return "No session is running on this PC. Add it to the next session instead.";
        }

        if (state.PhaseEndsAt is DateTimeOffset endsAt)
        {
            TimeSpan left = endsAt - state.CountdownTime(_time.GetUtcNow());
            if (left >= TimeSpan.Zero && left < LastMinute)
            {
                return "The phase ends in under a minute. Try again after it.";
            }
        }

        return null;
    }

    private static bool DetailsInvalid(SyncTaskRequest request)
    {
        return request.EstimateMinutes is < 0 or > SessionChecklist.MaxEstimateMinutes
            || (request.Note is not null && request.Note.Length > SessionChecklist.MaxNoteLength);
    }

    private static bool StepsInvalid(IReadOnlyList<SyncTaskStep>? steps)
    {
        // A step sent with no title at all ("title": null) is malformed: refuse it rather than throw in the apply loop.
        return steps is not null
            && (steps.Count > MaxRemoteSteps || steps.Any(step => step is null || step.Title is null || step.Title.Trim().Length > MaxRemoteText));
    }

    private async Task<Guid?> ResolveRemoteCategoryAsync(SyncTaskRequest request)
    {
        if (request.CategoryId is Guid known && _categories.Any(category => category.Id == known))
        {
            return known;
        }

        string name = (request.Category ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > MaxRemoteText)
        {
            return null;
        }

        TaskCategory? existing = _categories.FirstOrDefault(category => string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing.Id;
        }

        string? sent = request.CategoryColor is { Length: 7 } color && color[0] == '#' && color[1..].All(char.IsAsciiHexDigit) ? color : null;
        var used = _categories.Select(category => category.Color).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string fallback = TaskCategory.Palette.FirstOrDefault(hex => !used.Contains(hex)) ?? TaskCategory.Palette[_categories.Count % TaskCategory.Palette.Count];
        var created = new TaskCategory(Guid.NewGuid(), name, sent ?? fallback);
        _categories = [.. _categories, created];
        await SaveCategoriesAsync();
        return created.Id;
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

    /// <summary>
    /// The checklist's tasks as the session records in this PC's sync file carry them (ADR 24): with their category, but
    /// without the note, the estimate or the planned part (ADR 50), which travel only in the shared lists.
    /// </summary>
    public IReadOnlyList<SessionTask> LabelledForSync(SessionChecklist checklist)
    {
        return checklist.Labelled(_categories).Select(task => task with { EstimateMinutes = null, Note = null, PlannedFor = null }).ToList();
    }

    /// <summary>
    /// Takes a task or step off the checklist. What it held is kept in Recently deleted (this PC only, sealed) so Undo can bring
    /// it back; an open task taken off while the session runs counts in <see cref="SessionChecklist.DroppedOpen"/> (ADR 25).
    /// </summary>
    public async Task RemoveAsync(Guid id)
    {
        await RecordCurrentDeletionAsync(id);
        await SetAsync(Current.Remove(id));
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

    /// <summary>
    /// The estimate of a task of the checklist in minutes (null clears it). Allowed while a session runs: it loosens nothing
    /// (ADR 50).
    /// </summary>
    public Task SetEstimateAsync(Guid id, int? minutes)
    {
        return SetAsync(Current.SetEstimate(id, minutes));
    }

    /// <summary>The note of a task of the checklist (null or blank clears it); allowed while a session runs, like a rename. Never logged.</summary>
    public Task SetNoteAsync(Guid id, string? note)
    {
        return SetAsync(Current.SetNote(id, note));
    }

    /// <summary>"Put in plan": the day part a task of the checklist is planned for (null: not planned).</summary>
    public Task SetPlannedForAsync(Guid id, PlannedSlot? slot)
    {
        return SetAsync(Current.SetPlannedFor(id, slot));
    }

    /// <summary>
    /// "Move to Later": an open task of the checklist goes to the Later list with its steps, category, estimate and note
    /// (and <paramref name="dueOn"/>), a new item with a new id. During a session it counts as taken off open
    /// (<see cref="SessionChecklist.DroppedOpen"/>, ADR 25), like parking it. A done task, or an unknown one, stays.
    /// </summary>
    /// <returns>The id of the new Later item; null when nothing moved.</returns>
    public async Task<Guid?> MoveToLaterAsync(Guid taskId, DateOnly? dueOn = null)
    {
        SessionTask? task = Labelled(Current).FirstOrDefault(candidate => candidate.Id == taskId);
        if (Later is null || task is null || task.IsDone)
        {
            return null;
        }

        IReadOnlyList<SessionSubtask> openSteps = task.Subtasks.Where(step => !step.IsDone).ToList();
        SyncLaterItem? item = await Later.AddAsync(task.Title, openSteps, task.Category, task.CategoryColor, task.EstimateMinutes, task.Note, dueOn, task.PlannedFor);
        await SetAsync(Current.Parked(taskId));
        return item?.Id;
    }

    /// <summary>The open tasks of ended sessions (newest session first), for the Tasks page's "Unfinished from past sessions".</summary>
    public IReadOnlyList<PastTask> PastUnfinished()
    {
        HashSet<Guid> moved = _movedToLater.Select(entry => entry.Id).ToHashSet();
        return _history
            .Where(checklist => checklist.SessionStart is not null)
            .OrderByDescending(checklist => checklist.SessionStart)
            .SelectMany(checklist => checklist.Tasks.Where(task => !task.IsDone && !moved.Contains(task.Id)).Select(task => new PastTask(task, checklist.SessionStart!.Value)))
            .ToList();
    }

    /// <summary>
    /// Offers unfinished tasks of ended sessions to the Later list (nothing moves on its own): each becomes a Later item with
    /// its open steps, category, estimate and note. The session's record stays as it was; the task is remembered as moved,
    /// so it is no longer listed under "Unfinished from past sessions".
    /// </summary>
    /// <returns>How many moved.</returns>
    public async Task<int> MovePastToLaterAsync(IReadOnlySet<Guid> taskIds)
    {
        if (Later is null)
        {
            return 0;
        }

        int moved = 0;
        foreach (PastTask past in PastUnfinished().Where(candidate => taskIds.Contains(candidate.Task.Id)))
        {
            SessionTask task = past.Task;
            IReadOnlyList<SessionSubtask> openSteps = task.Subtasks.Where(step => !step.IsDone).ToList();
            // Remembered before the Later item is added, so the page that redraws on its change already leaves the task out.
            _movedToLater.Add(new MovedEntry(task.Id, _time.GetUtcNow()));
            if (await Later.AddAsync(task.Title, openSteps, task.Category, task.CategoryColor, task.EstimateMinutes, task.Note, null) is not null)
            {
                moved++;
            }
            else
            {
                _movedToLater.RemoveAt(_movedToLater.Count - 1);
            }
        }

        if (moved > 0)
        {
            DateTimeOffset keptFrom = _time.GetUtcNow() - HistoryKept;
            _movedToLater = _movedToLater.Where(entry => entry.At >= keptFrom).ToList();
            await _store.SaveAsync(MovedToLaterKey, _movedToLater);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return moved;
    }

    /// <summary>
    /// The titles of open tasks planned for the day part <paramref name="partId"/> on <paramref name="date"/> ("Put in plan"),
    /// for Today's Next up: the draft or running checklist, the next session's list and the Later list.
    /// </summary>
    public IReadOnlyList<string> PlannedTitles(DateOnly date, Guid partId)
    {
        bool Planned(PlannedSlot? slot) => slot is not null && slot.Date == date && slot.PartId == partId;

        var titles = new Dictionary<Guid, string>();
        // A running session's tasks are never "planned" (Put in plan is for the next session's list and Later).
        foreach (SessionTask task in Current.Tasks.Where(task => Current.SessionStart is null && !task.IsDone && Planned(task.PlannedFor)))
        {
            titles[task.Id] = task.Title;
        }

        foreach (SyncNextTask task in _nextTasks.Where(task => task.DeletedAt is null && Planned(task.PlannedFor)))
        {
            titles.TryAdd(task.Id, task.Title);
        }

        foreach (SyncLaterItem item in Later?.Items.Where(item => Planned(item.PlannedFor)) ?? [])
        {
            titles.TryAdd(item.Id, item.Title);
        }

        return titles.Values.ToList();
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
        _sessions.Ticked -= OnTicked;
    }

    // ADR 50, "Put in plan": no timer of its own. The session engine's tick is checked at most every 20 seconds, and only
    // while some task is planned.
    private void OnTicked(object? sender, EventArgs e)
    {
        DateTimeOffset now = _time.GetUtcNow();
        if (_releasingPlanned || now < _nextPlannedCheck)
        {
            return;
        }

        _nextPlannedCheck = now.AddSeconds(20);
        if (HasPlannedTasks())
        {
            _ = ReleasePlannedAsync();
        }
    }

    private bool _releasingPlanned;
    private DateTimeOffset _nextPlannedCheck;

    private bool HasPlannedTasks()
    {
        return (Later?.Items.Any(item => item.PlannedFor is not null) ?? false)
            || _nextTasks.Any(task => task.DeletedAt is null && task.PlannedFor is not null);
    }

    private DateTime PlannedStart(PlannedSlot slot)
    {
        return PlannedStartOf?.Invoke(slot) ?? slot.StartsAt;
    }

    /// <summary>
    /// When a task's planned day part has started (ADR 50): a Later item moves to the end of the next session's list, a task
    /// already on that list just stops being "planned". The running checklist is never touched, so this can't add a task to
    /// a session that runs; the list is the one another device may add to meanwhile (ADR 48).
    /// </summary>
    private async Task ReleasePlannedAsync()
    {
        _releasingPlanned = true;
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            DateTime localNow = TimeZoneInfo.ConvertTime(now, _sessions.TimeProvider.LocalTimeZone).DateTime;
            bool changed = false;
            List<SyncNextTask> items = _nextTasks.ToList();
            var released = new List<Guid>();
            foreach (SyncLaterItem item in (Later?.Items ?? []).Where(item => item.PlannedFor is PlannedSlot slot && PlannedStart(slot) <= localNow).ToList())
            {
                // The task keeps the Later item's id, so two PCs releasing the same item make the same task and the merge keeps one.
                // An id already on the list (a release that was interrupted before the Later item was deleted) isn't added again.
                if (items.All(task => task.Id != item.Id))
                {
                    double last = items.Where(task => task.DeletedAt is null && task.Category == item.Category).Select(task => task.Position).DefaultIfEmpty(0).Max();
                    items.Add(new SyncNextTask(
                        item.Id, item.Title, null, item.Category, item.CategoryColor, item.Subtasks, now, null,
                        SharedListMerge.PositionBetween(last, null), item.EstimateMinutes, item.Note));
                }

                released.Add(item.Id);
                changed = true;
            }

            for (int index = 0; index < items.Count; index++)
            {
                SyncNextTask task = items[index];
                if (task.DeletedAt is null && task.PlannedFor is PlannedSlot slot && PlannedStart(slot) <= localNow)
                {
                    items[index] = task with { PlannedFor = null, EditedAt = SharedListMerge.NextStamp(now, task) };
                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

            // The list is assigned before anything is awaited, so a merge or an edit that lands meanwhile builds on it.
            _nextTasks = Pruned(items, now);
            await SaveNextTasksAsync();
            foreach (Guid id in released)
            {
                await Later!.DeleteAsync(id);
            }

            if (Current.SessionStart is null)
            {
                await ShowSharedDraftAsync();
            }
            else
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _releasingPlanned = false;
        }
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
        if (title is null)
        {
            await RecordHistoryDeletionAsync(id);
        }

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
            _history = [.. _history.Where(item => KeptInHistory(item, cutoff)), labelled];
            await _store.SaveAsync(HistoryKey, _history.Select(item => new StoredHistory(Seal(item))).ToList());
        }

        // The next session's list: empty, unless another device added tasks to it while this session ran (ADR 48); a task of
        // the session that just ended is never on it.
        List<SyncNextTask> remaining = TombstoneConsumed(_nextTasks, _time.GetUtcNow(), out bool tombstoned);
        if (tombstoned)
        {
            _nextTasks = remaining;
            await SaveNextTasksAsync();
        }

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
    /// <param name="knowsDetails">
    /// The file's writer knows ADR 50's fields (its file carries a <c>later</c> list, empty or not). When it doesn't (an older
    /// phone), a task that comes without an estimate, note or planned part gets them from this device's copy of the same
    /// task before merging: such a writer can't clear them, so it never means "removed".
    /// </param>
    /// <returns>True when this device's list changed.</returns>
    public async Task<bool> MergeNextTasksAsync(IReadOnlyList<SyncNextTask> incoming, bool knowsDetails = true)
    {
        DateTimeOffset now = _time.GetUtcNow();
        if (!knowsDetails)
        {
            Dictionary<Guid, SyncNextTask> held = _nextTasks.Where(task => task.DeletedAt is null).ToDictionary(task => task.Id);
            incoming = incoming.Select(task => task.DeletedAt is null && held.TryGetValue(task.Id, out SyncNextTask? own)
                ? task with
                {
                    EstimateMinutes = task.EstimateMinutes ?? own.EstimateMinutes,
                    Note = task.Note ?? own.Note,
                    PlannedFor = task.PlannedFor ?? own.PlannedFor,
                }
                : task).ToList();
        }

        List<SyncNextTask> merged = SharedListMerge.Merge(_nextTasks, incoming, now);
        bool changed = !merged.SequenceEqual(SharedListMerge.Merge(_nextTasks, [], now), new NextTaskComparer());
        List<SyncNextTask> result = TombstoneConsumed(merged, now, out bool tombstoned);
        if (!changed && !tombstoned)
        {
            return false;
        }

        _nextTasks = result;
        await SaveNextTasksAsync();
        if (Current.SessionStart is null)
        {
            await ShowSharedDraftAsync();
        }
        else
        {
            // The running checklist stays as it is; the merged list goes out in this PC's file.
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    // A task that already belongs to a session (the running checklist, or one in history) can't be on the next list: a late
    // edit from another device that brought it back from its tombstone is deleted again, so it can't show twice.
    private List<SyncNextTask> TombstoneConsumed(List<SyncNextTask> items, DateTimeOffset now, out bool tombstoned)
    {
        HashSet<Guid> consumed = _history.SelectMany(checklist => checklist.Tasks).Select(task => task.Id).ToHashSet();
        if (Current.SessionStart is not null)
        {
            consumed.UnionWith(Current.Tasks.Select(task => task.Id));
        }

        tombstoned = false;
        var result = new List<SyncNextTask>(items.Count);
        foreach (SyncNextTask item in items)
        {
            if (item.DeletedAt is null && consumed.Contains(item.Id))
            {
                result.Add(item.Deleted(SharedListMerge.NextStamp(now, item)));
                tombstoned = true;
            }
            else
            {
                result.Add(item);
            }
        }

        return result;
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
                task.Id, task.Title ?? string.Empty, task.DoneAt, task.Subtasks ?? [],
                task.Category is string name ? _categories.FirstOrDefault(category => string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase))?.Id : null,
                EstimateMinutes: task.EstimateMinutes, Note: task.Note, PlannedFor: task.PlannedFor))
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
    /// task that moved within its category (it takes a position between its new neighbours), and a tombstone for each task
    /// taken off. Positions only mean something within a category (the checklist is grouped by category), so a task whose
    /// order among its category's tasks didn't change keeps its stamp, whatever the other categories do.
    /// </summary>
    /// <returns>True when the shared list changed.</returns>
    private bool ReconcileNextTasks(SessionChecklist draft)
    {
        DateTimeOffset now = _time.GetUtcNow();
        IReadOnlyList<SessionTask> tasks = draft.Labelled(_categories);
        Dictionary<Guid, SyncNextTask> items = _nextTasks.ToDictionary(task => task.Id);
        var positions = new Dictionary<Guid, double>();
        foreach (IGrouping<string, SessionTask> group in tasks.GroupBy(task => task.Category ?? string.Empty))
        {
            List<SessionTask> members = group.ToList();
            double[] assigned = GroupPositions(members
                .Select(task => items.TryGetValue(task.Id, out SyncNextTask? held) && held.DeletedAt is null ? (double?)held.Position : null)
                .ToList());
            for (int index = 0; index < members.Count; index++)
            {
                positions[members[index].Id] = assigned[index];
            }
        }

        var inDraft = new HashSet<Guid>();
        bool changed = false;
        foreach (SessionTask task in tasks)
        {
            inDraft.Add(task.Id);
            items.TryGetValue(task.Id, out SyncNextTask? held);
            SyncNextTask? live = held is { DeletedAt: null } ? held : null;
            double position = positions[task.Id];
            bool same = live is not null
                && live.Title == task.Title
                && live.DoneAt == task.DoneAt
                && string.Equals(live.Category, task.Category, StringComparison.Ordinal)
                && string.Equals(live.CategoryColor, task.CategoryColor, StringComparison.OrdinalIgnoreCase)
                && (live.Subtasks ?? []).SequenceEqual(task.Subtasks)
                && live.EstimateMinutes == task.EstimateMinutes
                && string.Equals(live.Note, task.Note, StringComparison.Ordinal)
                && live.PlannedFor == task.PlannedFor
                && live.Position == position;
            if (same)
            {
                continue;
            }

            items[task.Id] = new SyncNextTask(
                task.Id, task.Title, task.DoneAt, task.Category, task.CategoryColor, task.Subtasks.Count > 0 ? task.Subtasks : null,
                SharedListMerge.NextStamp(now, held), null, position, task.EstimateMinutes, task.Note, task.PlannedFor);
            changed = true;
        }

        foreach (SyncNextTask gone in items.Values.Where(item => item.DeletedAt is null && !inDraft.Contains(item.Id)).ToList())
        {
            items[gone.Id] = gone.Deleted(SharedListMerge.NextStamp(now, gone));
            changed = true;
        }

        if (changed)
        {
            _nextTasks = Pruned(items.Values, now);
        }

        return changed;
    }

    // The positions of one category's tasks in their order, given the stored ones (null for a task that has none yet). The
    // largest set of tasks whose stored positions already ascend keeps them; each other task gets a position between its
    // neighbours. When two positions get too close, the category is numbered 1, 2, 3 again.
    private static double[] GroupPositions(IReadOnlyList<double?> stored)
    {
        bool[] keep = LongestAscending(stored);
        var positions = new double[stored.Count];
        double? lower = null;
        for (int index = 0; index < stored.Count; index++)
        {
            if (keep[index])
            {
                positions[index] = stored[index]!.Value;
            }
            else
            {
                double? upper = null;
                for (int next = index + 1; next < stored.Count && upper is null; next++)
                {
                    upper = keep[next] ? stored[next] : null;
                }

                positions[index] = SharedListMerge.PositionBetween(lower, upper);
            }

            lower = positions[index];
        }

        bool tooClose = Enumerable.Range(1, Math.Max(0, positions.Length - 1)).Any(index => positions[index] - positions[index - 1] < MinPositionGap);
        return tooClose ? Enumerable.Range(1, positions.Length).Select(number => (double)number).ToArray() : positions;
    }

    private static bool[] LongestAscending(IReadOnlyList<double?> stored)
    {
        int count = stored.Count;
        var length = new int[count];
        var before = new int[count];
        int best = -1;
        for (int index = 0; index < count; index++)
        {
            before[index] = -1;
            if (stored[index] is not double position)
            {
                continue;
            }

            length[index] = 1;
            for (int earlier = 0; earlier < index; earlier++)
            {
                if (stored[earlier] is double earlierPosition && earlierPosition < position && length[earlier] + 1 > length[index])
                {
                    length[index] = length[earlier] + 1;
                    before[index] = earlier;
                }
            }

            if (best < 0 || length[index] > length[best])
            {
                best = index;
            }
        }

        var keep = new bool[count];
        for (int index = best; index >= 0; index = before[index])
        {
            keep[index] = true;
        }

        return keep;
    }

    private async Task TombstoneNextTasksAsync()
    {
        DateTimeOffset now = _time.GetUtcNow();
        if (_nextTasks.All(task => task.DeletedAt is not null))
        {
            return;
        }

        _nextTasks = Pruned(_nextTasks.Select(task => task.DeletedAt is null ? task.Deleted(SharedListMerge.NextStamp(now, task)) : task), now);
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

    // A past task moved to Later: its id and when (no text).
    private sealed record MovedEntry(Guid Id, DateTimeOffset At);

    /// <summary>True once the user has used ADR 50's task details: an estimate, a note, a plan, or the Later list (the setup check).</summary>
    public bool UsesTaskDetails => (Later?.Items.Count ?? 0) > 0
        || _nextTasks.Any(task => task.DeletedAt is null && (task.EstimateMinutes is not null || task.Note is not null || task.PlannedFor is not null))
        || Current.Tasks.Any(task => task.EstimateMinutes is not null || task.Note is not null || task.PlannedFor is not null)
        || _history.Any(checklist => checklist.Tasks.Any(task => task.EstimateMinutes is not null || task.Note is not null));

    // A task added past the limit: the session it belongs to (null while still on the draft), when, and the typed reason.
    private sealed record OverLimitEntry(DateTimeOffset? SessionStart, DateTimeOffset At, string Reason);
}
