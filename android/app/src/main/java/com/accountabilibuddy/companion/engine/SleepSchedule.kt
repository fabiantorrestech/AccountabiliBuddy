package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.model.SleepAmount
import com.accountabilibuddy.companion.model.SleepTimes
import java.time.DayOfWeek
import java.time.Duration
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime

/**
 * Bedtime and wake time worked out for actual dates (port of the Windows Core's SleepSchedule). A night is named
 * by the date of its evening; Friday and Saturday nights use the weekend times.
 */
class SleepSchedule(private val weeknights: SleepTimes, private val weekends: SleepTimes) {
    /** From bedtime to wake time of the night starting on [evening]. */
    fun night(evening: LocalDate): LocalWindow {
        val times = if (isWeekendNight(evening)) weekends else weeknights
        var bed = evening.atTime(times.bedtime)
        if (times.bedtime.isBefore(NOON)) {
            bed = bed.plusDays(1)
        }

        val wake = evening.plusDays(1).atTime(times.wake)
        return LocalWindow(bed, if (wake.isAfter(bed)) wake else bed)
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

        // A bedtime before noon belongs to the early hours after the evening.
        private val NOON: LocalTime = LocalTime.NOON

        fun isWeekendNight(evening: LocalDate): Boolean =
            evening.dayOfWeek == DayOfWeek.FRIDAY || evening.dayOfWeek == DayOfWeek.SATURDAY

        fun rate(sleep: Duration): SleepAmount = when {
            sleep >= GoodSleep -> SleepAmount.Good
            sleep >= OkaySleep -> SleepAmount.Okay
            else -> SleepAmount.Short
        }

        /** How long the sleep would be when going to bed at [now] (or at bedtime, if earlier). */
        fun sleepIfBedAt(now: LocalDateTime, night: LocalWindow): Duration {
            val asleep = if (now.isAfter(night.start)) now else night.start
            return if (asleep.isBefore(night.end)) Duration.between(asleep, night.end) else Duration.ZERO
        }
    }
}
