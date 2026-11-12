package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import kotlin.math.ceil

/**
 * The weekly review's suggestion for a goal (brief §5.8), the PC's `GoalReviewSuggestion`, rule-based on the device, with the
 * facts it used as pills: **Pause** when nothing counted for [QUIET_DAYS] days (never for an at-most goal); else **Adjust the
 * target** when not met in 2 of its last 3 periods that asked for something, when the target date has passed unmet, or when
 * the projected finish is more than [LATE_SHARE] of the days left past the date (judged once the goal has [MIN_PACE_DAYS] live
 * days before today); else **Keep going** (Q366, Q382). No network, no AI.
 *
 * @property behindPeriods Of [periodsChecked] (up to 3) completed periods, how many weren't met.
 * @property behindDays Days missed or short in the last completed period.
 * @property changePercent30 The last 30 days against the 30 before, in percent; null when the 30 before had nothing.
 * @property pacePerDay The trailing pace ([GoalWhatItTakes.pace]); null with none.
 * @property projectedFinish "At this pace: <date>" for a goal with a long-term target; null otherwise.
 */
data class GoalReviewSuggestion(
    val choice: GoalReviewChoice,
    val behindPeriods: Int,
    val periodsChecked: Int,
    val behindDays: Int,
    val changePercent30: Double?,
    val pacePerDay: Double?,
    val projectedFinish: LocalDate?,
    val quiet: Boolean,
) {
    companion object {
        /** Three weeks without activity suggests Pause. */
        const val QUIET_DAYS = 21

        /** The projected finish may run this share of the days left past the date before Adjust is suggested. */
        const val LATE_SHARE = 0.10

        /** The live days before today a goal needs before its projected finish is judged. */
        const val MIN_PACE_DAYS = 7

        fun forGoal(goal: Goal, data: GoalSourceData, today: LocalDate, weekStart: GoalWeekStart): GoalReviewSuggestion {
            val created = goal.createdOn(data.zone)
            val days = GoalCounting.history(goal, data, today)
            val reachesDown = GoalCounting.reachesDown(goal, data)

            // The last 3 completed periods that asked for something, newest first.
            val checkedPeriods = mutableListOf<GoalPeriodResult>()
            var span = GoalPeriods.periodOf(goal.target.period, today, weekStart, created)
            span = GoalPeriods.previous(goal.target.period, span, weekStart, created)
            while (span.end >= created && checkedPeriods.size < 3) {
                val result = GoalStatus.evaluate(goal, data, span, days, today, reachesDown)
                if (!result.skipped) {
                    checkedPeriods += result
                }

                span = GoalPeriods.previous(goal.target.period, span, weekStart, created)
            }

            val behindPeriods = checkedPeriods.count { !it.met }
            val behindDays = if (checkedPeriods.isEmpty()) {
                0
            } else {
                checkedPeriods[0].span.days().count { day ->
                    val state = days[day]
                    state != null && (state.status == GoalDayStatus.Missed || state.status == GoalDayStatus.NotCounted)
                }
            }

            val recentLive = (1..QUIET_DAYS).map { today.minusDays(it.toLong()) }.filter { goal.isAliveOn(it, data.zone) }
            val quiet = goal.target.direction != GoalDirection.AtMost &&
                created.toEpochDay() <= today.toEpochDay() - QUIET_DAYS &&
                recentLive.isNotEmpty() &&
                recentLive.all { day -> days[day]?.let { !active(it) } ?: true }

            val recent = sumLive(goal, data, days, today.minusDays(30), today.minusDays(1))
            val before = sumLive(goal, data, days, today.minusDays(60), today.minusDays(31))
            val change = if (before > 0) (recent - before) / before * 100 else null

            val takes = GoalWhatItTakes.forGoal(goal, data, today)
            val pace = takes?.pacePerDay ?: GoalWhatItTakes.pace(goal, data, today, GoalWhatItTakes.DEFAULT_PACE_WEEKS, reachesDown)
            val paceHistory = days.keys.count { it < today && goal.isAliveOn(it, data.zone) }
            var late = takes?.verdict == GoalWhatItTakesVerdict.DatePassed
            val left = takes?.daysLeft
            val date = goal.target.targetDate
            if (takes != null && (takes.verdict == GoalWhatItTakesVerdict.Covered || takes.verdict == GoalWhatItTakesVerdict.NeedsMore) &&
                left != null && date != null && paceHistory >= MIN_PACE_DAYS
            ) {
                val allowed = ceil(left * LATE_SHARE).toInt()
                val finish = takes.projectedFinish
                late = finish == null || finish.toEpochDay() > date.toEpochDay() + allowed
            }

            val choice = when {
                quiet -> GoalReviewChoice.Pause
                behindPeriods >= 2 || late -> GoalReviewChoice.Adjust
                else -> GoalReviewChoice.KeepGoing
            }
            return GoalReviewSuggestion(choice, behindPeriods, checkedPeriods.size, behindDays, change, pace, takes?.projectedFinish, quiet)
        }

        private fun active(state: GoalDayState): Boolean = state.hasValue && (state.value > 0 || state.minutes > 0)

        private fun sumLive(goal: Goal, data: GoalSourceData, days: Map<LocalDate, GoalDayState>, from: LocalDate, to: LocalDate): Double {
            var sum = 0.0
            var day = from
            while (day <= to) {
                val state = days[day]
                if (goal.isAliveOn(day, data.zone) && state != null) {
                    sum += state.value
                }

                day = day.plusDays(1)
            }

            return sum
        }
    }
}
