namespace AccountabiliBuddy.Core.Domain;

/// <summary>A session starting or ending, for the local history.</summary>
/// <param name="PlannedEnd">For <see cref="SessionEventKind.Started"/>: when the first phase was due to end, if timed.</param>
/// <param name="Reason">For <see cref="SessionEventKind.EndedEarly"/>: what the user typed (stored encrypted).</param>
public sealed record SessionEvent(
    DateTimeOffset At,
    SessionEventKind Kind,
    SessionMode? Mode,
    Guid? ProfileId,
    DateTimeOffset? PlannedEnd = null,
    string? Reason = null);
