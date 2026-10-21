namespace AccountabiliBuddy.App.Controls;

/// <summary>Everything the <see cref="DayBarControl"/> draws, already placed on its 0–1 width.</summary>
/// <param name="Rows">The rows top to bottom (Calendar, Plan, Focus, Blockers, as chosen in Settings).</param>
/// <param name="Now">Where now is; below 0 or above 1 when outside the bar (drawn pinned to that edge).</param>
/// <param name="Night">Sleep time (bedtime to wake), with sleep mode on.</param>
/// <param name="Missed">Sleep time already lost by staying up past bedtime.</param>
/// <param name="RangeStart">The local time at the bar's left edge (for turning a click into a time).</param>
/// <param name="RangeEnd">The local time at the bar's right edge.</param>
public sealed record DayBarLayout(
    IReadOnlyList<DayBarRow> Rows,
    IReadOnlyList<DayBarTick> Ticks,
    double Now,
    IReadOnlyList<DayBarSpan>? Night = null,
    IReadOnlyList<DayBarSpan>? Missed = null,
    DateTime? RangeStart = null,
    DateTime? RangeEnd = null)
{
    public static DayBarLayout Empty { get; } = new(Array.Empty<DayBarRow>(), Array.Empty<DayBarTick>(), 0);
}
