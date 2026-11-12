using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using AccountabiliBuddy.Core.Tasks;
using AccountabiliBuddy.App.Services.Tasks;
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
/// UI thread only. Since ADR 76 the next session's list and the categories are views of the one task list (<see cref="UseTaskStore"/>).
/// </summary>
public sealed partial class SessionTasksService : IDisposable
{
    public const string AllDoneReason = "All tasks done";

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

    // The device-local list of older versions (ADR 72 made it the shared PastSettled store); read once at startup and handed over.
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

    /// <summary>The clock the tasks are stamped by.</summary>
    public TimeProvider Clock => _time;

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
    /// What was done with past sessions' open tasks on any device (ADR 72), shared; set once the store exists. Null: only this
    /// device's own moves count (nothing is recorded).
    /// </summary>
    public PastSettledStore? PastSettled { get; set; }

    /// <summary>This device's id in its sync group (ADR 72's keeper rule); null while it isn't in one. Set by the app.</summary>
    public Func<Guid?> SelfId { get; set; } = () => null;

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
                value.DoneChanged += async (_, change) => await ApplyLaterDoneChangeAsync(change);
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
    /// Raised when a real session runs with more tasks than it has places for (carried over, or a session started from the phone or
    /// watch): the PC shows "Pick N for this session".
    /// </summary>
    public event EventHandler? PickRequired;

    /// <summary>The Tasks settings (actions per session, the next step takes the slot); set by the app. Null: the defaults.</summary>
    public TaskSettingsService? Settings { get; set; }

    /// <summary>
    /// How many actions This session holds (ADR 76): the running session's own number (the setting as it was at its start), else the
    /// Tasks setting (2 to 5, 3 by default). A stepless task is one action, a task with steps one per step in focus
    /// (<see cref="SessionActions"/>). A test session has no limit.
    /// </summary>
    public int ActionCap => Current.SessionStart is not null && Current.ActionCap is int cap ? cap : Settings?.SessionActions ?? SessionActions.DefaultCap;

    /// <summary>
    /// Actions this session may hold: <see cref="ActionCap"/> plus the "Add anyway" adds approved for it (those carry a reason and
    /// stay; only carried-over extras are picked down).
    /// </summary>
    public int ActionAllowance => AllowanceOf(Current);

    /// <summary>
    /// The actions the checklist holds: the running one's by its focused steps (<see cref="SessionActions.Used"/>), the draft's by
    /// the task list's focus (a task in This session with steps in focus, one per step; any other task one). Done tasks hold one.
    /// </summary>
    public int ActionsUsed => Current.SessionStart is not null ? SessionActions.Used(Current) : DraftActions();

    /// <summary>The running session's numbered places, in order (empty for a draft): the data of This session's Quiet view.</summary>
    public IReadOnlyList<SessionSlot> Slots => Current.SessionStart is not null ? SessionActions.Slots(Current) : [];

    /// <summary>
    /// A real session runs with more tasks (open and done) than <see cref="ActionAllowance"/>, and the user has not picked yet. Each
    /// task holds at least one action; steps in focus beyond that are trimmed to fit instead (<see cref="SessionActions.FittedToCap"/>).
    /// </summary>
    public bool NeedsPick => IsLive && _sessions.State.IsRealSession && Current.Tasks.Count > ActionAllowance;

    /// <summary>The open tasks the pick prompt lists, in list order (done ones never go).</summary>
    public IReadOnlyList<SessionTask> PickCandidates => Current.Tasks.Where(task => !task.IsDone).ToList();

    /// <summary>How many of the <see cref="PickCandidates"/> may stay: the allowance less the done tasks (one action each).</summary>
    public int PickKeepCount => Math.Max(0, ActionAllowance - Current.Tasks.Count(task => task.IsDone));

    /// <summary>True when the pick sends the tasks it leaves out to Queued (the task list); false: they are parked as come-backs.</summary>
    public bool PickSendsToQueued => _taskStore is not null;

    /// <summary>
    /// Sends the tasks of <paramref name="shown"/> (the rows the prompt listed) that are not in <paramref name="keep"/> to Queued (to
    /// Come back to before the task list), when they are still open; each counts as taken off open (ADR 25), as parking did. Done
    /// tasks and tasks added after the prompt opened are left alone. The kept ones' steps in focus are then trimmed to fit.
    /// </summary>
    public async Task KeepOnlyAsync(IReadOnlySet<Guid> keep, IReadOnlyCollection<Guid> shown)
    {
        foreach (Guid id in shown.Where(id => !keep.Contains(id)))
        {
            if (Current.Tasks.Any(task => task.Id == id && !task.IsDone))
            {
                await SendToQueuedAsync(id);
            }
        }

        if (Current.SessionStart is not null && SessionActions.Used(Current) > ActionAllowance)
        {
            await SetAsync(SessionActions.FittedToCap(Current, ActionAllowance));
        }
    }

