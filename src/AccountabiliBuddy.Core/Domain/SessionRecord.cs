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

    /// <summary>
    /// How long the planning stage lasted, without the time the user was stepped away in it (that is away time, kept apart).
    /// Zero for a session without one; a stage still going isn't counted.
    /// </summary>
    public TimeSpan PlanningTime
    {
        get
        {
            TimeSpan planning = TimeSpan.Zero;
            foreach (AwayStretch stretch in (Away ?? []).Where(stretch => stretch.Kind == AwayKind.Planning && stretch.To != DateTimeOffset.MaxValue))
            {
                // The away time inside the stage, each moment once (breaks, side tasks and marked gaps may overlap each other).
                TimeSpan away = TimeSpan.Zero;
                DateTimeOffset reached = stretch.From;
                foreach (AwayStretch other in (Away ?? [])
                             .Where(other => other.Kind != AwayKind.Planning && other.To != DateTimeOffset.MaxValue && other.To > stretch.From && other.From < stretch.To)
                             .OrderBy(other => other.From))
                {
                    DateTimeOffset from = other.From > reached ? other.From : reached;
                    DateTimeOffset to = other.To < stretch.To ? other.To : stretch.To;
                    if (to > from)
                    {
                        away += to - from;
                        reached = to;
                    }
                }

                TimeSpan length = stretch.To - stretch.From - away;
                planning += length > TimeSpan.Zero ? length : TimeSpan.Zero;
            }

            return planning;
        }
    }

    private static TimeSpan Total(IEnumerable<AwayStretch> stretches) => stretches
        .Where(stretch => stretch.To != DateTimeOffset.MaxValue)
        .Aggregate(TimeSpan.Zero, (sum, stretch) => sum + (stretch.To - stretch.From));
}
