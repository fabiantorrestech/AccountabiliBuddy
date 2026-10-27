using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Platform.Windows.Security;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Writes the local activity history (M7, ADR 8) while "Activity history" is granted in Setup:
/// <list type="bullet">
/// <item>what each monitor shows, as segments (whenever the app runs; classification only during focus);</item>
/// <item>session starts and ends, including early-end reasons;</item>
/// <item>answers to the classify prompt, as labels for future suggestions.</item>
/// </list>
/// Nothing is logged while the user is idle (no keyboard/mouse for <see cref="IdleThreshold"/>), and entries older
/// than <see cref="Retention"/> are deleted. Writes run one after another in the background so the UI never waits
/// on the disk. Use from the UI thread only.
/// </summary>
public sealed class ActivityRecorder : IDisposable
{
    public static TimeSpan IdleThreshold { get; } = TimeSpan.FromMinutes(5);

    public static TimeSpan Retention { get; } = TimeSpan.FromDays(90);

    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(3);

    private readonly IActivityLog _log;
    private readonly PrivacySettingsService _privacy;
    private readonly SessionController _sessions;
    private readonly IUserIdleDetector _idle;
    private readonly TimeProvider _timeProvider;
    private readonly ActivitySegmentTracker _tracker = new();
    private readonly DispatcherTimer _pruneTimer;

    // Writes are chained so they reach the database in the order they happened.
    private Task _writes = Task.CompletedTask;
    private SessionState _lastState = SessionState.Idle;

    public ActivityRecorder(
        IActivityLog log,
        PrivacySettingsService privacy,
        SessionController sessions,
        IUserIdleDetector idle,
        TimeProvider timeProvider)
    {
        _log = log;
        _privacy = privacy;
        _sessions = sessions;
        _idle = idle;
        _timeProvider = timeProvider;

        _sessions.StateChanged += OnSessionStateChanged;
        _sessions.SessionCompleted += OnSessionCompleted;
        _sessions.SessionEnded += OnSessionEnded;
        _sessions.SessionResumed += OnSessionResumed;
        _sessions.SideTaskExtended += OnSideTaskExtended;
        _privacy.Changed += OnPrivacyChanged;

        _pruneTimer = new DispatcherTimer { Interval = PruneInterval };
        _pruneTimer.Tick += (_, _) => Prune();
    }

    /// <summary>Raised (on the UI thread) after new entries were written, e.g. to refresh the History tab.</summary>
    public event EventHandler? LogChanged;

    public bool IsEnabled => _privacy.Current.ActivityLogEnabled;

    public void Start()
    {
        _lastState = _sessions.State;
        Prune();
        _pruneTimer.Start();
    }

    /// <summary>Called on every evaluation with what each monitor shows (from <see cref="EnforcementCoordinator"/>).</summary>
    public void Observe(IReadOnlyList<MonitorEnforcement> decisions, SessionState state, DateTimeOffset now)
    {
        if (!IsEnabled)
        {
            return;
        }

        TimeSpan idleFor = _idle.GetIdleTime();
        if (idleFor >= IdleThreshold)
        {
            // Close what was open at the moment the user went idle, so the idle stretch is not counted.
            WriteSegments(_tracker.CloseAll(now - idleFor));
            return;
        }

        // A test session logs its activity as outside any session, so it never reaches focus time or the review. Neither
        // does time away from the focus clock (a step away, a side task) or the planning stage before it: enforcement still
        // judges it, but it isn't focus.
        bool inFocus = state.IsEnforcing && !state.IsPlanning && !state.IsAway && !state.IsTest;
        Guid? profileId = state.IsRealSession ? state.ProfileId : null;
        IEnumerable<ObservedActivity> observations = decisions.Select(decision => new ObservedActivity(
            decision.Snapshot,
            // Classification is only meaningful against a profile during focus.
            inFocus ? decision.Classification : Classification.Unknown,
            profileId,
            inFocus));

        WriteSegments(_tracker.Observe(observations, now));
    }

    /// <summary>The user answered the classify prompt: keep the answer as a label for future suggestions.</summary>
    public void RecordClassification(ClassifyAnswer answer, Guid profileId)
    {
        if (!IsEnabled)
        {
            return;
        }

        ActivitySnapshot snapshot = answer.Snapshot;
        var label = new ActivityLabel(
            _timeProvider.GetUtcNow(),
            profileId,
            snapshot.ProcessName,
            snapshot.WindowTitle,
            snapshot.Url,
            snapshot.OcrText,
            answer.Rule.Verdict,
            LabelSource.ClassifyPrompt,
            answer.Lifetime,
            answer.Reason,
            _privacy.Current.KeepTitlesAndAddresses);
        Enqueue(() => _log.RecordLabelAsync(label));
    }

