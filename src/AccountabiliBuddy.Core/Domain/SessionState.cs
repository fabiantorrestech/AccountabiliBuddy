namespace AccountabiliBuddy.Core.Domain;

/// <summary>Snapshot of the session engine. Rules are enforced only while <see cref="IsEnforcing"/>.</summary>
/// <param name="Away">Set while the user has stepped away; the countdown is paused (no phase ends) until they're back.</param>
/// <param name="AwaySince">When the user stepped away.</param>
/// <param name="IsTest">A test session (Today, More options): it enforces and cues like a real one, but it is never counted,
/// logged, synced, shown to linked devices or saved for a restart, and it ends on command.</param>
public sealed record SessionState(
    SessionMode? Mode,
    SessionPhase Phase,
    Guid? ProfileId,
    DateTimeOffset? PhaseStartedAt,
    DateTimeOffset? PhaseEndsAt,
    int CompletedFocusCycles,
    AwayKind? Away = null,
    DateTimeOffset? AwaySince = null,
    bool IsTest = false)
{
    public static SessionState Idle { get; } = new(null, SessionPhase.Idle, null, null, null, 0);

    public bool IsActive => Mode is not null;

    /// <summary>A session that counts as the user's work: running and not a test (<see cref="IsTest"/>).</summary>
    public bool IsRealSession => IsActive && !IsTest;

    public bool IsEnforcing => Phase == SessionPhase.Focus;

    public bool IsAway => Away is not null;

    /// <summary>The time countdowns read: frozen at <see cref="AwaySince"/> while away, else <paramref name="now"/>.</summary>
    public DateTimeOffset CountdownTime(DateTimeOffset now) => AwaySince is DateTimeOffset since && since < now ? since : now;
}
