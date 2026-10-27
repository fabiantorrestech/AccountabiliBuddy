using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// State machine for the focus sessions the user starts: Manual and Pomodoro. Nothing starts by itself: the weekly
/// schedule of profiles is gone (ADR 18), and Scheduled sessions exist only in history from before.
/// Time only moves forward when <see cref="Tick"/> is called (e.g. once a second from a timer),
/// so tests can drive it with a fake <see cref="TimeProvider"/>.
/// Only one session runs at a time. Not thread-safe: call from a single thread.
/// </summary>
public sealed class SessionEngine
{
    private readonly TimeProvider _timeProvider;
    private PomodoroSettings? _pomodoroSettings;

    // When set, the last focus cycle is followed by its break before the session ends.
    private bool _breakAfterLastCycle;

    public SessionEngine(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public SessionState State { get; private set; } = SessionState.Idle;

    public event EventHandler<SessionState>? StateChanged;

    /// <summary>
    /// Raised (with the state it ended from) when a session runs out on its own: a timed manual session or a
    /// Pomodoro with a cycle limit reached its end. Not raised by <see cref="EndSession"/>.
    /// </summary>
    public event EventHandler<SessionState>? SessionCompleted;

    private DateTimeOffset Now => _timeProvider.GetUtcNow();

    /// <param name="duration">Null runs until <see cref="EndSession"/> is called.</param>
    /// <param name="planning">Null starts focus at once. Otherwise the session begins with a planning stage of this length
    /// (<see cref="SessionPhase.Planning"/>), and <paramref name="duration"/> starts when it ends (<see cref="EndPlanning"/>).</param>
    /// <returns>False if a session is already running.</returns>
    public bool StartManual(Guid profileId, TimeSpan? duration = null, TimeSpan? planning = null)
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
        if (planning is TimeSpan planningLength)
        {
            SetState(PlanningState(SessionMode.Manual, profileId, now, planningLength, duration));
            return true;
        }

        SetState(new SessionState(
            SessionMode.Manual,
            SessionPhase.Focus,
            profileId,
            PhaseStartedAt: now,
            PhaseEndsAt: duration is null ? null : now + duration,
            CompletedFocusCycles: 0));
        return true;
    }

    /// <summary>
    /// A test session: one timed focus phase that enforces like a real one but is never counted, logged, synced or saved
    /// for a restart, and ends on command (see <see cref="SessionState.IsTest"/>).
    /// </summary>
    /// <returns>False if a session is already running.</returns>
    public bool StartTest(Guid profileId, TimeSpan duration)
    {
        if (State.IsActive)
        {
            return false;
        }

        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");
        }

