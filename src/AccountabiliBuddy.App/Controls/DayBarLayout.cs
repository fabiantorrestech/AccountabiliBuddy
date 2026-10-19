namespace AccountabiliBuddy.App.Controls;

/// <summary>Everything the <see cref="DayBarControl"/> draws, already placed on its 0–1 width.</summary>
/// <param name="Planned">Scheduled focus time.</param>
/// <param name="Actual">Sessions that actually ran (the running one up to now).</param>
/// <param name="Events">Calendar events, drawn as a thin band.</param>
/// <param name="Now">Where now is; below 0 or above 1 when outside the bar (drawn pinned to that edge).</param>
public sealed record DayBarLayout(
    IReadOnlyList<DayBarSpan> Planned,
    IReadOnlyList<DayBarSpan> Actual,
    IReadOnlyList<DayBarSpan> Events,
    IReadOnlyList<DayBarTick> Ticks,
    double Now)
{
    public static DayBarLayout Empty { get; } = new(
        Array.Empty<DayBarSpan>(), Array.Empty<DayBarSpan>(), Array.Empty<DayBarSpan>(), Array.Empty<DayBarTick>(), 0);
}
