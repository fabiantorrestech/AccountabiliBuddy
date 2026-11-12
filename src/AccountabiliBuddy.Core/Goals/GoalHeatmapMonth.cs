namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// One month's part of a goal heatmap (<see cref="GoalHeatmapGrid"/>): the columns its shown days fill, for a label centred over
/// them, and where the month starts, for its staircase divider.
/// </summary>
/// <param name="Year">The month's year.</param>
/// <param name="Month">1 to 12.</param>
/// <param name="Label">"May" (three letters, invariant culture).</param>
/// <param name="FirstColumn">The column of the month's first shown day.</param>
/// <param name="LastColumn">The column of the month's last shown day.</param>
/// <param name="FirstRow">The row of the month's first shown day.</param>
/// <param name="HasDivider">
/// True when the month's first shown day is its 1st and an earlier month is shown before it: one continuous staircase divider runs
/// between them, turning at (<paramref name="FirstColumn"/>, <paramref name="FirstRow"/>).
/// </param>
public sealed record GoalHeatmapMonth(int Year, int Month, string Label, int FirstColumn, int LastColumn, int FirstRow, bool HasDivider)
{
    /// <summary>The middle of the month's columns, in columns from the grid's left edge (its label is centred here).</summary>
    public double CentreColumn => (FirstColumn + LastColumn + 1) / 2.0;
}
