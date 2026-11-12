package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * An amount goal's trend (brief §4.3): the charted points of the range, the dashed projection at the 3-month pace to the target, the
 * target level and date, the latest value with its 30-day change, and the banner.
 *
 * @property points The charted days of the range, oldest first.
 * @property projection The dashed line: the latest charted day, then where the pace reaches the target; empty when there is no
 * pace, no target, or the target is already met.
 * @property latest The latest charted value (over all days, whatever the range); null with no reading.
 * @property changeText "−1.2 lb in 30 days"; null with fewer than two readings in 30 days.
 * @property changeGood Whether the change moves toward the target; null without a target.
 */
data class GoalTrend(
    val range: GoalTrendRange,
    val points: List<GoalTrendPoint>,
    val projection: List<GoalTrendPoint>,
    val targetLevel: Double?,
    val targetDate: LocalDate?,
    val latest: Double?,
    val changeText: String?,
    val changeGood: Boolean?,
    val banner: GoalTrendBanner?,
)
