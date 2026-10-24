package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.model.SleepAmount
import com.accountabilibuddy.companion.model.SleepTimes
import com.accountabilibuddy.companion.model.WakeOnce
import java.time.DayOfWeek
import java.time.Duration
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime

/**
 * Bedtime and wake time worked out for actual dates (port of the Windows Core's SleepSchedule). A night is named
 * by the date of its evening. With the day plan ([nightBefore]), each night's times come from the plan for the day
 * the night leads into; otherwise, and for a day whose plan has no night, Friday and Saturday nights use the weekend
 * times. A wake time changed for one night ([wakeOnce]) moves only that morning. The phone's next alarm ([alarm], ADR 23)
 * sets the wake time of the morning it falls on, between the night's bedtime and noon, over a one-night change unless
 * that change was confirmed over the alarm. A night woken by the alarm ([SleepTimes.wakeByAlarm], ADR 33) with no alarm
 * that morning ends at bedtime plus [goal].
 */
class SleepSchedule(
    private val weeknights: SleepTimes,
    private val weekends: SleepTimes,
    private val nightBefore: ((LocalDate) -> SleepTimes?)? = null,
    private val wakeOnce: WakeOnce? = null,
    private val alarm: java.time.LocalDateTime? = null,
    private val goal: Duration = DefaultGoal,
) {
    /** From bedtime to wake time of the night starting on [evening]. */
    fun night(evening: LocalDate): LocalWindow {
        val times = nightBefore?.invoke(evening.plusDays(1)) ?: if (isWeekendNight(evening)) weekends else weeknights
        var bed = evening.atTime(times.bedtime)
        if (times.bedtime.isBefore(NOON)) {
            bed = bed.plusDays(1)
        }

        val wake = wakeOf(evening, bed, times).first
        return LocalWindow(bed, if (wake.isAfter(bed)) wake else bed)
    }

    /** True when the phone's alarm sets the wake time ending the night of [evening]. */
    fun wakesByAlarm(evening: LocalDate): Boolean {
        val times = nightBefore?.invoke(evening.plusDays(1)) ?: if (isWeekendNight(evening)) weekends else weeknights
        var bed = evening.atTime(times.bedtime)
        if (times.bedtime.isBefore(NOON)) {
            bed = bed.plusDays(1)
        }

        return wakeOf(evening, bed, times).second
    }

    // The morning's wake: the alarm between bedtime and noon (unless a one-night change was confirmed over it), else
    // the one-night change, else the plan's.
    private fun wakeOf(evening: LocalDate, bed: java.time.LocalDateTime, times: SleepTimes): Pair<java.time.LocalDateTime, Boolean> {
        val morning = evening.plusDays(1)
        val once = wakeOnce?.takeIf { it.morning == morning }
        val alarm = alarm
        if (alarm != null && alarm.isAfter(bed) && alarm.isBefore(morning.atTime(NOON)) && once?.overridesAlarm != true) {
            return alarm to true
        }

        if (once == null && times.wakeByAlarm) {
            return bed.plus(goal) to false
        }

        return morning.atTime(once?.wake ?: times.wake) to false
    }

    /** The night ahead of [now], or the one it is in: last night until its wake time, then tonight. */
    fun nightAround(now: LocalDateTime): LocalWindow {
        val today = now.toLocalDate()
        val lastNight = night(today.minusDays(1))
        return if (now.isBefore(lastNight.end)) lastNight else night(today)
    }

    /** The waking day [now] belongs to: from the last wake time to the next bedtime. */
    fun dayAround(now: LocalDateTime): LocalWindow {
        val night = nightAround(now)
        val evening = if (night.start.toLocalTime().isBefore(NOON)) night.start.toLocalDate().minusDays(1) else night.start.toLocalDate()
        val nightBefore = night(evening.minusDays(1))
        return LocalWindow(nightBefore.end, night.start)
    }

    companion object {
        val GoodSleep: Duration = Duration.ofHours(7)
        val OkaySleep: Duration = Duration.ofHours(5)

        /** The sleep goal unless the PC says another: the countdown starts when bed now would give this. */
        val DefaultGoal: Duration = Duration.ofHours(8)

        // A bedtime before noon belongs to the early hours after the evening.
        private val NOON: LocalTime = LocalTime.NOON

        fun isWeekendNight(evening: LocalDate): Boolean =
            evening.dayOfWeek == DayOfWeek.FRIDAY || evening.dayOfWeek == DayOfWeek.SATURDAY

        fun rate(sleep: Duration): SleepAmount = when {
            sleep >= GoodSleep -> SleepAmount.Good
            sleep >= OkaySleep -> SleepAmount.Okay
            else -> SleepAmount.Short
        }

        /** Graded against the goal: good down to an hour under it, okay down to three hours under, short below. */
        fun rate(sleep: Duration, goal: Duration): SleepAmount = when {
            sleep >= goal.minusHours(1) -> SleepAmount.Good
            sleep >= goal.minusHours(3) -> SleepAmount.Okay
            else -> SleepAmount.Short
        }

        /** From when going to bed would give [goal] of sleep until wake time: the "sleep if you go now" countdown. */
        fun showsCountdown(now: LocalDateTime, night: LocalWindow, goal: Duration): Boolean =
            !now.isBefore(night.end.minus(goal)) && now.isBefore(night.end)

        /** How long the sleep would be when going to bed at [now], until wake time. */
        fun sleepIfBedNow(now: LocalDateTime, night: LocalWindow): Duration =
            if (now.isBefore(night.end)) Duration.between(now, night.end) else Duration.ZERO

        /** How long the sleep would be when going to bed at [now] (or at bedtime, if earlier). */
        fun sleepIfBedAt(now: LocalDateTime, night: LocalWindow): Duration {
            val asleep = if (now.isAfter(night.start)) now else night.start
            return if (asleep.isBefore(night.end)) Duration.between(asleep, night.end) else Duration.ZERO
        }
    }
}
