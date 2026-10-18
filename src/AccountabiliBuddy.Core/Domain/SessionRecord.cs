namespace AccountabiliBuddy.Core.Domain;

/// <summary>One session in the History list, built from its start and end events.</summary>
/// <param name="EndedAt">Null while the session is still running (or the app stopped before it ended).</param>
/// <param name="Outcome">How it ended; null while running or unknown.</param>
/// <param name="Reason">The early-end reason, when there was one.</param>
public sealed record SessionRecord(
    DateTimeOffset StartedAt,
    SessionMode? Mode,
    Guid? ProfileId,
    DateTimeOffset? EndedAt,
    SessionEventKind? Outcome,
    string? Reason)
{
    public TimeSpan? Length => EndedAt - StartedAt;
}
