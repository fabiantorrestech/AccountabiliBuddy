using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Today's deep work against the daily goal (redesign phase 4): this PC's counted sessions and the other devices' synced
/// ones (<see cref="DeepWork"/>), re-read every minute and when a session starts or ends, recomputed every second.
/// <see cref="GoalReached"/> is raised once a day, the moment the total reaches the goal (not at launch when it already
/// had). Use from the UI thread only.
/// </summary>
public sealed class DeepWorkService : IDisposable
{
    private static readonly TimeSpan ReloadEvery = TimeSpan.FromMinutes(1);

    private readonly SessionController _sessions;
    private readonly GoalService _goal;
    private readonly Func<Task<IReadOnlyList<SessionRecord>>> _loadRecords;
    private readonly Func<IEnumerable<SyncSession>> _others;
    private readonly Func<DateTimeOffset, bool> _isOmitted;
    private IReadOnlyList<SessionRecord> _records = Array.Empty<SessionRecord>();
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;
    private DateOnly? _announcedOn;
    private bool _computed;
    private bool _reloading;
    private bool _loaded;
    private int _loadNumber;
    private DateOnly _countedDay;
    private TimeSpan _countedYesterday;

    /// <param name="loadRecords">This PC's sessions around today, newest first (<see cref="SessionHistoryBuilder.Build"/>).</param>
    /// <param name="others">The other devices' sessions (ADR 22).</param>
    /// <param name="isOmitted">Sessions the user left out of the totals, by start time.</param>
    public DeepWorkService(
        SessionController sessions,
        GoalService goal,
        Func<Task<IReadOnlyList<SessionRecord>>> loadRecords,
        Func<IEnumerable<SyncSession>> others,
        Func<DateTimeOffset, bool> isOmitted)
    {
        _sessions = sessions;
        _goal = goal;
        _loadRecords = loadRecords;
        _others = others;
        _isOmitted = isOmitted;
        _sessions.Ticked += OnTicked;
    }

    public GoalProgress Progress { get; private set; } = new(TimeSpan.Zero, GoalSettings.Default.DailyMinutes);

    /// <summary>
    /// What earns the work-hours unlock (design K): only the other devices' recorded sessions, never this PC's own
    /// nor sessions added by hand, so the personal PC opens when the work PC reports the goal met.
    /// </summary>
    public GoalProgress UnlockProgress { get; private set; } = new(TimeSpan.Zero, GoalSettings.Default.DailyMinutes);

    /// <summary>
    /// Counted focus on a date, in whole minutes, for the day plan's Budget days (<see cref="DayWorkWindows"/>): today's
    /// across the user's devices (test sessions, side tasks and planning never count, as in <see cref="Progress"/>) and
    /// yesterday's; zero for any other date or before the first read.
    /// </summary>
    public TimeSpan FocusedOn(DateOnly date)
    {
        return !_loaded ? TimeSpan.Zero
            : date == _countedDay ? Progress.Focused
            : date == _countedDay.AddDays(-1) ? _countedYesterday
            : TimeSpan.Zero;
    }

    /// <summary>Raised when <see cref="Progress"/> changes (at most once a minute while a session runs).</summary>
    public event EventHandler? Changed;

    /// <summary>Raised once a day when today's deep work reaches the goal.</summary>
    public event EventHandler<GoalProgress>? GoalReached;

    /// <summary>Reads this PC's sessions again, then recomputes.</summary>
    public async Task ReloadAsync()
    {
        int number = ++_loadNumber;
        _reloading = true;
        try
        {
            IReadOnlyList<SessionRecord> records = await _loadRecords();
            if (number != _loadNumber)
            {
                return; // A newer load is on its way.
            }

            _records = records;
            _loadedAt = _sessions.Now;
            _loaded = true;
        }
        finally
        {
            _reloading = false;
        }

        CheckNow();
    }

    /// <summary>The activity log was written (a session started or ended, or one was added): read it again.</summary>
    public void NotifyLogChanged()
    {
        _ = ReloadAsync();
    }

    /// <summary>Recomputes today's progress. Runs every second.</summary>
    public void CheckNow()
    {
        if (!_loaded)
        {
            return; // Nothing to count yet: no "Goal met" or unlock from an empty list.
        }

        DateTimeOffset now = _sessions.Now;
        TimeZoneInfo zone = _sessions.TimeProvider.LocalTimeZone;
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        // Only the other devices' sessions near today (each device keeps up to 90 days).
        DateTimeOffset from = now.AddDays(-2);
        List<SyncSession> others = _others().Where(session => (session.End ?? now) > from).ToList();
        // Yesterday too: a Budget day that runs past midnight is still counted against yesterday (see FocusedOn).
        IReadOnlyList<DeepWorkDay> counted = DeepWork.Days(_records, others, _isOmitted, _sessions.State.IsRealSession, today.AddDays(-1), today, now, zone);
        DeepWorkDay day = counted[1];
        _countedDay = today;
        _countedYesterday = TimeSpan.FromMinutes(Math.Floor(counted[0].Focused.TotalMinutes));
        var progress = new GoalProgress(TimeSpan.FromMinutes(Math.Floor(day.Focused.TotalMinutes)), _goal.Current.DailyMinutes);
        DeepWorkDay earned = DeepWork.Days([], others.Where(session => !session.Added), _isOmitted, false, today, today, now, zone)[0];
        UnlockProgress = new GoalProgress(TimeSpan.FromMinutes(Math.Floor(earned.Focused.TotalMinutes)), _goal.Current.DailyMinutes);

        bool firstTime = !_computed;
        _computed = true;
        if (progress.Met && _announcedOn != today)
        {
            _announcedOn = today;
            if (!firstTime)
            {
                GoalReached?.Invoke(this, progress);
            }
        }

        if (progress != Progress)
        {
            Progress = progress;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _sessions.Ticked -= OnTicked;
    }

    private void OnTicked(object? sender, EventArgs e)
    {
        if (!_reloading && _sessions.Now - _loadedAt >= ReloadEvery)
        {
            _ = ReloadAsync();
            return;
        }

        CheckNow();
    }
}
