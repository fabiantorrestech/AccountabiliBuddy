package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * The heatmap's intensity (brief §5.5), the PC's `GoalHeat`: 4 steps of the goal's colour ramp. 0 = nothing (also paused, not
 * due), 1 = under the day's target, 2 = at it, 3 = well over (at least [WELL_OVER] times it). The day's target is the daily
 * amount (or the amount for each of "N days"), else the period's amount spread over its days; a habit's is its threshold (a
 * hand tick is "at"). An at-most day under its cap is "at", over it "under".
 */
object GoalHeat {
    const val WELL_OVER = 1.5

    fun step(goal: Goal, state: GoalDayState): Int {
        if (state.status == GoalDayStatus.Paused || !state.hasValue) {
            return 0
        }

        if (goal.kind == GoalKind.Habit) {
            val threshold = goal.sources
                .filter { it.automatic && it.countsTime && (it.thresholdMinutes ?: 0.0) > 0 }
                .mapNotNull { it.thresholdMinutes }
                .minOrNull()
            if (!state.hit && state.value < 1) {
                return if (state.minutes > 0) 1 else 0
            }

            return if (threshold != null && state.minutes >= threshold * WELL_OVER) 3 else 2
        }

        if (goal.target.direction == GoalDirection.AtMost) {
            val cap = goal.target.amount
            return if (cap != null && state.value > cap) 1 else 2
        }

        if (goal.target.direction == GoalDirection.Reach) {
            return if (state.hit) 2 else 1
        }

        val dayTarget = dayTarget(goal, state.day)
        if (state.value <= 0) {
            return 0
        }

        if (dayTarget == null || dayTarget <= 0) {
            return 2
        }

        val ratio = state.value / dayTarget
        return if (ratio < 1) 1 else if (ratio < WELL_OVER) 2 else 3
    }

    private fun dayTarget(goal: Goal, day: LocalDate): Double? {
        val amount = goal.target.amount ?: return null
        if (goal.target.countsDays) {
            return amount
        }

        val length = when (goal.target.period) {
            GoalPeriod.Week -> 7
            GoalPeriod.TwoWeeks -> 14
            GoalPeriod.Month -> day.lengthOfMonth()
            GoalPeriod.Day -> 1
        }
        return amount / length
    }
}
