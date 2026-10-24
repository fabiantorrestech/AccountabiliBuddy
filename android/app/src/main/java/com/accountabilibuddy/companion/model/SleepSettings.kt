package com.accountabilibuddy.companion.model

import com.accountabilibuddy.companion.engine.SleepSchedule
import java.time.LocalTime

/**
 * Sleep mode, as in the Windows app's "sleep" document: bedtime and wake time, weeknights and weekend nights
 * (Friday and Saturday), or the same every night; the sleep goal the countdown starts from, and a wake time changed
 * on the PC for one night ([wakeOnce]).
 */
data class SleepSettings(
    val enabled: Boolean,
    val sameEveryNight: Boolean,
    val weeknights: SleepTimes,
    val weekends: SleepTimes,
    val goalHours: Double = 8.0,
    val wakeOnce: WakeOnce? = null,
    /** This phone's next alarm while "Next alarm as wake time" is on (ADR 23); set by the app, never saved. */
    val alarm: java.time.LocalDateTime? = null,
) {
    /** The goal as a length; 8 hours when the value makes no sense. */
    val goal: java.time.Duration
        get() = if (goalHours in 4.0..12.0) java.time.Duration.ofMinutes((goalHours * 60).toLong()) else SleepSchedule.DefaultGoal

    /** [plan] sets each night (workdays and one-off days included); these times are the fallback. */
    fun toSchedule(plan: DayPlan? = null): SleepSchedule =
        SleepSchedule(
            weeknights, if (sameEveryNight) weeknights else weekends,
            plan?.let { p -> { day: java.time.LocalDate -> p.nightBefore(day) } },
            wakeOnce,
            alarm,
            goal,
        )

    companion object {
        val Default = SleepSettings(
            enabled = true,
            sameEveryNight = false,
            weeknights = SleepTimes(LocalTime.of(22, 0), LocalTime.of(6, 0)),
            weekends = SleepTimes(LocalTime.of(23, 30), LocalTime.of(7, 30)),
        )
    }
}