    /// <summary>"Delete my data": drops what is being tracked, waits for pending writes, then empties the log.</summary>
    public async Task DeleteAllAsync()
    {
        _tracker.CloseAll(_timeProvider.GetUtcNow());
        await _writes;
        await _log.DeleteAllAsync();
        LogChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<long> CountEntriesAsync()
    {
        return _log.CountEntriesAsync();
    }

    public void Dispose()
    {
        _pruneTimer.Stop();
        _sessions.StateChanged -= OnSessionStateChanged;
        _sessions.SessionCompleted -= OnSessionCompleted;
        _sessions.SessionEnded -= OnSessionEnded;
        _sessions.SessionResumed -= OnSessionResumed;
        _sessions.SideTaskExtended -= OnSideTaskExtended;
        _privacy.Changed -= OnPrivacyChanged;

        // Keep what was on screen up to now; give the disk a moment, but never hang the exit.
        if (IsEnabled)
        {
            WriteSegments(_tracker.CloseAll(_timeProvider.GetUtcNow()));
        }

        _writes.Wait(FlushTimeout);
    }

    private void OnSessionStateChanged(object? sender, SessionState state)
    {
        SessionState previous = _lastState;
        _lastState = state;

        // A test session leaves no events: not its start, its steps away, or its end.
        if (previous.IsTest || state.IsTest)
        {
            return;
        }

        // A resumed session was logged as started before the app stopped.
        if (!previous.IsActive && state.IsActive && IsEnabled && !_sessions.IsResuming)
        {
            var started = new SessionEvent(
                _timeProvider.GetUtcNow(), SessionEventKind.Started, state.Mode, state.ProfileId, state.FocusEndsAt);
            Enqueue(() => _log.RecordSessionEventAsync(started));
        }

        // The planning stage is its own stretch in History (never focus): a resumed one was logged before the app stopped.
        if (!_sessions.IsResuming && IsEnabled && state.IsActive && previous.IsPlanning != state.IsPlanning && (state.IsPlanning || previous.IsActive))
        {
            var planning = new SessionEvent(
                _timeProvider.GetUtcNow(), state.IsPlanning ? SessionEventKind.PlanningStarted : SessionEventKind.PlanningEnded, state.Mode, state.ProfileId);
            Enqueue(() => _log.RecordSessionEventAsync(planning));
        }

        // Step away: a resumed session that was away is already logged as away.
        if (previous.IsActive && state.IsActive && previous.IsAway != state.IsAway && IsEnabled && !_sessions.IsResuming)
        {
            var change = state.Away is AwayKind kind
                ? new SessionEvent(state.AwaySince ?? _timeProvider.GetUtcNow(), SessionEventKind.AwayStarted, state.Mode, state.ProfileId, Reason: kind.ToString())
                : new SessionEvent(_timeProvider.GetUtcNow(), SessionEventKind.AwayEnded, state.Mode, state.ProfileId);
            Enqueue(() => _log.RecordSessionEventAsync(change));
        }
    }

    private void OnSessionCompleted(object? sender, SessionState finished)
    {
        if (IsEnabled && !finished.IsTest)
        {
            var completed = new SessionEvent(
                _timeProvider.GetUtcNow(), SessionEventKind.Completed, finished.Mode, finished.ProfileId);
            Enqueue(() => _log.RecordSessionEventAsync(completed));
        }
    }

    private void OnSessionEnded(object? sender, SessionEnd end)
    {
        if (IsEnabled && !end.Ended.IsTest)
        {
            SessionEventKind kind = end.EarlyEndReason is not null ? SessionEventKind.EndedEarly : SessionEventKind.Ended;
            var ended = new SessionEvent(
                _timeProvider.GetUtcNow(), kind, end.Ended.Mode, end.Ended.ProfileId, Reason: end.EarlyEndReason);
            Enqueue(() => _log.RecordSessionEventAsync(ended));
        }
    }

    // A test session leaves no events, so its side tasks leave none either.
    private void OnSideTaskExtended(object? sender, SideTaskExtension extension)
    {
        if (IsEnabled && !extension.State.IsTest)
        {
            var extended = new SessionEvent(
                _timeProvider.GetUtcNow(), SessionEventKind.SideTaskExtended, extension.State.Mode, extension.State.ProfileId, Reason: extension.Reason);
            Enqueue(() => _log.RecordSessionEventAsync(extended));
        }
    }

    private void OnSessionResumed(object? sender, SessionResume resume)
    {
        if (IsEnabled)
        {
            SessionState state = resume.State;
            var interrupted = new SessionEvent(resume.LastAliveAt, SessionEventKind.Interrupted, state.Mode, state.ProfileId);
            var resumed = new SessionEvent(resume.ResumedAt, SessionEventKind.Resumed, state.Mode, state.ProfileId);
            Enqueue(() => _log.RecordSessionEventAsync(interrupted));
            Enqueue(() => _log.RecordSessionEventAsync(resumed));
        }
    }

    private void OnPrivacyChanged(object? sender, PrivacySettings settings)
    {
        if (!settings.ActivityLogEnabled)
        {
            // Revoked: stop at once and drop what was still being tracked.
            _tracker.CloseAll(_timeProvider.GetUtcNow());
        }
    }

    private void WriteSegments(IReadOnlyList<ActivitySegment> segments)
    {
        bool keepProtected = _privacy.Current.KeepTitlesAndAddresses;
        foreach (ActivitySegment segment in segments)
        {
            ActivitySegment written = segment with { KeepProtectedText = keepProtected };
            Enqueue(() => _log.RecordSegmentAsync(written));
        }
    }

    private void Prune()
    {
        DateTimeOffset cutoff = _timeProvider.GetUtcNow() - Retention;
        Enqueue(() => _log.DeleteOlderThanAsync(cutoff), notify: false);
    }

    private void Enqueue(Func<Task> write, bool notify = true)
    {
        _writes = RunAfterAsync(_writes, write, notify);
    }

    private async Task RunAfterAsync(Task previous, Func<Task> write, bool notify)
    {
        await previous.ConfigureAwait(false);
        try
        {
            await write().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or CryptographicFailureException)
        {
            // The log is a convenience: a failed write must never disturb a focus session.
            return;
        }

        if (notify)
        {
            Dispatcher.UIThread.Post(() => LogChanged?.Invoke(this, EventArgs.Empty));
        }
    }
}
