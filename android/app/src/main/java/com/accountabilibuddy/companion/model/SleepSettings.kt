package com.accountabilibuddy.companion.model

import com.accountabilibuddy.companion.engine.SleepSchedule
import java.time.LocalTime

/**
 * Sleep mode, as in the Windows app's "sleep" document: bedtime and wake time, weeknights and weekend nights
 * (Friday and Saturday), or the same every night.
 */
data class SleepSettings(
    val enabled: Boolean,
    val sameEveryNight: Boolean,
    val weeknights: SleepTimes,
    val weekends: SleepTimes,
) {
    /** [plan] sets each night (workdays and one-off days included); these times are the fallback. */
    fun toSchedule(plan: DayPlan? = null): SleepSchedule =
        SleepSchedule(weeknights, if (sameEveryNight) weeknights else weekends, plan?.let { p -> { day: java.time.LocalDate -> p.nightBefore(day) } })

    companion object {
        val Default = SleepSettings(
            enabled = true,
            sameEveryNight = false,
            weeknights = SleepTimes(LocalTime.of(22, 0), LocalTime.of(6, 0)),
            weekends = SleepTimes(LocalTime.of(23, 30), LocalTime.of(7, 30)),
        )
    }
}