    /// <summary>
    /// Brings the open step <paramref name="stepId"/> of the running checklist into focus (the target on a step). With no free place
    /// nothing changes and the answer is <see cref="SessionFocusResult.Full"/>: the caller offers "This session is full".
    /// </summary>
    public async Task<SessionFocusResult> FocusStepAsync(Guid stepId)
    {
        if (Current.SessionStart is null)
        {
            return SessionFocusResult.NotFound;
        }

        int allowance = _sessions.State.IsTest ? int.MaxValue : ActionAllowance;
        (SessionChecklist focused, SessionFocusResult result) = SessionActions.Focus(Current, stepId, allowance);
        if (result == SessionFocusResult.Focused && !ReferenceEquals(focused, Current))
        {
            await SetAsync(focused);
        }

        return result;
    }

    /// <summary>
    /// "This session is full", Swap: makes the place <paramref name="slot"/> free. A focused step leaves focus while its task has another
    /// in focus; otherwise the task leaves the running checklist for Queued (counted as taken off open, ADR 25, so it never makes "All
    /// tasks done" easier). The caller then brings the newcomer in. False when the place is gone.
    /// </summary>
    public async Task<bool> SwapOutAsync(SessionSlot slot)
    {
        if (Current.SessionStart is null || Current.Tasks.All(task => task.Id != slot.TaskId))
        {
            return false;
        }

        SessionTask? leaving = Labelled(Current).FirstOrDefault(task => task.Id == slot.TaskId);
        if (leaving is null || leaving.IsDone)
        {
            // A finished task holds its place for good.
            return false;
        }

        (SessionChecklist swapped, Guid? left) = SessionActions.SwapOut(Current, slot);
        if (left is null)
        {
            await SetAsync(swapped);
            return true;
        }

        if (_taskStore is not null)
        {
            // The checklist without it is worked out again once it is in Queued: a tick or a remote edit meanwhile stays.
            await MoveOutOfChecklistAsync(leaving, TaskBin.Queued, current => SessionActions.Cleaned(current.Remove(leaving.Id)));
        }
        else
        {
            await SetAsync(swapped);
            await QueueOrParkAsync(leaving);
        }

        return true;
    }

    /// <summary>
    /// The swap's order rule (the newcomer takes the swapped place's number): the running checklist's task <paramref name="id"/> goes in
    /// front of <paramref name="beforeId"/> (<see cref="SessionActions.PlacedBefore"/>; its label stays). Nothing else changes.
    /// </summary>
    public async Task PlaceBeforeAsync(Guid id, Guid? beforeId)
    {
        if (Current.SessionStart is null)
        {
            return;
        }

        SessionChecklist placed = SessionActions.PlacedBefore(Current, id, beforeId);
        if (!ReferenceEquals(placed, Current))
        {
            await SetAsync(placed);
        }
    }

