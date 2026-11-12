package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import kotlin.math.ceil
import kotlin.math.max

/**
 * The Target tile's "What it takes" pop-up and the New goal box (brief §5.6, change note §4a), the PC's `GoalWhatItTakes`:
 * remaining = target − current; days left = the target date − today, both included; each day = remaining ÷ days left, each
 * week = each day × 7; pace = the average over the trailing weeks (live days before today); projected finish = today +
 * remaining ÷ pace in whole days; verdict met / covered / needs more / date passed / no date. Minutes are shown rounded up to
 * friendly values ([friendlyMinutes], [minutesText]).
 *
 * @property remaining What is left, in the goal's unit (0 once met).
 * @property daysLeft Days left to the date, today included; null without a date or once it has passed.
 * @property ownPerDay The goal's own period target as a daily rate; null without one.
 * @property morePerDay Amber: how much more than [ownPerDay] each day needs; null otherwise.
 */
data class GoalWhatItTakes(
    val verdict: GoalWhatItTakesVerdict,
    val remaining: Double,
    val daysLeft: Int?,
    val eachDay: Double?,
    val eachWeek: Double?,
    val pacePerDay: Double?,
    val projectedFinish: LocalDate?,
    val ownPerDay: Double?,
    val morePerDay: Double?,
    val morePerWeek: Double?,
) {
    companion object {
        const val DEFAULT_PACE_WEEKS = 4

        /** For a goal with a long-term amount (not an at-most one); null otherwise. */
        fun forGoal(goal: Goal, data: GoalSourceData, today: LocalDate, paceWeeks: Int = DEFAULT_PACE_WEEKS): GoalWhatItTakes? {
            val progress = GoalStatus.target(goal, data, today)
            if (progress == null || goal.target.direction == GoalDirection.AtMost) {
                return null
            }

            val down = goal.target.direction == GoalDirection.Reach && GoalCounting.reachesDown(goal, data)
            val remaining = if (goal.target.direction == GoalDirection.Reach) {
                if (down) progress.current - progress.target else progress.target - progress.current
            } else {
                progress.target - progress.current
            }
            return compute(remaining, today, goal.target.targetDate, pace(goal, data, today, paceWeeks, down), ownRate(goal, today))
        }

        /** The maths on its own (the New goal box, before the goal exists). */
        fun compute(remaining: Double, today: LocalDate, targetDate: LocalDate?, pacePerDay: Double?, ownPerDay: Double?): GoalWhatItTakes {
            val left = max(0.0, remaining)
            val projected = if (left > 0 && pacePerDay != null && pacePerDay > 0) today.plusDays(ceil((left / pacePerDay) - 1e-9).toInt().toLong()) else null
            if (left <= 0) {
                return GoalWhatItTakes(GoalWhatItTakesVerdict.Met, 0.0, null, null, null, pacePerDay, null, ownPerDay, null, null)
            }

            if (targetDate == null) {
                return GoalWhatItTakes(GoalWhatItTakesVerdict.NoDate, left, null, null, null, pacePerDay, projected, ownPerDay, null, null)
            }

            if (today > targetDate) {
                return GoalWhatItTakes(GoalWhatItTakesVerdict.DatePassed, left, null, null, null, pacePerDay, projected, ownPerDay, null, null)
            }

            val daysLeft = (targetDate.toEpochDay() - today.toEpochDay() + 1).toInt()
            val eachDay = left / daysLeft
            val eachWeek = eachDay * 7
            if (ownPerDay != null && ownPerDay >= eachDay) {
                return GoalWhatItTakes(GoalWhatItTakesVerdict.Covered, left, daysLeft, eachDay, eachWeek, pacePerDay, projected, ownPerDay, null, null)
            }

            val more = eachDay - (ownPerDay ?: 0.0)
            return GoalWhatItTakes(GoalWhatItTakesVerdict.NeedsMore, left, daysLeft, eachDay, eachWeek, pacePerDay, projected, ownPerDay, more, more * 7)
        }

        /** Minutes rounded up to a friendly value: whole minutes under an hour, then 5-minute steps (84.2 → 85). */
        fun friendlyMinutes(minutes: Double): Int {
            if (minutes <= 0) {
                return 0
            }

            val whole = ceil(minutes - 1e-9).toInt()
            return if (whole < 60) whole else (ceil((minutes - 1e-9) / 5) * 5).toInt()
        }

        /** "12 min", "1h", "1h 25m". */
        fun minutesText(minutes: Int): String {
            if (minutes < 60) {
                return "$minutes min"
            }

            val hours = minutes / 60
            val rest = minutes % 60
            return if (rest == 0) "${hours}h" else "${hours}h ${rest}m"
        }

        /** The goal's own period target as a daily rate (at-least goals only); null without one. */
        fun ownRate(goal: Goal, today: LocalDate): Double? {
            val amount = goal.target.amount
            if (goal.target.direction != GoalDirection.AtLeast || amount == null) {
                return null
            }

            val length = when (goal.target.period) {
                GoalPeriod.Week -> 7
                GoalPeriod.TwoWeeks -> 14
                GoalPeriod.Month -> today.lengthOfMonth()
                GoalPeriod.Day -> 1
            }
            val days = goal.target.weeklyDays
            val perPeriod = if (days != null && goal.target.period != GoalPeriod.Day) amount * days else amount
            return perPeriod / length
        }

        /** The trailing average a day over [weeks] weeks before today (live days only); null with none. */
        fun pace(goal: Goal, data: GoalSourceData, today: LocalDate, weeks: Int, reachesDown: Boolean): Double? {
            val from = today.minusDays(7L * weeks)
            val days = GoalCounting.history(goal, data, today, from)
            if (goal.target.direction == GoalDirection.Reach) {
                val values = days.values
                    .filter { it.hasValue && goal.isAliveOn(it.day, data.zone) }
                    .sortedBy { it.day }
                if (values.size < 2 || values.last().day.toEpochDay() - values.first().day.toEpochDay() < 1) {
                    return null
                }

                val moved = if (reachesDown) values.first().value - values.last().value else values.last().value - values.first().value
                return moved / (values.last().day.toEpochDay() - values.first().day.toEpochDay())
            }

            val live = days.values.filter { it.day < today && goal.isAliveOn(it.day, data.zone) }
            return if (live.isEmpty()) null else live.sumOf { it.value } / live.size
        }
    }
}
