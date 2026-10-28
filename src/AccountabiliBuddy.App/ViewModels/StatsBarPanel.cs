using AccountabiliBuddy.App.Controls;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// What an Overview-style view shows (Overview, Drift, Phone, Sleep): a big number with its Muted line, a bar chart when the
/// data has a series (Sleep's doesn't), and up to three cards.
/// </summary>
/// <param name="GoalFraction">The goal line's height, 0 to 1 of the scale; NaN for no line.</param>
public sealed record StatsBarPanel(
    string BigText,
    string BigNote,
    IReadOnlyList<StatsBar>? Bars,
    double GoalFraction,
    string? GoalText,
    IReadOnlyList<StatsCard> Cards)
{
    public bool HasBars => Bars is not null;

    public bool HasCards => Cards.Count > 0;
}
