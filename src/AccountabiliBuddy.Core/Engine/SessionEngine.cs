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

    // Set when a scheduled session is ended early, so the next Tick does not restart it.
    private DateTimeOffset? _scheduleSuppressedUntil;

    public SessionEngine(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public SessionState State { get; private set; } = SessionState.Idle;

    public event EventHandler<SessionState>? StateChanged;

    private DateTimeOffset Now => _timeProvider.GetUtcNow();

    /// <summary>Replaces the weekly schedule. Takes effect on the next <see cref="Tick"/>.</summary>
    public void SetSchedule(WeeklySchedule schedule)
    {
        _schedule = schedule;
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

    /// <returns>False if a session is already running.</returns>
    public bool StartPomodoro(Guid profileId, PomodoroSettings settings)
    {
        if (State.IsActive)
        {
            return false;
        }

        ValidatePomodoro(settings);
        _pomodoroSettings = settings;

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
    public void EndSession()
    {
        if (!State.IsActive)
        {
            return;
        }

        if (State.Mode == SessionMode.Scheduled)
        {
            _scheduleSuppressedUntil = State.PhaseEndsAt;
        }

        SetState(SessionState.Idle);
    }

    /// <summary>Advances phases that have run out and starts scheduled blocks that have begun.</summary>
    public void Tick()
    {
        DateTimeOffset now = Now;

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
            state = NextPomodoroPhase(state, settings, phaseEnd);
        }

        SetState(state);
    }

    private static SessionState NextPomodoroPhase(SessionState current, PomodoroSettings settings, DateTimeOffset phaseEnd)
    {
        if (current.Phase != SessionPhase.Focus)
        {
            return current with
            {
                Phase = SessionPhase.Focus,
                PhaseStartedAt = phaseEnd,
                PhaseEndsAt = phaseEnd + settings.FocusLength,
            };
        }

        int completed = current.CompletedFocusCycles + 1;
        if (settings.TotalCycles is int totalCycles && completed >= totalCycles)
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
