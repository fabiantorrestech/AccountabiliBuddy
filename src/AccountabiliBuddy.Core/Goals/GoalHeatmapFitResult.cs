namespace AccountabiliBuddy.Core.Goals;

/// <summary>What <see cref="GoalHeatmapFit.Fit"/> shows.</summary>
/// <param name="Range">The range shown: the one asked for, or a shorter one once it would crop more than about a month.</param>
/// <param name="Grid">The grid shown, its oldest weeks already cropped.</param>
/// <param name="CroppedColumns">Weeks cropped from the left of <paramref name="Range"/>'s grid.</param>
/// <param name="SteppedDown">True when <paramref name="Range"/> is shorter than the one asked for.</param>
public sealed record GoalHeatmapFitResult(GoalHeatmapRange Range, GoalHeatmapGrid Grid, int CroppedColumns, bool SteppedDown);
