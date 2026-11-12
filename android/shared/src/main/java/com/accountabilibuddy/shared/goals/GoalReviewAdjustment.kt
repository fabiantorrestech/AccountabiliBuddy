package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import kotlin.math.max

/**
 * The numbers "Adjust the target" pre-fills in Edit goal › Target (brief §5.8), the PC's `GoalReviewAdjustment` rule for rule:
 * a long-term target finishing past its date at this pace moves its date to that finish; "N of the period's days" above one asks one
 * day fewer; an at-least amount above what the pace gives drops to the pace's amount (time to 5 minutes, others whole, rounded half to
 * even like .NET's `Math.Round`, never below one step). Null when none applies. Pre-filled only; a lower Deep work target still waits.
 */
data class GoalReviewAdjustment(val targetDate: LocalDate?, val amount: Double?, val weeklyDays: Int?) {
    companion object {
        fun forGoal(goal: Goal, suggestion: GoalReviewSuggestion, today: LocalDate): GoalReviewAdjustment? {
            val target = goal.target
            val date = target.targetDate
            val finish = suggestion.projectedFinish
            if (date != null && finish != null && finish > date && finish > today) {
                return GoalReviewAdjustment(finish, null, null)
            }

            val days = target.weeklyDays
            if (days != null && days > 1) {
                return GoalReviewAdjustment(null, null, days - 1)
            }

            val amount = target.amount
            val pace = suggestion.pacePerDay
            if (amount != null && target.direction == GoalDirection.AtLeast && target.weeklyDays == null && pace != null && pace > 0) {
                val step = if (target.unit == GoalUnit.Minutes) 5.0 else 1.0
                val perPeriod = max(step, Math.rint(pace * daysIn(target.period) / step) * step)
                if (perPeriod < amount) {
                    return GoalReviewAdjustment(null, perPeriod, null)
                }
            }

            return null
        }

        private fun daysIn(period: GoalPeriod): Int = when (period) {
            GoalPeriod.Day -> 1
            GoalPeriod.Week -> 7
            GoalPeriod.TwoWeeks -> 14
            GoalPeriod.Month -> 30
        }
    }
}
