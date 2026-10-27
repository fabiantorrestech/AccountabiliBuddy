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
    /** The smart bedtime on for nights before workdays / Off-Days (ADR 34, shared both ways, ADR 35). */
    val smartWorkdays: Boolean = false,
    val smartOffDays: Boolean = false,
    /** The smart bedtime's minimum and maximum sleep, in hours. */
    val minHours: Double = 6.5,
    val maxHours: Double = 9.0,
    /** The bedtime reminders (ADR 38, shared both ways). */
    val reminders: BedtimeReminderSettings = BedtimeReminderSettings.Default,
    /** The smart bedtime's levels (minutes in bed before waking) for the kinds of night it's on for (ADR 34); set by the app, never saved. */
    val smartLevels: Map<com.accountabilibuddy.companion.engine.NightKind, Int> = emptyMap(),
    /** The estimated wake time per kind of morning (ADR 40), for mornings woken by the alarm with none known; set by the app, never saved. */
    val wakeEstimates: Map<com.accountabilibuddy.companion.engine.NightKind, LocalTime> = emptyMap(),
) {
    /** The smart bedtime is on for [kind]. */
    fun smartOn(kind: com.accountabilibuddy.companion.engine.NightKind): Boolean =
        if (kind == com.accountabilibuddy.companion.engine.NightKind.Workday) smartWorkdays else smartOffDays

    /** [smartOn] changed for [kind]. */
    fun withSmart(kind: com.accountabilibuddy.companion.engine.NightKind, on: Boolean): SleepSettings =
        if (kind == com.accountabilibuddy.companion.engine.NightKind.Workday) copy(smartWorkdays = on) else copy(smartOffDays = on)

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
            if (plan == null || smartLevels.isEmpty()) null
            else { day: java.time.LocalDate -> smartLevels[com.accountabilibuddy.companion.engine.SmartBedtime.kindOf(day, plan::isWorkdayByWeek)] },
            if (plan == null || wakeEstimates.isEmpty()) null
            else { morning: java.time.LocalDate -> wakeEstimates[com.accountabilibuddy.companion.engine.SmartBedtime.kindOf(morning, plan::isWorkdayByWeek)] },
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
