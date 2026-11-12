package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * A habit's reviewed week on the phone's review card (board Goals-Phone-Review): seven boxes with the weekday letter under each, ticked
 * when hit, and the line "**2 of 3** · one more would have made it". Only for habits ([of] is null otherwise).
 *
 * @property bold "2 of 3" (or "5 of 7 days" for a daily habit).
 * @property rest "one more would have made it", "made it", "2 more would have made it".
 */
data class GoalReviewWeek(val cells: List<GoalReviewWeekCell>, val bold: String, val rest: String) {
    companion object {
        private val LETTERS = mapOf(
            java.time.DayOfWeek.SUNDAY to "S", java.time.DayOfWeek.MONDAY to "M", java.time.DayOfWeek.TUESDAY to "T", java.time.DayOfWeek.WEDNESDAY to "W",
            java.time.DayOfWeek.THURSDAY to "T", java.time.DayOfWeek.FRIDAY to "F", java.time.DayOfWeek.SATURDAY to "S",
        )

        /** The week from [weekStart] (the review's week) for [goal]; null for a goal that isn't a habit. */
        fun of(goal: Goal, data: GoalSourceData, weekStart: LocalDate, today: LocalDate): GoalReviewWeek? {
            if (goal.kind != GoalKind.Habit || goal.isDeepWork) return null
            val days = GoalCounting.days(goal, data, weekStart, weekStart.plusDays(6), today)
            val cells = days.map { state ->
                val counted = state.status != GoalDayStatus.Paused && state.day >= goal.createdOn(data.zone)
                GoalReviewWeekCell(state.day, LETTERS.getValue(state.day.dayOfWeek), state.hit, counted)
            }
            val hit = cells.count { it.hit }
            val daily = goal.target.period == GoalPeriod.Day
            val required = if (daily) cells.count { it.counted } else (goal.target.weeklyDays ?: maxOf(1.0, Math.rint(goal.target.amount ?: 1.0)).toInt())
            val bold = if (daily) "$hit of $required days" else "$hit of $required"
            val short = required - hit
            val rest = when {
                short <= 0 -> "made it"
                short == 1 -> "one more would have made it"
                else -> "$short more would have made it"
            }
            return GoalReviewWeek(cells, bold, rest)
        }
    }
}
