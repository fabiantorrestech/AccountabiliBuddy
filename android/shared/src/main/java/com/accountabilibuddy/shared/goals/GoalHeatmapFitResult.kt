package com.accountabilibuddy.shared.goals

/**
 * What [GoalHeatmapFit.fit] shows; the PC's `GoalHeatmapFitResult`.
 *
 * @property range The range shown: the one asked for, or a shorter one once it would crop more than about a month.
 * @property grid The grid shown, its oldest weeks already cropped.
 * @property croppedColumns Weeks cropped from the left of [range]'s grid.
 * @property steppedDown True when [range] is shorter than the one asked for.
 */
data class GoalHeatmapFitResult(val range: GoalHeatmapRange, val grid: GoalHeatmapGrid, val croppedColumns: Int, val steppedDown: Boolean)