    /// <summary>
    /// An open task of the checklist goes to <paramref name="bin"/> of the task list (Queued after a swap or the pick, To sort when
    /// parked) as the same task, with its steps and all it had: put there first, then taken off the checklist
    /// (<paramref name="without"/>, by default <see cref="SessionChecklist.Parked"/>, which counts an open task of a running session as
    /// taken off open, ADR 25), worked out from the checklist as it is after the put back; a task ticked meanwhile stays done in the
    /// checklist and is taken back off the list. A stop in between leaves it in both places, and the same id makes doing it again harmless.
    /// </summary>
    private async Task MoveOutOfChecklistAsync(SessionTask labelled, TaskBin bin, Func<SessionChecklist, SessionChecklist>? without = null)
    {
        // Sent as this service's own change: the checklist still holds the task, so the list mustn't take it as consumed meanwhile.
        await _taskStore!.PutBackAsync(this, [labelled with { DoneAt = null }], bin);
        SessionTask? now = Current.Tasks.FirstOrDefault(task => task.Id == labelled.Id);
        if (now is { IsDone: true })
        {
            // Ticked meanwhile (another device): it stays done in the checklist, holding its place, and leaves the list again; no drop.
            await _taskStore.TakeBackAsync(this, labelled.Id);
        }
        else if (now is not null)
        {
            await SetAsync(without is null ? Current.Parked(labelled.Id) : without(Current));
        }

        ReadFromTaskStore();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // An open task of the running checklist leaves it for Queued (or, before the task list, for Come back to).
    private async Task SendToQueuedAsync(Guid taskId)
    {
        if (_taskStore is null)
        {
            await ParkAsync(taskId);
            return;
        }

        SessionTask? leaving = Labelled(Current).FirstOrDefault(task => task.Id == taskId);
        if (leaving is null)
        {
            return;
        }

        await MoveOutOfChecklistAsync(leaving, TaskBin.Queued);
    }

    private async Task QueueOrParkAsync(SessionTask task)
    {
        if (_taskStore is not null)
        {
            // Every view reads the list again (the next session's list shows it in Queued).
            await _taskStore.PutBackAsync(null, [task with { DoneAt = null }], TaskBin.Queued);
            return;
        }

        if (Comebacks is not null)
        {
            int openSteps = task.Subtasks.Count(step => !step.IsDone);
            await Comebacks.AddAsync(ParkTextOf(task.Title, openSteps), task.Category, task.CategoryColor, task.Labels);
        }
    }

    // The places a checklist may hold: its cap plus the "Add anyway" adds approved for its session.
    private int AllowanceOf(SessionChecklist checklist)
    {
        int cap = checklist.SessionStart is not null && checklist.ActionCap is int own ? own : Settings?.SessionActions ?? SessionActions.DefaultCap;
        return cap + (checklist.SessionStart is DateTimeOffset start ? OverLimitCount(start) : 0);
    }

    /// <summary>
    /// No session runs: This session's open tasks of the next session's list, in order (the "This session is full" dialog's rows before a
    /// start). Empty while a session runs (its places are <see cref="Slots"/>).
    /// </summary>
    public IReadOnlyList<SessionTask> DraftSessionTasks()
    {
        return Current.SessionStart is null ? ThisSessionDraftTasks(StoredDraftItems()).Where(task => !task.IsDone).ToList() : [];
    }

    /// <summary>No session runs and the next session's list holds the task (in This session or in Queued).</summary>
    public bool IsInDraft(Guid id)
    {
        return Current.SessionStart is null && Current.Tasks.Any(task => task.Id == id);
    }

    /// <summary>
    /// No session runs and This session (the next session's tasks outside Queued) has room for <paramref name="actions"/> more
    /// actions under <see cref="ActionCap"/>. The same count as <see cref="ActionsUsed"/>, so the two never disagree.
    /// </summary>
    public bool HasDraftRoomFor(int actions)
    {
        return Current.SessionStart is null && DraftActions() + actions <= ActionCap;
    }

    // The draft's actions (This session's only): a task in This session of the task list holds one per step in focus
    // (TaskItem.ActionCount), a done one or one the task list doesn't hold one. Queued's tasks hold no place.
    private int DraftActions()
    {
        Dictionary<Guid, TaskItem> stored = StoredDraftItems();
        return ThisSessionDraftTasks(stored).Sum(task => !task.IsDone && stored.TryGetValue(task.Id, out TaskItem? item) ? item.ActionCount : 1);
    }

    // The next session's list's tasks that are This session's: all of them before the task list; with it, not the ones in Queued and
    // not a Queued task finished meanwhile (it sits in Finished today, ADR 76; it never joins a session as done).
    private List<SessionTask> ThisSessionDraftTasks(IReadOnlyDictionary<Guid, TaskItem> stored)
    {
        return Current.Tasks.Where(task => !stored.TryGetValue(task.Id, out TaskItem? item) || BelongsToThisSession(item)).ToList();
    }

    private static bool BelongsToThisSession(TaskItem item)
    {
        return item.Bin switch
        {
            TaskBin.Queued => false,
            TaskBin.Done => item.PrevBin is null or TaskBin.Session,
            _ => true,
        };
    }

    // The task list's live tasks by id (none before the task list exists).
    private Dictionary<Guid, TaskItem> StoredDraftItems()
    {
        if (_taskStore is not { IsActive: true } store)
        {
            return [];
        }

        return store.Lists.Tasks.Where(item => item.Task.IsLive)
            .GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First().Task);
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

    /// <summary>
    /// True when This session's actions are all taken (<see cref="ActionCap"/>, ADR 76): the caller asks to park the next one or for a
    /// reason (never in a test session).
    /// </summary>
    public bool IsAtActionLimit => !_sessions.State.IsTest && ActionsUsed >= ActionCap;

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

    /// <summary>
    /// "Add anyway" for a move into This session that has no room (the swap dialog): records the reason as an over-limit add, exactly as
    /// the session add does (at least <see cref="MinOverLimitReasonLength"/> characters, sealed on this PC, counted for the review).
    /// False when the reason is too short (nothing recorded).
    /// </summary>
    public Task<bool> RecordOverLimitAddAsync(string reason)
    {
        return RecordOverLimitAsync(reason);
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
    /// add: with no free action (past <see cref="ActionAllowance"/>, which counts done tasks) it goes to Queued instead (to Come back
    /// to before the task list, ADR 76), and the answer says so; a task with steps that fits gets its first open steps in focus. Refused when no real session runs here and in the last minute of a phase. A category named in
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
        if (SessionActions.Used(Current) >= ActionAllowance)
        {
            // Past the limit a task goes to Queued (or is parked), never added: only the PC's own "Add anyway" (with a reason) goes over.
            if (_taskStore is { IsActive: true } store)
            {
                Guid? queuedCategory = await ResolveRemoteCategoryAsync(request);
                SessionTask queued = Current.AddWithId(id, title, queuedCategory, steps, request.EstimateMinutes, request.Note).Tasks[^1];
                queued = queued with { Category = _categories.FirstOrDefault(category => category.Id == queuedCategory)?.Name };
                await store.PutBackAsync(null, [queued], TaskBin.Queued);
                return RemoteTaskResult.ParkedInstead($"Queued: this session is full ({ActionAllowance} actions)");
            }

            if (Comebacks is null)
            {
                return RemoteTaskResult.Refused($"This session already has {ActionAllowance} actions.");
            }

            // Parked with the category the phone sent, when it is a sane name and colour.
            string? parkedCategory = request.Category?.Trim() is { Length: > 0 and <= MaxRemoteText } name ? name : null;
            string? parkedColor = request.CategoryColor is { Length: 7 } hex && hex[0] == '#' && hex[1..].All(char.IsAsciiHexDigit) ? hex : null;
            await Comebacks.AddAsync(steps.Count > 0 ? $"{title} (+{steps.Count} {(steps.Count == 1 ? "step" : "steps")})" : title, parkedCategory, parkedColor);
            return RemoteTaskResult.ParkedInstead($"Parked: {ActionAllowance} actions this session");
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

    /// <summary>The other devices' task categories (only devices still in the group), asked for when a picker opens; set by the app. Null: none.</summary>
    public Func<IReadOnlyList<DeviceCategories>>? OtherDeviceCategories { get; set; }

    /// <summary>Raised when what <see cref="OtherDeviceCategories"/> gives has changed (a device's list, a device added or removed).</summary>
    public event EventHandler? OtherCategoriesChanged;

    private string _otherCategoriesSignature = string.Empty;

    /// <summary>The other devices' categories right now, grouped by device.</summary>
    public IReadOnlyList<DeviceCategories> OtherCategories()
    {
        return OtherDeviceCategories?.Invoke() ?? [];
    }

    /// <summary>Tells the pickers to look again, when the other devices' categories are not what they last showed. Call on the UI thread.</summary>
    public void NotifyOtherCategoriesChanged()
    {
        // A colour another device chose later is this PC's colour too (ADR 62).
        if (TaskCategoryColors.TakeNewer(_categories, OtherCategories().SelectMany(device => device.Categories), _time.GetUtcNow()) is IReadOnlyList<TaskCategory> newer)
        {
            _ = SetCategoriesAsync(newer);
        }

        string signature = string.Join(
            "\u0001",
            OtherCategories().Select(device => $"{device.DeviceId:N}\u0002{string.Join("\u0003", device.Categories.Select(category => $"{category.Name}\u0004{category.Color}"))}"));
        if (signature == _otherCategoriesSignature)
        {
            return;
        }

        _otherCategoriesSignature = signature;
        OtherCategoriesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Takes a category another device lists into this PC's list: one with the same name (any case) is this PC's already and keeps its
    /// colour; otherwise it is added with the colour given (none: the next colour no category has). The id of this PC's category; null for a blank name.
    /// </summary>
    public async Task<Guid?> AdoptCategoryAsync(string name, string? color)
    {
        int before = _categories.Count;
        Guid? id = await ResolveRemoteCategoryAsync(new SyncTaskRequest(Category: name, CategoryColor: color));
        if (_categories.Count != before)
        {
            // Set again so it's regrouped, the views see the new category and the sync file lists it.
            await SetAsync(Current);
        }

        return id;
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

    /// <summary>A blank name keeps the old one. Later items and come-backs in the category take the new name (and this PC's colour for it).</summary>
    public async Task RenameCategoryAsync(Guid id, string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        TaskCategory? old = _categories.FirstOrDefault(category => category.Id == id);
        await SetCategoriesAsync(_categories.Select(category => category.Id == id ? category with { Name = trimmed } : category).ToList());
        if (old is not null)
        {
            await RelabelSharedListsAsync(old.Name, trimmed, old.Color);
        }
    }

    /// <summary>
    /// Chooses the category's colour, stamped so the newest choice wins on every device (ADR 62). Later items and come-backs are not
    /// touched (that would re-stamp them on every device); every device shows a category's current colour for an item whose name matches.
    /// </summary>
    public Task RecolorCategoryAsync(Guid id, string color)
    {
        DateTimeOffset now = _time.GetUtcNow();
        return SetCategoriesAsync(_categories.Select(category => category.Id == id ? category with { Color = color, ColorEditedAt = now } : category).ToList());
    }

    /// <summary>
    /// How many things hold the category: this checklist's tasks, Later items and come-backs. Deleting it takes the category off
    /// every one of them (the Later items and come-backs on all the user's devices), so the editor asks first with this number.
    /// </summary>
    public int CategoryUseCount(Guid id)
    {
        TaskCategory? category = _categories.FirstOrDefault(candidate => candidate.Id == id);
        if (category is null)
        {
            return 0;
        }

        return Current.Tasks.Count(task => task.CategoryId == id)
            + (Later?.CountInCategory(category.Name) ?? 0)
            + (Comebacks?.CountInCategory(category.Name) ?? 0);
    }

    /// <summary>Its tasks, Later items and come-backs have no category after it (ended sessions' checklists keep the name and colour they had).</summary>
    public async Task DeleteCategoryAsync(Guid id)
    {
        TaskCategory? old = _categories.FirstOrDefault(category => category.Id == id);
        _categories = _categories.Where(category => category.Id != id).ToList();
        await SaveCategoriesAsync();
        await SetAsync(Current.WithoutCategory(id));
        if (old is not null)
        {
            await RelabelSharedListsAsync(old.Name, null, null);
        }
    }

    // Later items and come-backs carry their category's name themselves (they are shared with the other devices), so a renamed or
    // deleted category is carried over to them as ordinary stamped edits (null new name: no category). A limit: merge works on whole
    // items, so such an edit can win over an older edit made on a device that has not synced yet (see hard-rules.md).
    private async Task RelabelSharedListsAsync(string oldName, string? newName, string? newColor)
    {
        if (Later is not null)
        {
            await Later.RelabelAsync(oldName, newName, newColor);
        }

        if (Comebacks is not null)
        {
            await Comebacks.RelabelAsync(oldName, newName, newColor);
        }
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

        SessionTask? labelled = Labelled(Current).FirstOrDefault(candidate => candidate.Id == taskId);
        if (_taskStore is not null && labelled is not null)
        {
            // ADR 76: the same task moves to To sort with its title, steps, number and History (a task added during the session, with
            // no held copy, keeps its steps too).
            await MoveOutOfChecklistAsync(labelled, TaskBin.Sort);
            return;
        }

        await Comebacks.AddAsync(text, labelled?.Category, labelled?.CategoryColor, labelled?.Labels);
        await SetAsync(Current.Parked(taskId));
    }

    /// <summary>
    /// A come-back becomes a task at the end of the checklist (in its category, if it has one) and is ticked off the come-backs. Past the
    /// limit it needs an <paramref name="overLimitReason"/> (<see cref="MinOverLimitReasonLength"/> characters, counted
    /// like "Add anyway"); false when it was not added.
    /// </summary>
    public async Task<bool> AddFromComebackAsync(Guid comebackId, string? overLimitReason = null)
    {
        if (Comebacks?.Open.FirstOrDefault(comeback => comeback.Id == comebackId) is not Comeback comeback)
        {
            return false;
        }

        if (IsAtActionLimit && !await RecordOverLimitAsync(overLimitReason ?? string.Empty))
        {
            return false;
        }

        Guid? categoryId = comeback.Category is null
            ? null
            : await ResolveRemoteCategoryAsync(new SyncTaskRequest(Category: comeback.Category, CategoryColor: comeback.CategoryColor));
        if (_taskStore is not null)
        {
            // ADR 76: the same task moves (its id, number and History stay); the come-back's tick is then no tick of it.
            await HoldIfLiveAsync(comebackId);
            await SetAsync(Current.AddTask(new SessionTask(comebackId, comeback.Text, null, [], categoryId, Labels: TaskLabels.Clean(comeback.Labels))));
            await Comebacks.DeleteAsync(comebackId);
            return true;
        }

        await AddAsync(comeback.Text, null, categoryId);
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

        // ADR 76: with the task list the same task moves (its id, number, steps and History stay); before it, a new item with its open steps.
        bool keepsId = _taskStore is not null;
        IReadOnlyList<SessionSubtask> steps = keepsId ? task.Subtasks : task.Subtasks.Where(step => !step.IsDone).ToList();
        SyncLaterItem? item = await Later.AddAsync(
            task.Title, steps, task.Category, task.CategoryColor, task.EstimateMinutes, task.Note, dueOn, task.PlannedFor, keepsId ? task.Labels : null, keepsId ? task.Id : null);
        await SetAsync(Current.Parked(taskId));
        return item?.Id;
    }

    /// <summary>The task was moved off an ended session's record (to Later, Today or Come back to), so it is no longer left open.</summary>
    public bool WasMoved(Guid taskId)
    {
        return PastSettled?.Hides(taskId) == true;
    }

    /// <summary>The open tasks of ended sessions (newest session first), for the Tasks page's "Unfinished from past sessions".</summary>
    public IReadOnlyList<PastTask> PastUnfinished()
    {
        Guid? self = SelfId();
        // A task kept on another device (ADR 72) is a stub here; one kept here still shows.
        return _history
            .Where(checklist => checklist.SessionStart is not null)
            .OrderByDescending(checklist => checklist.SessionStart)
            .SelectMany(checklist => checklist.Tasks
                .Where(task => !task.IsDone && task.Title.Length > 0 && (task.LocalTo is null || task.LocalTo == self) && !WasMovedOff(task.Id))
                .Select(task => new PastTask(task, checklist.SessionStart!.Value)))
            .Concat(OtherPastUnfinished?.Invoke() ?? [])
            .OrderByDescending(item => item.SessionStart)
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
            // Recorded before the Later item is added, so the page that redraws on its change already leaves the task out.
            if (PastSettled is not null)
            {
                await PastSettled.SetAsync(task.Id, SyncPastSettled.Moved);
            }

            (string? categoryName, string? categoryColor) = TaskCategoryDisplay.Resolve(_categories, task.CategoryId, task.Category, task.CategoryColor);
            if (await Later.AddAsync(task.Title, openSteps, categoryName, categoryColor, task.EstimateMinutes, task.Note, null, labels: task.Labels) is not null)
            {
                moved++;
            }
            else if (PastSettled is not null)
            {
                await PastSettled.SetAsync(task.Id, SyncPastSettled.Open);
            }
        }

        if (moved > 0)
        {
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
        // ADR 76: a focused step that is finished hands its place to its task's next open step (the setting, on by default).
        await SetAsync(Current.SessionStart is not null
            ? SessionActions.Ticked(Current, id, _time.GetUtcNow(), Settings?.NextStepTakesSlot ?? true)
            : Current.Toggle(id, _time.GetUtcNow()));
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
        await _store.SaveAsync(HistoryKey, _history.Select(Sealed).ToList());
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
        // The draft's tasks are this session's now: they leave the shared next-session list (ADR 48). ADR 76: their copies in the task
        // list are kept here first (a task going back to a list comes back whole), and the session takes This session's focus from them.
        // Queued is not part of the session (spec 05 §1): only This session's tasks start; Queued's stay in the list as they are (not
        // held, not taken off), and so does a Queued task finished before the start.
        List<SessionTask> joining = ThisSessionDraftTasks(StoredDraftItems());
        HashSet<Guid> joiningIds = joining.Select(task => task.Id).ToHashSet();
        IReadOnlyList<Guid> planned = [];
        if (_taskStore is not null)
        {
            await _taskStore.HoldRunningAsync(joiningIds);
            planned = _taskStore.HeldFocus();
        }

        await TombstoneNextTasksAsync(joiningIds);
        SessionChecklist started = (Current with { Tasks = joining }).Started(start) with { ActionCap = Settings?.SessionActions ?? SessionActions.DefaultCap };
        started = SessionActions.WithFocus(started, planned);
        int allowance = AllowanceOf(started);
        // Tasks with steps and none in focus get their first open steps in focus while places are free; extra focus is trimmed to fit.
        started = SessionActions.FittedToCap(SessionActions.Joined(started, started.Tasks.Select(task => task.Id), allowance), allowance);
        await SetAsync(started);
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
        if (title is null && await DeleteForeignPastAsync(id))
        {
            return;
        }

        if (title is null)
        {
            await RecordHistoryDeletionAsync(id);
            // ADR 72: a past session's task deleted here is deleted on every device that lists it.
            if (PastSettled is not null && _history.Any(checklist => !checklist.OutsideSession && checklist.Tasks.Any(task => task.Id == id)))
            {
                await PastSettled.SetAsync(id, SyncPastSettled.Deleted);
            }
            // A ticked Later item has the record's id: deleting the record deletes the item too, so nothing brings the record back.
            if (Later is not null && Later.DoneItems.Any(item => item.Id == id))
            {
                await Later.DeleteAsync(id);
            }
        }

        static SessionTask Apply(SessionTask task, Guid id, string? title) => task with
        {
            Subtasks = task.Subtasks
                .Where(subtask => subtask.Id != id || title is not null)
                .Select(subtask => subtask.Id == id ? subtask with { Title = title! } : subtask)
                .ToList(),
        };

        // Only the checklist that holds the task or step is made again (the others keep their sealed copy).
        _history = _history.Select(checklist => !checklist.Tasks.Any(task => task.Id == id || task.Subtasks.Any(step => step.Id == id))
            ? checklist
            : checklist with
            {
                Tasks = checklist.Tasks
                    .Where(task => task.Id != id || title is not null)
                    .Select(task => task.Id == id ? task with { Title = title! } : Apply(task, id, title))
                    .ToList(),
            }).ToList();
        await _store.SaveAsync(HistoryKey, _history.Select(Sealed).ToList());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ArchiveAsync()
    {
        if (Current.HasTasks)
        {
            DateTimeOffset cutoff = _time.GetUtcNow() - HistoryKept;
            // History keeps each task's category name and colour as they were, so deleting a category later loses nothing.
            // "End and mark the rest unfinished": the open tasks go back to Queued as the same tasks, so the record leaves them out (a task
            // id is in one record at most) and counts them as taken off open (ADR 25; the review's number).
            // Only when they can go back (the task list is open); otherwise the record keeps them, open, as a plain end does.
            SessionChecklist full = Current with { Tasks = Labelled(Current) };
            SessionChecklist recorded = _taskStore is { IsActive: true } ? SessionConsumed.Recorded(full) : full with { UnfinishedToQueued = false };
            SessionChecklist labelled = recorded with { FocusedSteps = null, ActionCap = null };
            _history = [.. _history.Where(item => KeptInHistory(item, cutoff)), labelled];
            await _store.SaveAsync(HistoryKey, _history.Select(Sealed).ToList());
        }

        // "End and mark the rest unfinished": the open tasks go back to Queued as the same tasks (with what they had at the start),
        // before the held copies are let go. They are in no record, so nothing offers them from past sessions.
        await ReturnUnfinishedToQueuedAsync(Current);

        // The next session's list: empty, unless another device added tasks to it while this session ran (ADR 48); a task of
        // the session that just ended is never on it (except the ones just sent back to Queued).
        List<SyncNextTask> remaining = TombstoneConsumed(_nextTasks, _time.GetUtcNow(), out bool tombstoned);
        if (tombstoned)
        {
            _nextTasks = remaining;
            await SaveNextTasksAsync();
        }

        await ShowSharedDraftAsync();
        if (_taskStore is not null)
        {
            await _taskStore.ReleaseRunningAsync();
        }
    }

    // The open tasks of a session ended with "End and mark the rest unfinished" (none otherwise, or before the task list).
    private async Task ReturnUnfinishedToQueuedAsync(SessionChecklist ended)
    {
        if (_taskStore is not { IsActive: true } || ended.SessionStart is null)
        {
            return;
        }

        List<SessionTask> returned = Labelled(ended with { Tasks = SessionConsumed.ReturnedToQueued(ended).ToList() }).ToList();
        if (returned.Count == 0)
        {
            return;
        }

        // Sent as this service's own change, then read back here (the draft shows them in Queued).
        await _taskStore.PutBackAsync(this, returned, TaskBin.Queued);
        ReadFromTaskStore();
    }

    /// <summary>
    /// "End and mark the rest unfinished" is offered: the task list is the source (the tasks go back to Queued as the same tasks) and a
    /// real session runs. It is an outcome of the usual end, never another way to end.
    /// </summary>
    public bool CanEndToQueued => _taskStore is not null && Current.SessionStart is not null && _sessions.State.IsRealSession;

    /// <summary>
    /// The user's end of the running session was confirmed with "End and mark the rest unfinished" (after its wait and reason, or armed
    /// with them): once it ends, its open tasks go back to Queued. Called just before the end itself; it changes nothing about the end.
    /// </summary>
    /// <returns>How many open tasks will go back to Queued (0: nothing to send back, the end is a plain one).</returns>
    public async Task<int> MarkRestUnfinishedAtEndAsync()
    {
        if (!CanEndToQueued)
        {
            return 0;
        }

        if (!Current.UnfinishedToQueued)
        {
            await SetAsync(Current with { UnfinishedToQueued = true });
        }

        return Current.Tasks.Count(task => !task.IsDone);
    }

    /// <summary>
    /// A swap's task came back after the newcomer wasn't let in after all: it no longer counts as taken off open (ADR 25), as an undo that
    /// brings an open task back does. Only while a session runs.
    /// </summary>
    public async Task CountBackFromSwapAsync()
    {
        if (IsLive && Current.DroppedOpen > 0)
        {
            await SetAsync(Current.WithOneLessDropped());
        }
    }

    /// <summary>
    /// The task or step is in the running session's checklist (not the next session's list): undo leaves such a task alone (the session
    /// holds it). A draft task of This session or Queued is an ordinary task of the list.
    /// </summary>
    public bool IsInRunningChecklist(Guid id)
    {
        return Current.SessionStart is not null && IsInChecklist(id);
    }

    /// <summary>
    /// "This session is full", before a swap: whether freeing <paramref name="slot"/> lets the incoming task in through the session's add
    /// (no action free at the cap is refused there) or brings the incoming step into focus. False when the place is gone.
    /// </summary>
    public bool SwapAdmits(SessionSlot slot, Guid? incomingStepId)
    {
        if (Current.SessionStart is null || Current.Tasks.All(task => task.Id != slot.TaskId))
        {
            return false;
        }

        (SessionChecklist swapped, _) = SessionActions.SwapOut(Current, slot);
        if (ReferenceEquals(swapped, Current))
        {
            return false;
        }

        if (incomingStepId is Guid stepId)
        {
            int allowance = _sessions.State.IsTest ? int.MaxValue : ActionAllowance;
            return SessionActions.Focus(swapped, stepId, allowance).Result == SessionFocusResult.Focused;
        }

        return _sessions.State.IsTest || SessionActions.Used(swapped) < ActionCap;
    }

    /// <summary>
    /// "This session is full" before a start: whether moving the draft's task <paramref name="outgoingId"/> to Queued leaves This session room
    /// for <paramref name="incomingActions"/> actions (the same count as <see cref="HasDraftRoomFor"/>).
    /// </summary>
    public bool DraftSwapAdmits(Guid outgoingId, int incomingActions)
    {
        if (Current.SessionStart is not null)
        {
            return false;
        }

        Dictionary<Guid, TaskItem> stored = StoredDraftItems();
        int outgoing = stored.TryGetValue(outgoingId, out TaskItem? item) && !item.IsDone ? item.ActionCount : 1;
        return HasDraftRoomFor(incomingActions - outgoing);
    }

    private async Task SetAsync(SessionChecklist checklist)
    {
        if (checklist.SessionStart is not null && Current.SessionStart is not null)
        {
            // ADR 76: a task with steps that joins the running session gets its first open steps in focus while places are free
            // (a test session has no limit); focus on steps no longer open goes.
            var before = Current.Tasks.Select(task => task.Id).ToHashSet();
            int allowance = _sessions.State.IsTest ? int.MaxValue / 2 : AllowanceOf(checklist);
            checklist = SessionActions.Joined(SessionActions.Cleaned(checklist), checklist.Tasks.Where(task => !before.Contains(task.Id)).Select(task => task.Id), allowance);
        }

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
        // ADR 72: a local item goes out as its stub, kept while it is local.
        return SharedListMerge.ForFile(_nextTasks
            .Where(task => task.DeletedAt is not DateTimeOffset deletedAt || deletedAt > keptFrom || task.LocalTo is not null)
            .OrderBy(task => task.Position));
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

        List<SyncNextTask> merged = SharedListMerge.Merge(_nextTasks, incoming, now, SelfId());
        bool changed = !merged.SequenceEqual(SharedListMerge.Merge(_nextTasks, [], now, SelfId()), new NextTaskComparer());
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
    // Exception: the open tasks of a session ended with "End and mark the rest unfinished" went back to Queued as the same tasks
    // (SessionConsumed), so they stay.
    private List<SyncNextTask> TombstoneConsumed(List<SyncNextTask> items, DateTimeOffset now, out bool tombstoned)
    {
        HashSet<Guid> consumed = SessionConsumed.Ids(_history, Current);

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
                EstimateMinutes: task.EstimateMinutes, Note: task.Note, PlannedFor: task.PlannedFor, Labels: task.Labels, LocalTo: task.LocalTo))
            .ToList();
        return new SessionChecklist(null, false, tasks, tasks.Any(task => !task.IsDone && task.Id == currentId) ? currentId : null);
    }

    // A task that comes with a category this device doesn't have gets it, with the name and colour it came with (ADR 24, 48).
    // A colour change counts as an edit of the category: it restamps the category's tasks (ReconcileNextTasks), so the colour on
    // the category's most recently edited task is the latest one, and a category this device already has takes it. Not once the
    // category has a colour stamp: those travel in the devices' category lists (ADR 62), and a task can carry an old colour with a new edit
    // (an old Later item moved to Today), which would undo the recolour.
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
            else if (sent is not null && categories[index].ColorEditedAt is null && !string.Equals(categories[index].Color, sent, StringComparison.OrdinalIgnoreCase))
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
                && TaskLabels.TieText(live.Labels) == TaskLabels.TieText(task.Labels)
                && live.LocalTo == task.LocalTo
                && live.Position == position;
            if (same)
            {
                continue;
            }

            items[task.Id] = new SyncNextTask(
                task.Id, task.Title, task.DoneAt, task.Category, task.CategoryColor, task.Subtasks.Count > 0 ? task.Subtasks : null,
                SharedListMerge.NextStamp(now, held), null, position, task.EstimateMinutes, task.Note, task.PlannedFor, TaskLabels.Clean(task.Labels), task.LocalTo);
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

    // The tasks that joined the session (all of the list's, before the task list) leave the shared next-session list.
    private async Task TombstoneNextTasksAsync(IReadOnlySet<Guid> joining)
    {
        DateTimeOffset now = _time.GetUtcNow();
        bool Leaves(SyncNextTask task) => task.DeletedAt is null && (_taskStore is null || joining.Contains(task.Id));
        if (!_nextTasks.Any(Leaves))
        {
            return;
        }

        _nextTasks = Pruned(_nextTasks.Select(task => Leaves(task) ? task.Deleted(SharedListMerge.NextStamp(now, task)) : task), now);
        await SaveNextTasksAsync();
    }

    private static List<SyncNextTask> Pruned(IEnumerable<SyncNextTask> items, DateTimeOffset now)
    {
        DateTimeOffset keptFrom = now - SharedListMerge.TombstonesKept;
        return items
            .Where(task => task.DeletedAt is not DateTimeOffset deletedAt || deletedAt > keptFrom || task.LocalTo is not null)
            .OrderBy(task => task.Position)
            .ToList();
    }

    private Task SaveNextTasksAsync()
    {
        if (_taskStore is not null)
        {
            return SendNextTasksToTaskStore();
        }

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
        if (_taskStore is not null)
        {
            return SendCategoriesToTaskStore();
        }

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
