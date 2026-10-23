namespace AccountabiliBuddy.Core.Domain;

/// <summary>One session in the History list, built from its start and end events.</summary>
/// <param name="EndedAt">Null while the session is still running (or the app stopped before it ended).</param>
/// <param name="Outcome">How it ended; null while running or unknown.</param>
/// <param name="Reason">The early-end reason, when there was one.</param>
/// <param name="AppClosedFor">How long the app was closed while the session ran (it resumed afterwards).</param>
/// <param name="Away">The stretches the user stepped away (and why), oldest first; null when never. One still going ends
/// at <see cref="DateTimeOffset.MaxValue"/>: cut it to the session.</param>
public sealed record SessionRecord(
    DateTimeOffset StartedAt,
    SessionMode? Mode,
    Guid? ProfileId,
    DateTimeOffset? EndedAt,
    SessionEventKind? Outcome,
    string? Reason,
    TimeSpan AppClosedFor = default,
    IReadOnlyList<AwayStretch>? Away = null)
{
    public TimeSpan? Length => EndedAt - StartedAt;

    /// <summary>How long the user was away during the session that ended (a stretch still going isn't counted).</summary>
    public TimeSpan AwayTime => Total(Away ?? []);

    /// <summary>How long the user was away for <paramref name="kind"/> (breaks or interruptions).</summary>
    public TimeSpan AwayTimeOf(AwayKind kind) => Total((Away ?? []).Where(stretch => stretch.Kind == kind));

    private static TimeSpan Total(IEnumerable<AwayStretch> stretches) => stretches
        .Where(stretch => stretch.To != DateTimeOffset.MaxValue)
        .Aggregate(TimeSpan.Zero, (sum, stretch) => sum + (stretch.To - stretch.From));
}
