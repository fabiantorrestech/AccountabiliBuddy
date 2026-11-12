package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * "Nudge when the week falls behind" (brief §5.9, §5.10), the PC's `GoalNudge`: one nudge the morning after the goal drops below pace.
 * It dropped when this period's status is Behind today but wasn't as of yesterday; a goal paused (or not yet made) yesterday or today
 * isn't nudged. Suggestions only.
 */
object GoalNudge {
    fun droppedBehind(goal: Goal, data: GoalSourceData, today: LocalDate, weekStart: GoalWeekStart): Boolean {
        val yesterday = today.minusDays(1)
        if (!goal.isAliveOn(today, data.zone) || !goal.isAliveOn(yesterday, data.zone)) return false
        return GoalStatus.thisPeriod(goal, data, today, weekStart) == GoalPeriodStatus.Behind &&
            GoalStatus.thisPeriod(goal, data, yesterday, weekStart) != GoalPeriodStatus.Behind
    }
}
