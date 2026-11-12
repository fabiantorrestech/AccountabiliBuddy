package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * What a goal aims for (brief §5.1): an amount per period, and optionally a long-term amount by a date.
 *
 * @property period The span [amount] is for.
 * @property amount The amount per period; null = no period target (a long-term target only, or Deep work's "Track time
 * only"). With [weeklyDays] set it is the amount for each of those days instead (a habit's is 1).
 * @property targetAmount The long-term amount (a total since the goal began, or the level to reach); null = none.
 * @property targetDate The date [targetAmount] is for; null = no date.
 * @property weeklyDays "3 of 7 days": how many days of each period must be hit; null = the period's amount counts instead.
 * @property missesAllowed For a daily goal: missed days a week before it reads Behind; null = none (Q361).
 * @property pendingChange A looser amount waiting for its delay (Deep work only today, §5.7); null = none.
 */
data class GoalTarget(
    val period: GoalPeriod,
    val amount: Double?,
    val unit: GoalUnit,
    val direction: GoalDirection = GoalDirection.AtLeast,
    val targetAmount: Double? = null,
    val targetDate: LocalDate? = null,
    val weeklyDays: Int? = null,
    val missesAllowed: Int? = null,
    val pendingChange: GoalPendingChange? = null,
) {
    /** Whether a day can be hit on its own: a daily target, or "N of the period's days". */
    val countsDays: Boolean get() = period == GoalPeriod.Day || weeklyDays != null
}
