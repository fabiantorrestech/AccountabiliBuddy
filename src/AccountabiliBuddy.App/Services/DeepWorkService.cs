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
        _sessions.StateChanged += OnStateChanged;
        _sessions.Ticked += OnTicked;
    }

    public GoalProgress Progress { get; private set; } = new(TimeSpan.Zero, GoalSettings.Default.DailyMinutes);

    /// <summary>Raised when <see cref="Progress"/> changes (at most once a minute while a session runs).</summary>
    public event EventHandler? Changed;

    /// <summary>Raised once a day when today's deep work reaches the goal.</summary>
    public event EventHandler<GoalProgress>? GoalReached;

    /// <summary>Reads this PC's sessions again, then recomputes.</summary>
    public async Task ReloadAsync()
    {
        _reloading = true;
        try
        {
            _records = await _loadRecords();
            _loadedAt = _sessions.Now;
        }
        finally
        {
            _reloading = false;
        }

        CheckNow();
    }

    /// <summary>Recomputes today's progress. Runs every second.</summary>
    public void CheckNow()
    {
        DateTimeOffset now = _sessions.Now;
        TimeZoneInfo zone = _sessions.TimeProvider.LocalTimeZone;
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        DeepWorkDay day = DeepWork.Days(_records, _others(), _isOmitted, _sessions.State.IsActive, today, today, now, zone)[0];
        var progress = new GoalProgress(TimeSpan.FromMinutes(Math.Floor(day.Focused.TotalMinutes)), _goal.Current.DailyMinutes);

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
        _sessions.StateChanged -= OnStateChanged;
        _sessions.Ticked -= OnTicked;
    }

    private void OnStateChanged(object? sender, SessionState state)
    {
        _ = ReloadAsync();
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
