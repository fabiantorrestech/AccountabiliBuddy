package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * Builds [GoalFacts] from the goals maths only ([GoalCounting], [GoalStatus], [GoalStrip14], [GoalStreak], [GoalAge],
 * [GoalWhatItTakes]); the PC's `GoalFactsBuilder` line for line, so the phone shows what the PC shows from the same data.
 */
object GoalFactsBuilder {
    /** Days of history the row visuals read (sparkline, 7-day bars). */
    const val RECENT_DAYS = 30

    fun build(goal: Goal, data: GoalSourceData, today: LocalDate, weekStart: GoalWeekStart): GoalFacts {
        val weekly = goal.secondTarget
        val periodGoal = if (goal.isDeepWork && weekly?.amount != null) goal.copy(target = weekly) else goal
        val span = GoalStatus.thisPeriodSpan(periodGoal, today, weekStart, data.zone)
        val period = GoalStatus.period(periodGoal, data, span, today)
        val recent = GoalCounting.days(goal, data, today.minusDays((RECENT_DAYS - 1).toLong()), today, today)
        val periodCells = if (span.length <= 14) {
            GoalCounting.days(goal, data, span.start, span.end, today).map { GoalStrip14.cellOf(goal, it, today) }
        } else {
            emptyList()
        }
        val pausedSince = goal.pauses.orEmpty().filter { it.resumedOn == null && it.from <= today }.maxOfOrNull { it.from }
        return GoalFacts(
            goal = goal,
            today = today,
            isPaused = goal.isPausedOn(today),
            pausedSince = pausedSince,
            todayState = recent.last(),
            periodSpan = span,
            periodGoal = periodGoal,
            period = period,
            status = GoalStatus.thisPeriod(goal, data, today, weekStart),
            strip = GoalStrip14.forGoal(goal, data, today),
            periodCells = periodCells,
            recent = recent,
            target = GoalStatus.target(goal, data, today),
            whatItTakes = GoalWhatItTakes.forGoal(goal, data, today),
            streak = GoalStreak.count(goal, data, today, weekStart),
            age = GoalAge.forGoal(goal, today, data.zone),
            threshold = GoalTexts.threshold(goal),
        )
    }
}
