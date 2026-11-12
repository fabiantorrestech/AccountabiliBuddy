package com.accountabilibuddy.shared.goals

/**
 * How a goal did in one period ([GoalStatus.period]): the "This period" tile, streaks and the weekly review. Only the goal's
 * live days count: before it was created and paused days are left out.
 *
 * @property required Days to hit (a goal that counts days), else the amount the period asks for.
 * @property progress Days hit so far, else the amount so far (a level to reach: its latest value).
 * @property met Whether it is met (an at-most period: not over its cap so far).
 * @property skipped Nothing was asked of it (no live day, or no period amount): neither met nor missed, skipped by streaks.
 * @property aliveDays The period's days the goal was live (created, not paused), past and future.
 */
data class GoalPeriodResult(
    val span: GoalPeriodSpan,
    val required: Double,
    val progress: Double,
    val met: Boolean,
    val skipped: Boolean,
    val aliveDays: Int,
)
