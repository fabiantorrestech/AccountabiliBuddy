using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// Goals Options › Charts on this PC (document "goals.settings", device-local, never synced, a device setting in backups; brief §4.3):
/// the week start every goal's week, streak and heatmap use, the heatmap's day labels and the extra tiles on goal pages. Display
/// and period maths only, nothing a guard reads, so changes apply at once in both modes.
/// </summary>
/// <param name="WeekStart">Sunday by default (brief §4.3).</param>
/// <param name="HeatmapLabels">Some by default (Q373).</param>
/// <param name="ExtraTiles">Streak and Total tiles on goal pages; off by default (a goal's own switch in Edit goal overrides it).</param>
public sealed record GoalPageSettings(GoalWeekStart WeekStart, GoalHeatmapLabels HeatmapLabels, GoalExtraTiles? ExtraTiles)
{
    public static GoalPageSettings Default { get; } = new(GoalWeekStart.Sunday, GoalHeatmapLabels.Some, GoalExtraTiles.None);

    /// <summary>The same settings with known values only.</summary>
    public GoalPageSettings Clamped()
    {
        return this with
        {
            WeekStart = Enum.IsDefined(WeekStart) ? WeekStart : GoalWeekStart.Sunday,
            HeatmapLabels = Enum.IsDefined(HeatmapLabels) ? HeatmapLabels : GoalHeatmapLabels.Some,
            ExtraTiles = ExtraTiles ?? GoalExtraTiles.None,
        };
    }
}
