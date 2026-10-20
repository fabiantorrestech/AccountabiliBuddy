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
    fun toSchedule(): SleepSchedule = SleepSchedule(weeknights, if (sameEveryNight) weeknights else weekends)

    companion object {
        val Default = SleepSettings(
            enabled = true,
            sameEveryNight = false,
            weeknights = SleepTimes(LocalTime.of(22, 0), LocalTime.of(6, 0)),
            weekends = SleepTimes(LocalTime.of(23, 30), LocalTime.of(7, 30)),
        )
    }
}
