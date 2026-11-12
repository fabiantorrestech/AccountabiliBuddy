package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * The 14-day strip on the goal list (change note §4), the PC's `GoalStrip14`: the last 13 days and today, oldest first, from the
 * same day states as the streak, so a 12-day streak on a daily goal is 12 solid cells before today (paused cells skipped).
 */
object GoalStrip14 {
    const val LENGTH = 14

    fun forGoal(goal: Goal, data: GoalSourceData, today: LocalDate): List<GoalStripCell> =
        GoalCounting.days(goal, data, today.minusDays((LENGTH - 1).toLong()), today, today).map { cell(goal, it, today) }

    private fun cell(goal: Goal, state: GoalDayState, today: LocalDate): GoalStripCell {
        val isToday = state.day == today
        val counted = state.status == GoalDayStatus.Counted || state.status == GoalDayStatus.CountedTickedItself
        val kind = when {
            counted && state.hit -> GoalStripCellKind.Hit
            state.status == GoalDayStatus.Paused -> GoalStripCellKind.Paused

            // A day that added to a weekly amount without being a target of its own.
            counted && !isToday -> GoalStripCellKind.Partial
            state.status == GoalDayStatus.NotCounted && !isToday -> GoalStripCellKind.Partial
            state.status == GoalDayStatus.Missed -> GoalStripCellKind.Missed
            isToday && state.status != GoalDayStatus.NotDue -> GoalStripCellKind.Today
            else -> GoalStripCellKind.NotDue
        }
        val progress = if (kind == GoalStripCellKind.Today) todayProgress(goal, state) else 0.0
        return GoalStripCell(state.day, kind, progress, isToday)
    }

    // As on the PC, a habit's threshold is read from its stored rules here (not GoalSources.countedFor).
    private fun todayProgress(goal: Goal, state: GoalDayState): Double {
        val target = if (goal.kind == GoalKind.Habit) {
            goal.sources.filter { it.automatic && it.countsTime && (it.thresholdMinutes ?: 0.0) > 0 }.mapNotNull { it.thresholdMinutes }.minOrNull()
        } else if (goal.target.direction == GoalDirection.AtLeast) {
            goal.target.amount
        } else {
            null
        }
        val done = if (goal.kind == GoalKind.Habit) state.minutes else state.value
        return if (target != null && target > 0) (done / target).coerceIn(0.0, 1.0) else 0.0
    }
}
