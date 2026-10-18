using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// State machine for focus sessions: Manual, Pomodoro and Scheduled.
/// Time only moves forward when <see cref="Tick"/> is called (e.g. once a second from a timer),
/// so tests can drive it with a fake <see cref="TimeProvider"/>.
/// Only one session runs at a time; a scheduled block starts only when no session is active.
/// Not thread-safe: call from a single thread.
/// </summary>
public sealed class SessionEngine
{
    private readonly TimeProvider _timeProvider;
    private WeeklySchedule _schedule = WeeklySchedule.Empty;
    private PomodoroSettings? _pomodoroSettings;

    // When set, the last focus cycle is followed by its break before the session ends.
    private bool _breakAfterLastCycle;

    // Set when a scheduled session is ended early, so the next Tick does not restart it.
    private DateTimeOffset? _scheduleSuppressedUntil;

    // Set while an unlock is in effect: no scheduled block starts before this.
    private DateTimeOffset? _scheduleHeldUntil;

    public SessionEngine(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public SessionState State { get; private set; } = SessionState.Idle;

    public event EventHandler<SessionState>? StateChanged;

    /// <summary>
    /// Raised (with the state it ended from) when a session runs out on its own: a timed manual session or a
    /// Pomodoro with a cycle limit reached its end, or a scheduled block finished. Not raised by <see cref="EndSession"/>.
    /// </summary>
    public event EventHandler<SessionState>? SessionCompleted;

    private DateTimeOffset Now => _timeProvider.GetUtcNow();

    /// <summary>Replaces the weekly schedule. Takes effect on the next <see cref="Tick"/>.</summary>
    public void SetSchedule(WeeklySchedule schedule)
    {
        _schedule = schedule;
    }

    /// <summary>
    /// Stops scheduled blocks from starting before <paramref name="until"/> (an unlock); null lifts the hold.
    /// Does not end a running session. Takes effect on the next <see cref="Tick"/>.
    /// </summary>
    public void HoldSchedule(DateTimeOffset? until)
    {
        _scheduleHeldUntil = until;
    }

    /// <param name="duration">Null runs until <see cref="EndSession"/> is called.</param>
    /// <returns>False if a session is already running.</returns>
    public bool StartManual(Guid profileId, TimeSpan? duration = null)
    {
        if (State.IsActive)
        {
            return false;
        }

        if (duration is TimeSpan length && length <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");
        }

        DateTimeOffset now = Now;
        SetState(new SessionState(
            SessionMode.Manual,
            SessionPhase.Focus,
            profileId,
            PhaseStartedAt: now,
            PhaseEndsAt: duration is null ? null : now + duration,
            CompletedFocusCycles: 0));
        return true;
    }

    /// <param name="breakAfterLastCycle">With a cycle limit: also run the break after the last focus cycle
    /// before ending (e.g. "one focus, one break").</param>
    /// <returns>False if a session is already running.</returns>
    public bool StartPomodoro(Guid profileId, PomodoroSettings settings, bool breakAfterLastCycle = false)
    {
        if (State.IsActive)
        {
            return false;
        }

        ValidatePomodoro(settings);
        _pomodoroSettings = settings;
        _breakAfterLastCycle = breakAfterLastCycle;

        DateTimeOffset now = Now;
        SetState(new SessionState(
            SessionMode.Pomodoro,
            SessionPhase.Focus,
            profileId,
            PhaseStartedAt: now,
            PhaseEndsAt: now + settings.FocusLength,
            CompletedFocusCycles: 0));
        return true;
    }

    /// <summary>
    /// Ends the current session immediately. Callers must apply <see cref="BypassPolicy"/> first
    /// when the user is ending a focus phase early.
    /// </summary>
    /// <param name="suppressSchedule">For a scheduled session: keep its block from restarting until the block ends.
    /// False when an unlock's hold (<see cref="HoldSchedule"/>) already decides when blocks start again.</param>
    public void EndSession(bool suppressSchedule = true)
    {
        if (!State.IsActive)
        {
            return;
        }

        if (State.Mode == SessionMode.Scheduled && suppressSchedule)
        {
            _scheduleSuppressedUntil = State.PhaseEndsAt;
        }

        SetState(SessionState.Idle);
    }

    /// <summary>Everything needed to carry on with the current session after the app restarts.</summary>
    public SessionCheckpoint CreateCheckpoint()
    {
        return new SessionCheckpoint(State, _pomodoroSettings, _breakAfterLastCycle, _scheduleSuppressedUntil);
    }

    /// <summary>
    /// Puts back a session saved with <see cref="CreateCheckpoint"/>, e.g. after a crash or restart.
    /// Call <see cref="Tick"/> afterwards to catch up on phases that ran out while the app was closed.
    /// </summary>
    /// <returns>True if a session is running again; false if the checkpoint had none, was invalid,
    /// or a session is already running.</returns>
    public bool Restore(SessionCheckpoint checkpoint)
    {
        if (State.IsActive)
        {
            return false;
        }

        // Kept even without a session, so a scheduled block ended early does not restart.
        _scheduleSuppressedUntil = checkpoint.ScheduleSuppressedUntil;

        SessionState saved = checkpoint.State;
        if (!saved.IsActive || saved.PhaseStartedAt is null)
        {
            return false;
        }

        if (saved.Mode == SessionMode.Pomodoro)
        {
            if (checkpoint.Pomodoro is null || !IsValidPomodoro(checkpoint.Pomodoro))
            {
                return false;
            }

            _pomodoroSettings = checkpoint.Pomodoro;
            _breakAfterLastCycle = checkpoint.BreakAfterLastCycle;
        }

        SetState(saved);
        return true;
    }

    /// <summary>Advances phases that have run out and starts scheduled blocks that have begun.</summary>
    public void Tick()
    {
        DateTimeOffset now = Now;
        SessionState before = State;

        switch (State.Mode)
        {
            case SessionMode.Manual:
            case SessionMode.Scheduled:
                if (State.PhaseEndsAt is DateTimeOffset end && now >= end)
                {
                    SetState(SessionState.Idle);
                }
                break;

            case SessionMode.Pomodoro:
                AdvancePomodoro(now);
                break;
        }

        if (before.IsActive && !State.IsActive)
        {
            SessionCompleted?.Invoke(this, before);
        }

        if (!State.IsActive)
        {
            TryStartScheduled(now);
        }
    }

    private void AdvancePomodoro(DateTimeOffset now)
    {
        PomodoroSettings settings = _pomodoroSettings
            ?? throw new InvalidOperationException("Pomodoro session has no settings.");

        SessionState state = State;

        // Loop so that waking from sleep catches up through every phase that was missed.
        while (state.Mode == SessionMode.Pomodoro
               && state.PhaseEndsAt is DateTimeOffset phaseEnd
               && now >= phaseEnd)
        {
            state = NextPomodoroPhase(state, settings, phaseEnd, _breakAfterLastCycle);
        }

        SetState(state);
    }

    private static SessionState NextPomodoroPhase(
        SessionState current, PomodoroSettings settings, DateTimeOffset phaseEnd, bool breakAfterLastCycle)
    {
        if (current.Phase != SessionPhase.Focus)
        {
            // The break after the last cycle has ended.
            if (settings.TotalCycles is int limit && current.CompletedFocusCycles >= limit)
            {
                return SessionState.Idle;
            }

            return current with
            {
                Phase = SessionPhase.Focus,
                PhaseStartedAt = phaseEnd,
                PhaseEndsAt = phaseEnd + settings.FocusLength,
            };
        }

        int completed = current.CompletedFocusCycles + 1;
        if (settings.TotalCycles is int totalCycles && completed >= totalCycles && !breakAfterLastCycle)
        {
            return SessionState.Idle;
        }

        bool isLongBreak = completed % settings.CyclesBeforeLongBreak == 0;
        TimeSpan breakLength = isLongBreak ? settings.LongBreakLength : settings.ShortBreakLength;

        return current with
        {
            Phase = isLongBreak ? SessionPhase.LongBreak : SessionPhase.ShortBreak,
            PhaseStartedAt = phaseEnd,
            PhaseEndsAt = phaseEnd + breakLength,
            CompletedFocusCycles = completed,
        };
    }

    private void TryStartScheduled(DateTimeOffset now)
    {
        if (_scheduleHeldUntil is DateTimeOffset heldUntil && now < heldUntil)
        {
            return;
        }

        if (_scheduleSuppressedUntil is DateTimeOffset suppressedUntil)
        {
            if (now < suppressedUntil)
            {
                return;
            }

            _scheduleSuppressedUntil = null;
        }

        TimeZoneInfo timeZone = _timeProvider.LocalTimeZone;
        DateTime localNow = TimeZoneInfo.ConvertTime(now, timeZone).DateTime;

        ActiveScheduleBlock? active = _schedule.FindActive(localNow);
        if (active is null)
        {
            return;
        }

        SetState(new SessionState(
            SessionMode.Scheduled,
            SessionPhase.Focus,
            active.Block.ProfileId,
            PhaseStartedAt: ToOffset(active.LocalStart, timeZone),
            PhaseEndsAt: ToOffset(active.LocalEnd, timeZone),
            CompletedFocusCycles: 0));
    }

    private void SetState(SessionState newState)
    {
        if (newState == State)
        {
            return;
        }

        State = newState;
        if (newState.Mode != SessionMode.Pomodoro)
        {
            _pomodoroSettings = null;
        }

        StateChanged?.Invoke(this, newState);
    }

    private static DateTimeOffset ToOffset(DateTime localTime, TimeZoneInfo timeZone)
    {
        return new DateTimeOffset(localTime, timeZone.GetUtcOffset(localTime));
    }

    private static bool IsValidPomodoro(PomodoroSettings settings)
    {
        try
        {
            ValidatePomodoro(settings);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void ValidatePomodoro(PomodoroSettings settings)
    {
        if (settings.FocusLength <= TimeSpan.Zero
            || settings.ShortBreakLength <= TimeSpan.Zero
            || settings.LongBreakLength <= TimeSpan.Zero)
        {
            throw new ArgumentException("Pomodoro phase lengths must be positive.", nameof(settings));
        }

        if (settings.CyclesBeforeLongBreak <= 0)
        {
            throw new ArgumentException("Cycles before a long break must be at least 1.", nameof(settings));
        }

        if (settings.TotalCycles is int total && total <= 0)
        {
            throw new ArgumentException("Total cycles must be at least 1 when set.", nameof(settings));
        }
    }
}
