package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * The streak (brief §5.5), the PC's `GoalStreak`: consecutive met periods up to yesterday's (a daily goal's days, else its
 * weeks, two weeks or months), the current one added once it is met. A period with nothing asked of it is skipped, never a
 * break; the first unmet period ends it. The 14-day strip ([GoalStrip14]) shows the same days.
 */
object GoalStreak {
    fun count(goal: Goal, data: GoalSourceData, today: LocalDate, weekStart: GoalWeekStart): Int {
        val created = goal.createdOn(data.zone)
        if (today < created) {
            return 0
        }

        val days = GoalCounting.history(goal, data, today)
        val reachesDown = GoalCounting.reachesDown(goal, data)
        var span = GoalPeriods.periodOf(goal.target.period, today, weekStart, created)
        // An at-most period isn't met until it is over (being under the cap so far proves nothing yet).
        val currentCanCount = GoalCounting.countsDays(goal) || goal.target.direction != GoalDirection.AtMost
        var streak = if (currentCanCount && GoalStatus.evaluate(goal, data, span, days, today, reachesDown).met) 1 else 0

        span = GoalPeriods.previous(goal.target.period, span, weekStart, created)
        while (span.end >= created) {
            val result = GoalStatus.evaluate(goal, data, span, days, today, reachesDown)
            if (!result.skipped) {
                if (!result.met) {
                    break
                }

                streak++
            }

            span = GoalPeriods.previous(goal.target.period, span, weekStart, created)
        }

        return streak
    }
}
