package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * What the goal page's heatmap card draws: the grid fitted to the width, each shown day's cell, the title and dates, and the legend
 * line. The PC's `GoalHeatmapViewModel.Rebuild` rule for rule; the phone's range is 3 months to start with (brief §4.3).
 *
 * @property title "Last 3 months" at the latest page, else the dates.
 * @property dates "Aug – Nov 2026" at the latest page; null once paged back.
 * @property states Each shown day's state (the day panel reads the same).
 * @property canGoNext False at the latest page.
 */
data class GoalHeatmap(
    val grid: GoalHeatmapGrid,
    val range: GoalHeatmapRange,
    val title: String,
    val dates: String?,
    val legend: String,
    val cells: List<GoalHeatCell>,
    val states: Map<LocalDate, GoalDayState>,
    val canGoNext: Boolean,
) {
    companion object {
        /** The columns that fit before the first layout (the PC's 27, six months' worth). */
        const val UNMEASURED_COLUMNS = 27

        fun build(
            goal: Goal,
            data: GoalSourceData,
            today: LocalDate,
            weekStart: GoalWeekStart,
            wanted: GoalHeatmapRange,
            end: LocalDate,
            columnsThatFit: Int,
        ): GoalHeatmap {
            val fit = GoalHeatmapFit.fit(wanted, end, weekStart, columnsThatFit)
            val grid = fit.grid
            val latest = end >= today
            val dates = datesText(grid.rangeStart, grid.end)
            val last = if (grid.end < today) grid.end else today
            val states = GoalCounting.days(goal, data, grid.rangeStart, last, today).associateBy { it.day }
            val cells = mutableListOf<GoalHeatCell>()
            for (column in 0 until grid.columns) {
                for (row in 0 until GoalHeatmapGrid.ROWS) {
                    val day = grid.dayAt(row, column)
                    if (!grid.shows(day)) {
                        if (day > grid.end && latest && day > today) {
                            cells += GoalHeatCell(row, column, 0, "${GoalTexts.date(day, today)} · still to come", null, GoalHeatCellMark.Future, 0.0)
                        }

                        continue
                    }

                    val state = states[day] ?: continue
                    cells += cell(goal, state, row, column, today)
                }
            }

            return GoalHeatmap(grid, fit.range, if (latest) lastText(fit.range) else dates, if (latest) dates else null, legend(goal), cells, states, !latest)
        }

        private fun cell(goal: Goal, state: GoalDayState, row: Int, column: Int, today: LocalDate): GoalHeatCell {
            val kind = GoalStrip14.cellOf(goal, state, today)
            val level = when (GoalHeat.step(goal, state)) {
                0 -> 0
                1 -> 1
                2 -> 3
                else -> 4
            }
            val mark = when (kind.kind) {
                GoalStripCellKind.Missed -> GoalHeatCellMark.Hollow
                GoalStripCellKind.Partial -> GoalHeatCellMark.Faint
                GoalStripCellKind.Paused -> GoalHeatCellMark.Paused
                GoalStripCellKind.Today -> GoalHeatCellMark.Today
                else -> GoalHeatCellMark.None
            }
            val progress = if (kind.kind == GoalStripCellKind.Today) kind.progress else 0.0
            return GoalHeatCell(row, column, level, tip(goal, state, today), state.day, mark, progress)
        }

        /** "Fri 10/30 · 0 min · missed". */
        private fun tip(goal: Goal, state: GoalDayState, today: LocalDate): String {
            val value = if (goal.kind == GoalKind.Habit || goal.target.unit == GoalUnit.Minutes) {
                GoalTexts.minutes(if (state.minutes > 0 || goal.kind == GoalKind.Habit) state.minutes else state.value)
            } else if (state.hasValue) {
                GoalTexts.value(state.value, goal.target.unit)
            } else {
                "no value"
            }
            return "${GoalTexts.date(state.day, today)} · $value · ${statusWord(state)}"
        }

        private fun statusWord(state: GoalDayState): String = when (state.status) {
            GoalDayStatus.Counted, GoalDayStatus.CountedTickedItself -> if (state.hit) "counted" else "logged"
            GoalDayStatus.NotCounted -> "under the target"
            GoalDayStatus.Missed -> "missed"
            GoalDayStatus.InProgress -> "in progress"
            GoalDayStatus.Paused -> "paused"
            GoalDayStatus.NotDue -> "not due"
        }

        private fun legend(goal: Goal): String {
            if (goal.kind == GoalKind.Habit) {
                val threshold = GoalTexts.threshold(goal)
                return if (threshold != null) "Shade = minutes that day (${threshold.toInt()}+ counts)" else "Shade = done that day"
            }

            if (goal.target.unit == GoalUnit.Minutes) {
                val amount = goal.target.amount
                return if (goal.target.period == GoalPeriod.Day && amount != null) "Shade = minutes that day (${GoalTexts.minutes(amount)}+ counts)" else "Shade = minutes that day"
            }

            return "Shade = the day's value"
        }

        private fun lastText(range: GoalHeatmapRange): String = when (range) {
            GoalHeatmapRange.OneMonth -> "Last month"
            GoalHeatmapRange.ThreeMonths -> "Last 3 months"
            GoalHeatmapRange.SixMonths -> "Last 6 months"
            GoalHeatmapRange.Year -> "Last year"
        }

        private val MONTHS = listOf("Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec")

        /** "May – Nov 2026", "Nov 2025 – May 2026". */
        fun datesText(from: LocalDate, to: LocalDate): String {
            val end = "${MONTHS[to.monthValue - 1]} ${to.year}"
            return if (from.year == to.year) "${MONTHS[from.monthValue - 1]} – $end" else "${MONTHS[from.monthValue - 1]} ${from.year} – $end"
        }
    }
}
