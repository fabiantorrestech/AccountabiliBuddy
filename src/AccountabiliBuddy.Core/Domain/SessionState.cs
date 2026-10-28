namespace AccountabiliBuddy.Core.Domain;

/// <summary>Snapshot of the session engine. Rules are enforced only while <see cref="IsEnforcing"/>.</summary>
/// <param name="Away">Set while the user has stepped away; the countdown is paused (no phase ends) until they're back.</param>
/// <param name="AwaySince">When the user stepped away.</param>
/// <param name="AwayEndsAt">For a side task (<see cref="AwayKind.SideTask"/>): when its chosen length is up; null for any other away.</param>
/// <param name="AwayAtDesk">A break or interruption in which input was seen (the "Back to the task?" card was shown): the user is
/// at the desk, so enforcement runs as in focus although the countdown stays paused until Back on track.</param>
/// <param name="SideTaskExtensions">How many times 5 minutes were added to the running side task (kept with it, so a restart
/// can't reset the wait the next +5 min needs).</param>
/// <param name="IsTest">A test session (Today, More options): it enforces and cues like a real one, but it is never counted,
/// logged, synced, shown to linked devices or saved for a restart, and it ends on command.</param>
/// <param name="FocusLengthAfterPlanning">While <see cref="SessionPhase.Planning"/>: how long the focus phase runs once planning ends
/// (<see cref="PhaseStartedAt"/> and <see cref="PhaseEndsAt"/> are then the planning stage's own); null for a session with no set end.
/// Kept in the checkpoint, so a restart resumes planning and still knows the focus length.</param>
public sealed record SessionState(
    SessionMode? Mode,
    SessionPhase Phase,
    Guid? ProfileId,
    DateTimeOffset? PhaseStartedAt,
    DateTimeOffset? PhaseEndsAt,
    int CompletedFocusCycles,
    AwayKind? Away = null,
    DateTimeOffset? AwaySince = null,
    bool IsTest = false,
    DateTimeOffset? AwayEndsAt = null,
    bool AwayAtDesk = false,
    int SideTaskExtensions = 0,
    TimeSpan? FocusLengthAfterPlanning = null)
{
    public static SessionState Idle { get; } = new(null, SessionPhase.Idle, null, null, null, 0);

    public bool IsActive => Mode is not null;

    /// <summary>A session that counts as the user's work: running and not a test (<see cref="IsTest"/>).</summary>
    public bool IsRealSession => IsActive && !IsTest;

    /// <summary>Rules are enforced in focus and in the planning stage before it.</summary>
    public bool IsEnforcing => Phase is SessionPhase.Focus or SessionPhase.Planning;

    /// <summary>The planning stage before the first focus countdown: enforcement runs, but it is not focus time.</summary>
    public bool IsPlanning => Phase == SessionPhase.Planning;

    /// <summary>
    /// When the (current or coming) focus phase starts: its own start, or, while planning, the planning stage's end, or
    /// <paramref name="now"/> once that has passed unanswered (what a device that doesn't know planning is told: a focus
    /// countdown that begins when planning ends, which can't be earlier than now).
    /// </summary>
    public DateTimeOffset? FocusStartsAt(DateTimeOffset now) =>
        IsPlanning ? PhaseEndsAt is DateTimeOffset planningEnd ? Max(planningEnd, CountdownTime(now)) : null : PhaseStartedAt;

    /// <summary>When that focus phase ends: null for a session with no set end. While planning it is projected from <see cref="FocusStartsAt"/>.</summary>
    public DateTimeOffset? FocusEndsAt(DateTimeOffset now) => IsPlanning
        ? FocusLengthAfterPlanning is TimeSpan length && FocusStartsAt(now) is DateTimeOffset start ? start + length : null
        : PhaseEndsAt;

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    /// <summary>
    /// True when the session counts down to a set end: this phase's, or, while planning, the focus phase that follows it. A
    /// session without one (count-up, "No set end") ends on command (<see cref="Engine.BypassPolicy.EndsOnCommand"/>).
    /// </summary>
    public bool HasSetEnd => IsPlanning ? FocusLengthAfterPlanning is not null : PhaseEndsAt is not null;

    public bool IsAway => Away is not null;

    /// <summary>
    /// "+10 min" is possible: a timed manual session in its focus phase, not away (break, interruption or side task). Never for
    /// planning, a break phase, Pomodoro, a session with no set end or a test session.
    /// </summary>
    public bool CanExtendFocus => IsRealSession && Mode == SessionMode.Manual && Phase == SessionPhase.Focus && !IsAway && PhaseEndsAt is not null;

    /// <summary>
    /// True while enforcement holds as it was (a break or an interruption with no input seen yet): nothing new is judged and
    /// nothing blocked is lifted. From the first input (<see cref="AwayAtDesk"/>) and through a side task, enforcement runs
    /// as in focus; only the focus clock stays paused.
    /// </summary>
    public bool PausesEnforcement => IsAway && Away != AwayKind.SideTask && !AwayAtDesk;

    /// <summary>The time countdowns read: frozen at <see cref="AwaySince"/> while away, else <paramref name="now"/>.</summary>
    public DateTimeOffset CountdownTime(DateTimeOffset now) => AwaySince is DateTimeOffset since && since < now ? since : now;
}