        DateTimeOffset now = Now;
        SetState(new SessionState(
            SessionMode.Manual,
            SessionPhase.Focus,
            profileId,
            PhaseStartedAt: now,
            PhaseEndsAt: now + duration,
            CompletedFocusCycles: 0,
            IsTest: true));
        return true;
    }

    /// <param name="breakAfterLastCycle">With a cycle limit: also run the break after the last focus cycle
    /// before ending (e.g. "one focus, one break").</param>
    /// <param name="planning">As for <see cref="StartManual"/>: a planning stage of this length before the first focus cycle.</param>
    /// <returns>False if a session is already running.</returns>
    public bool StartPomodoro(Guid profileId, PomodoroSettings settings, bool breakAfterLastCycle = false, TimeSpan? planning = null)
    {
        if (State.IsActive)
        {
            return false;
        }

        ValidatePomodoro(settings);
        _pomodoroSettings = settings;
        _breakAfterLastCycle = breakAfterLastCycle;

        DateTimeOffset now = Now;
        if (planning is TimeSpan planningLength)
        {
            SetState(PlanningState(SessionMode.Pomodoro, profileId, now, planningLength, settings.FocusLength));
            return true;
        }

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
    /// Ends the planning stage now: the focus countdown starts fresh for its full length from this moment (planning time is not
    /// focus time, so the focus phase's start and end are the moment planning ended and its length after it). A session with
    /// no set end starts counting up from here.
    /// </summary>
    /// <returns>False when the session is not planning, or the user is away (planning ends when they are back).</returns>
    public bool EndPlanning()
    {
        if (!State.IsPlanning || State.IsAway)
        {
            return false;
        }

        DateTimeOffset now = Now;
        SetState(State with
        {
            Phase = SessionPhase.Focus,
            PhaseStartedAt = now,
            PhaseEndsAt = State.FocusLengthAfterPlanning is TimeSpan length ? now + length : null,
            FocusLengthAfterPlanning = null,
        });
        return true;
    }

    /// <summary>
    /// Gives the planning stage <paramref name="extra"/> more, counted from its end, or from now once that has passed (the
    /// "Ready?" card's +2 / +5 / +15). Nothing here is focus time.
    /// </summary>
    /// <returns>False when the session is not planning, or the user is away.</returns>
    public bool ExtendPlanning(TimeSpan extra)
    {
        if (!State.IsPlanning || State.IsAway || extra <= TimeSpan.Zero)
        {
            return false;
        }

        DateTimeOffset now = Now;
        DateTimeOffset from = State.PhaseEndsAt is DateTimeOffset end && end > now ? end : now;
        SetState(State with { PhaseEndsAt = from + extra });
        return true;
    }

    /// <summary>
    /// Shortens a timed manual session so its focus ends at <paramref name="newEnd"/> (the run-into-lunch card's "Adjust"): the
    /// running focus phase's end, or, while planning, the focus length that follows it. It never lengthens a session and never
    /// leaves less than <paramref name="shortest"/> of focus.
    /// </summary>
    /// <returns>False when the session isn't a timed manual one, or the new end isn't earlier than its end or leaves too little.</returns>
    public bool ShortenFocusEnd(DateTimeOffset newEnd, TimeSpan shortest)
    {
        if (!State.IsActive || State.Mode != SessionMode.Manual || !State.HasSetEnd)
        {
            return false;
        }

        DateTimeOffset now = Now;
        if (State.IsPlanning)
        {
            if (State.FocusStartsAt(now) is not DateTimeOffset focusStart || State.FocusLengthAfterPlanning is not TimeSpan length)
            {
                return false;
            }

            TimeSpan newLength = newEnd - focusStart;
            if (newLength < shortest || newLength >= length)
            {
                return false;
            }

            SetState(State with { FocusLengthAfterPlanning = newLength });
            return true;
        }

        if (State.PhaseEndsAt is not DateTimeOffset end || newEnd >= end || newEnd < State.CountdownTime(now) + shortest)
        {
            return false;
        }

        SetState(State with { PhaseEndsAt = newEnd });
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

        SetState(SessionState.Idle);
    }

    /// <summary>
    /// The user steps away (a break or an interruption): the countdown pauses until <see cref="Return"/>.
    /// It never shortens a wait or ends the session.
    /// </summary>
    /// <returns>False with no session running or when already away.</returns>
    public bool StepAway(AwayKind kind)
    {
        if (!State.IsActive || State.IsAway)
        {
            return false;
        }

        SetState(State with { Away = kind, AwaySince = Now });
        return true;
    }

    /// <summary>
    /// The user starts a side task of <paramref name="length"/>: the countdown pauses like a step away, but enforcement keeps
    /// running (<see cref="SessionState.PausesEnforcement"/>). The length only says when the user is asked if they are back:
    /// nothing ends and nothing is shortened when it is up.
    /// </summary>
    /// <returns>False with no session running or when already away.</returns>
    public bool StartSideTask(TimeSpan length)
    {
        if (length <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be positive.");
        }

        if (!State.IsActive || State.IsAway)
        {
            return false;
        }

        DateTimeOffset now = Now;
        SetState(State with { Away = AwayKind.SideTask, AwaySince = now, AwayEndsAt = now + length });
        return true;
    }

    /// <summary>
    /// Gives the running side task <paramref name="extra"/> more, counted from its end, or from now once that has passed.
    /// The caller applies the friction (reason, wait) first.
    /// </summary>
    /// <returns>False when no side task is running.</returns>
    public bool ExtendSideTask(TimeSpan extra)
    {
        if (State.Away != AwayKind.SideTask || extra <= TimeSpan.Zero)
        {
            return false;
        }

        DateTimeOffset now = Now;
        DateTimeOffset from = State.AwayEndsAt is DateTimeOffset end && end > now ? end : now;
        SetState(State with { AwayEndsAt = from + extra, SideTaskExtensions = State.SideTaskExtensions + 1 });
        return true;
    }

    /// <summary>
    /// Input was seen during a break or interruption: the user is at the desk, so enforcement runs as in focus from now
    /// (the countdown stays paused until <see cref="Return"/>). <paramref name="atDesk"/> false puts the hold back
    /// (lunch holds the away again).
    /// </summary>
    /// <returns>False when not away on a break or interruption, or nothing changed.</returns>
    public bool SetAwayAtDesk(bool atDesk)
    {
        if (!State.IsAway || State.Away == AwayKind.SideTask || State.AwayAtDesk == atDesk)
        {
            return false;
        }

        SetState(State with { AwayAtDesk = atDesk });
        return true;
    }

    /// <summary>
    /// The user is back: the phase's start and end move later by the time away, so no focus time is lost and its
    /// countdown carries on where it paused.
    /// </summary>
    /// <returns>How long the user was away; null when not away.</returns>
    public TimeSpan? Return()
    {
        if (State.AwaySince is not DateTimeOffset since)
        {
            return null;
        }

        TimeSpan away = Now - since;
        if (away < TimeSpan.Zero)
        {
            away = TimeSpan.Zero;
        }

        SetState(State with
        {
            Away = null,
            AwaySince = null,
            AwayEndsAt = null,
            AwayAtDesk = false,
            SideTaskExtensions = 0,
            PhaseStartedAt = State.PhaseStartedAt + away,
            PhaseEndsAt = State.PhaseEndsAt + away,
        });
        return away;
    }

    /// <summary>
    /// Everything needed to carry on with the current session after the app restarts. A test session is saved as no
    /// session: it never comes back after a restart.
    /// </summary>
    public SessionCheckpoint CreateCheckpoint()
    {
        if (State.IsTest)
        {
            return new SessionCheckpoint(SessionState.Idle, null, false);
        }

        return new SessionCheckpoint(State, _pomodoroSettings, _breakAfterLastCycle);
    }

    /// <summary>
    /// Puts back a session saved with <see cref="CreateCheckpoint"/>, e.g. after a crash or restart.
    /// Call <see cref="Tick"/> afterwards to catch up on phases that ran out while the app was closed.
    /// </summary>
    /// <returns>True if a session is running again; false if the checkpoint had none, was invalid, was a scheduled
    /// block (from before schema v5; never resumed) or a test session, or a session is already running.</returns>
    public bool Restore(SessionCheckpoint checkpoint)
    {
        if (State.IsActive)
        {
            return false;
        }

        SessionState saved = checkpoint.State;
        if (!saved.IsActive || saved.PhaseStartedAt is null || saved.Mode == SessionMode.Scheduled || saved.IsTest)
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

    /// <summary>Advances phases that have run out. Nothing runs out while the user is away or planning.</summary>
    public void Tick()
    {
        // Planning has no end of its own: it lasts until the user answers (Ready, a ticked task, Start on the card).
        if (State.IsAway || State.IsPlanning)
        {
            return;
        }

        DateTimeOffset now = Now;
        SessionState before = State;

        switch (State.Mode)
        {
            case SessionMode.Manual:
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

    private static SessionState PlanningState(SessionMode mode, Guid profileId, DateTimeOffset now, TimeSpan planning, TimeSpan? focusLength)
    {
        if (planning <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(planning), "Planning must be positive.");
        }

        return new SessionState(
            mode,
            SessionPhase.Planning,
            profileId,
            PhaseStartedAt: now,
            PhaseEndsAt: now + planning,
            CompletedFocusCycles: 0,
            FocusLengthAfterPlanning: focusLength);
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
