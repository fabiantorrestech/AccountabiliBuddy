namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// How a goal heatmap fits its width (change note §4a): it never squashes. When the grid has more weeks than fit, the oldest weeks
/// are cropped from the left (the newest stay); once that would crop more than about a month (<see cref="MaxCroppedColumns"/>
/// weeks), the range steps down (Year → 6 months → 3 months → 1 month) so the title stays true. A month never steps down: it crops.
/// </summary>
public static class GoalHeatmapFit
{
    /// <summary>"About a month" of weeks: cropping more than this steps the range down.</summary>
    public const int MaxCroppedColumns = 4;

    /// <param name="columnsThatFit">Whole weeks that fit the width (at least one is always shown).</param>
    public static GoalHeatmapFitResult Fit(GoalHeatmapRange wanted, DateOnly end, GoalWeekStart weekStart, int columnsThatFit)
    {
        int fit = Math.Max(1, columnsThatFit);
        GoalHeatmapRange range = wanted;
        while (true)
        {
            GoalHeatmapGrid grid = GoalHeatmapGrid.For(range, end, weekStart);
            int crop = Math.Max(0, grid.Columns - fit);
            if (crop > MaxCroppedColumns && range > GoalHeatmapRange.OneMonth)
            {
                range--;
                continue;
            }

            return new GoalHeatmapFitResult(range, grid.CropOldest(crop), crop, range != wanted);
        }
    }
}
