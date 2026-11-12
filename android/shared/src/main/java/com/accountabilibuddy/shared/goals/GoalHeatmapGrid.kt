package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * The goal heatmap's grid (change note §4a), the PC's `GoalHeatmapGrid`: a column per week, a row per weekday, weeks starting on
 * the user's week start, the newest week on the right. The range ends on [end] (today, or the last day of a page paged back to)
 * and starts its months (or a year) before that, the day after; the first column starts on that day's week start, so the first
 * column's days before [rangeStart] and the last column's days after [end] are in the grid but not shown. Month labels are
 * centred over each month's shown columns; every month after the first shown one gets a staircase divider.
 *
 * @property first The first day of the first column (a week start).
 * @property rangeStart The first shown day.
 * @property end The last shown day.
 * @property columns Weeks across, the one holding [end] last.
 */
data class GoalHeatmapGrid(
    val range: GoalHeatmapRange,
    val first: LocalDate,
    val rangeStart: LocalDate,
    val end: LocalDate,
    val columns: Int,
    val weekStart: GoalWeekStart,
    val months: List<GoalHeatmapMonth>,
) {
    /** The row of [day] (0 = the week start). */
    fun rowOf(day: LocalDate): Int = Math.floorMod(day.toEpochDay() - first.toEpochDay(), 7L).toInt()

    /** The column of [day] (negative before the grid). */
    fun columnOf(day: LocalDate): Int = Math.floorDiv(day.toEpochDay() - first.toEpochDay(), 7L).toInt()

    /** The day in a cell. */
    fun dayAt(row: Int, column: Int): LocalDate = first.plusDays(column * 7L + row)

    /** Whether [day] is shown (inside the range). */
    fun shows(day: LocalDate): Boolean = day >= rangeStart && day <= end

    /** The same grid with its [columns] oldest weeks cropped from the left (the newest week always stays). */
    fun cropOldest(columns: Int): GoalHeatmapGrid {
        val crop = columns.coerceIn(0, this.columns - 1)
        if (crop == 0) {
            return this
        }

        val first = this.first.plusDays(7L * crop)
        val rangeStart = if (this.rangeStart > first) this.rangeStart else first
        return make(range, first, rangeStart, end, weekStart)
    }

    private fun monthsShown(): List<GoalHeatmapMonth> {
        val months = mutableListOf<GoalHeatmapMonth>()
        var month = rangeStart.withDayOfMonth(1)
        while (month <= end) {
            val firstShown = if (month < rangeStart) rangeStart else month
            val monthEnd = month.plusMonths(1).minusDays(1)
            val lastShown = if (monthEnd > end) end else monthEnd
            val divider = firstShown.dayOfMonth == 1 && months.isNotEmpty()
            months += GoalHeatmapMonth(
                month.year, month.monthValue, MONTH_LABELS[month.monthValue - 1], columnOf(firstShown), columnOf(lastShown), rowOf(firstShown), divider,
            )
            month = month.plusMonths(1)
        }

        return months
    }

    companion object {
        const val ROWS = 7

        // The PC's month.ToString("MMM", InvariantCulture).
        private val MONTH_LABELS = listOf("Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec")

        /** The grid of [range] ending on [end]. */
        fun forRange(range: GoalHeatmapRange, end: LocalDate, weekStart: GoalWeekStart): GoalHeatmapGrid {
            val rangeStart = rangeStartOf(range, end)
            return make(range, GoalPeriods.weekStartOf(rangeStart, weekStart), rangeStart, end, weekStart)
        }

        /** The first day of [range] ending on [end]: its months (or a year) back, the day after. */
        fun rangeStartOf(range: GoalHeatmapRange, end: LocalDate): LocalDate =
            if (range == GoalHeatmapRange.Year) end.minusYears(1).plusDays(1) else end.minusMonths(monthsOf(range).toLong()).plusDays(1)

        /** How many months a range spans (12 for a year). */
        fun monthsOf(range: GoalHeatmapRange): Int = when (range) {
            GoalHeatmapRange.OneMonth -> 1
            GoalHeatmapRange.ThreeMonths -> 3
            GoalHeatmapRange.SixMonths -> 6
            GoalHeatmapRange.Year -> 12
        }

        /**
         * The last day of the page before ([direction] below 0: the day before this page's first day) or after (above 0: the
         * range's span later, never past [today]) a page of [range] ending on [end] (‹ and ›).
         */
        fun step(range: GoalHeatmapRange, end: LocalDate, direction: Int, today: LocalDate): LocalDate {
            if (direction < 0) {
                return rangeStartOf(range, end).minusDays(1)
            }

            val next = if (range == GoalHeatmapRange.Year) end.plusYears(1) else end.plusMonths(monthsOf(range).toLong())
            return if (next > today) today else next
        }

        private fun make(range: GoalHeatmapRange, first: LocalDate, rangeStart: LocalDate, end: LocalDate, weekStart: GoalWeekStart): GoalHeatmapGrid {
            val columns = Math.floorDiv(end.toEpochDay() - first.toEpochDay(), 7L).toInt() + 1
            val grid = GoalHeatmapGrid(range, first, rangeStart, end, maxOf(1, columns), weekStart, emptyList())
            return grid.copy(months = grid.monthsShown())
        }
    }
}
