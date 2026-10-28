using AccountabiliBuddy.App.Controls;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What a heatmap view shows (Consistency, Intensity): the grid, its labels, the cards and a note under it.</summary>
/// <param name="RowLabels">Null for none.</param>
/// <param name="ColumnLabels">Null for none.</param>
/// <param name="Note">The Muted line under the grid.</param>
/// <param name="HelperLine">Intensity's plan-helper line; null for none.</param>
public sealed record StatsHeatPanel(
    IReadOnlyList<HeatCell> Cells,
    int Rows,
    int Columns,
    IReadOnlyList<string>? RowLabels,
    IReadOnlyList<string?>? ColumnLabels,
    IReadOnlyList<StatsCard> Cards,
    string Note,
    string? HelperLine = null)
{
    public bool HasCards => Cards.Count > 0;

    public bool HasHelperLine => HelperLine is not null;
}
