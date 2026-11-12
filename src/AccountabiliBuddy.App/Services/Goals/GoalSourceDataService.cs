using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Goals;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// What this PC's goals are counted from (brief §5.4, plan §3b 5), for the Goals page and Today's goals: this PC's own counted
/// sessions with their checklists (<see cref="GoalFocusAttribution.FromRecords"/>, the same records and omitted-session rule as the
/// day bar, History and the guards) plus each other device's synced sessions (<see cref="GoalFocusAttribution.FromSynced"/>, one
/// call per device), and the goals' own entries from <see cref="GoalStore"/>. Event-driven: the log is read again only when it was
/// written (<see cref="ReloadAsync"/>); a store change, a received file or a minute of focus only recomputes from what is held
/// (<see cref="Recompute"/>). Nothing here feeds a guard: guards, the goal unlock and focusDay read <see cref="DeepWorkService"/>.
/// Use from the UI thread only.
/// </summary>
public sealed class GoalSourceDataService
{
    /// <summary>The longest history read back (the heatmap's year plus paging; older days count as nothing).</summary>
    public static readonly TimeSpan LongestHistory = TimeSpan.FromDays(3 * 366);

    private readonly Func<DateTimeOffset, DateTimeOffset, Task<IReadOnlyList<SessionRecord>>> _loadRecords;
    private readonly Func<IEnumerable<IEnumerable<SyncSession>>> _otherDevices;
    private readonly Func<DateTimeOffset, IReadOnlyList<SessionTask>?> _tasksOf;
    private readonly Func<DateTimeOffset, bool> _isOmitted;
    private readonly Func<bool> _sessionRunning;
    private readonly GoalStore _store;
    private readonly TimeProvider _time;
    private IReadOnlyList<SessionRecord> _records = [];
    private int _loadNumber;

    /// <param name="loadRecords">This PC's session records between two times (gaps applied), as the day bar reads them.</param>
    /// <param name="otherDevices">Each other device's synced sessions, open ones closed at their file's time (one list per device).</param>
    /// <param name="tasksOf">A session's checklist (labelled tasks) by its start, or null for none (ADR 24, as the sync file's).</param>
    /// <param name="isOmitted">A session the user left out of the counts.</param>
    /// <param name="sessionRunning">A real session runs now (its open record counts up to now).</param>
    public GoalSourceDataService(
        Func<DateTimeOffset, DateTimeOffset, Task<IReadOnlyList<SessionRecord>>> loadRecords,
        Func<IEnumerable<IEnumerable<SyncSession>>> otherDevices,
        Func<DateTimeOffset, IReadOnlyList<SessionTask>?> tasksOf,
        Func<DateTimeOffset, bool> isOmitted,
        Func<bool> sessionRunning,
        GoalStore store,
        TimeProvider time)
    {
        _loadRecords = loadRecords;
        _otherDevices = otherDevices;
        _tasksOf = tasksOf;
        _isOmitted = isOmitted;
        _sessionRunning = sessionRunning;
        _store = store;
        _time = time;
        Current = new GoalSourceData([], [], time.LocalTimeZone);
        _store.Changed += (_, _) => Recompute();
    }

    /// <summary>The data the goals are counted from now (empty until the first <see cref="ReloadAsync"/>).</summary>
    public GoalSourceData Current { get; private set; }

    /// <summary>True once this PC's records were read once.</summary>
    public bool IsLoaded { get; private set; }

    public TimeZoneInfo Zone => _time.LocalTimeZone;

    public DateTimeOffset Now => _time.GetUtcNow();

    /// <summary>Today on this PC's clock (the trusted clock the app is given).</summary>
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, Zone).DateTime);

    /// <summary>Raised after <see cref="Current"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Reads this PC's records again (from the oldest live goal's first day), then recomputes.</summary>
    public async Task ReloadAsync()
    {
        int number = ++_loadNumber;
        DateTimeOffset now = Now;
        DateTimeOffset oldest = _store.Lists.LiveGoals.Select(goal => goal.CreatedAt).DefaultIfEmpty(now).Min();
        DateTimeOffset from = oldest.AddDays(-1);
        if (now - from > LongestHistory)
        {
            from = now - LongestHistory;
        }

        IReadOnlyList<SessionRecord> records = await _loadRecords(from, now.AddDays(1));
        if (number != _loadNumber)
        {
            return; // A newer read is on its way.
        }

        _records = records;
        IsLoaded = true;
        Recompute();
    }

    /// <summary>Recomputes from the records held, the other devices' sessions and the store's entries (a store change, a file, a minute).</summary>
    public void Recompute()
    {
        DateTimeOffset now = Now;
        TimeZoneInfo zone = Zone;
        var focus = new List<GoalFocusSession>(GoalFocusAttribution.FromRecords(_records, _tasksOf, _isOmitted, _sessionRunning(), now, zone));
        foreach (IEnumerable<SyncSession> device in _otherDevices())
        {
            focus.AddRange(GoalFocusAttribution.FromSynced(device, now, zone));
        }

        Current = new GoalSourceData(focus, _store.Lists.Entries, zone);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
