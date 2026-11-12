package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * One goal's numbers for today, computed once per refresh by [GoalFactsBuilder] from the goals maths; the PC's `GoalFacts`
 * (App, `Services/Goals`). Read by a goal row, the goal's page and the week summary. Nothing here is stored.
 *
 * @property goal The goal as shown (Deep work as its view: on the phone its daily target is the PC's daily goal, read only).
 * @property isPaused Paused today: it leaves the week's counts and shows only in All.
 * @property pausedSince The day its current pause began, while paused.
 * @property todayState Today's day state.
 * @property periodGoal The goal the This period tile reads (Deep work with a weekly target: that target).
 * @property period How [periodGoal] does this period.
 * @property status On track / Behind / Not due yet / Paused for this period (brief §5.5).
 * @property strip The 14-day strip, oldest first ([GoalStrip14]).
 * @property periodCells This period's days as strip cells (a week or two only; empty for a month).
 * @property recent The last [GoalFactsBuilder.RECENT_DAYS] days' states, oldest first (sparkline, 7-day bars).
 * @property target The Target tile (a long-term amount), or null.
 * @property whatItTakes The Target tile's pop-up (brief §5.6), or null.
 * @property threshold A habit's "ticks itself at" minutes, or null.
 */
data class GoalFacts(
    val goal: Goal,
    val today: LocalDate,
    val isPaused: Boolean,
    val pausedSince: LocalDate?,
    val todayState: GoalDayState,
    val periodSpan: GoalPeriodSpan,
    val periodGoal: Goal,
    val period: GoalPeriodResult,
    val status: GoalPeriodStatus,
    val strip: List<GoalStripCell>,
    val periodCells: List<GoalStripCell>,
    val recent: List<GoalDayState>,
    val target: GoalTargetProgress?,
    val whatItTakes: GoalWhatItTakes?,
    val streak: Int,
    val age: GoalAge,
    val threshold: Double?,
) {
    /** True when the goal asks for something each day or period (not Deep work's "Track time only"). */
    val hasPeriodTarget: Boolean
        get() = goal.kind == GoalKind.Habit || goal.target.amount != null || goal.target.weeklyDays != null
}
