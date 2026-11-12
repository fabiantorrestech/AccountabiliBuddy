package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * How a goal heatmap fits its width (change note §4a), the PC's `GoalHeatmapFit`: it never squashes. When the grid has more
 * weeks than fit, the oldest weeks are cropped from the left (the newest stay); once that would crop more than about a month
 * ([MAX_CROPPED_COLUMNS] weeks), the range steps down (Year → 6 months → 3 months → 1 month) so the title stays true. A month
 * never steps down: it crops.
 */
object GoalHeatmapFit {
    /** "About a month" of weeks: cropping more than this steps the range down. */
    const val MAX_CROPPED_COLUMNS = 4

    /** [columnsThatFit]: whole weeks that fit the width (at least one is always shown). */
    fun fit(wanted: GoalHeatmapRange, end: LocalDate, weekStart: GoalWeekStart, columnsThatFit: Int): GoalHeatmapFitResult {
        val fit = maxOf(1, columnsThatFit)
        var range = wanted
        while (true) {
            val grid = GoalHeatmapGrid.forRange(range, end, weekStart)
            val crop = maxOf(0, grid.columns - fit)
            if (crop > MAX_CROPPED_COLUMNS && range > GoalHeatmapRange.OneMonth) {
                range = GoalHeatmapRange.entries[range.ordinal - 1]
                continue
            }

            return GoalHeatmapFitResult(range, grid.cropOldest(crop), crop, range != wanted)
        }
    }
}
