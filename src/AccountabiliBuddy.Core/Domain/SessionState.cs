namespace AccountabiliBuddy.Core.Domain;

/// <summary>Snapshot of the session engine. Rules are enforced only while <see cref="IsEnforcing"/>.</summary>
/// <param name="Away">Set while the user has stepped away; the countdown is paused (no phase ends) until they're back.</param>
/// <param name="AwaySince">When the user stepped away.</param>
public sealed record SessionState(
    SessionMode? Mode,
    SessionPhase Phase,
    Guid? ProfileId,
    DateTimeOffset? PhaseStartedAt,
    DateTimeOffset? PhaseEndsAt,
    int CompletedFocusCycles,
    AwayKind? Away = null,
    DateTimeOffset? AwaySince = null)
{
    public static SessionState Idle { get; } = new(null, SessionPhase.Idle, null, null, null, 0);

    public bool IsActive => Mode is not null;

    public bool IsEnforcing => Phase == SessionPhase.Focus;

    public bool IsAway => Away is not null;
}
