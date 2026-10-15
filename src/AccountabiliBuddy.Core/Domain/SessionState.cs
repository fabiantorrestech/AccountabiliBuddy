namespace AccountabiliBuddy.Core.Domain;

/// <summary>Snapshot of the session engine. Rules are enforced only while <see cref="IsEnforcing"/>.</summary>
public sealed record SessionState(
    SessionMode? Mode,
    SessionPhase Phase,
    Guid? ProfileId,
    DateTimeOffset? PhaseStartedAt,
    DateTimeOffset? PhaseEndsAt,
    int CompletedFocusCycles)
{
    public static SessionState Idle { get; } = new(null, SessionPhase.Idle, null, null, null, 0);

    public bool IsActive => Mode is not null;

    public bool IsEnforcing => Phase == SessionPhase.Focus;
}
