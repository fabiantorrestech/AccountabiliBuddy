package com.accountabilibuddy.shared.goals

/**
 * The Target tile (change note §4a): "34h of 50h · Target by Sun 1/31" with an On track / Behind pill ([GoalStatus.target]).
 *
 * @property current The total since the goal began (live days only), or a level goal's latest value.
 * @property target The long-term amount or level.
 * @property expectedToday The pace line at the end of today; null without a date.
 * @property start A level goal's first value (where its pace line starts); null for a total.
 */
data class GoalTargetProgress(
    val current: Double,
    val target: Double,
    val expectedToday: Double?,
    val start: Double?,
    val met: Boolean,
    val status: GoalPeriodStatus,
)
