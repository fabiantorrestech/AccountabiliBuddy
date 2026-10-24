package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.LocalWindow
import java.time.Duration
import java.time.LocalDate
import java.time.LocalDateTime

/**
 * The past week's measured sleep (Health Connect sessions): last night's total and the average of the nights with
 * any sleep in the 7 before tonight. A session belongs to the night of the day 12 hours before it ended (6:45 AM on
 * the 24th is the night of the 23rd), so tonight's sleep so far never counts.
 */
data class SleepWeek(val lastNight: Duration?, val average: Duration?, val nights: Int) {
    companion object {
        fun of(slept: List<LocalWindow>, now: LocalDateTime): SleepWeek? {
            val tonight = nightOf(now)
            val byNight = slept.filter { it.end.isAfter(it.start) }
                .groupBy { nightOf(it.end) }
                .filterKeys { it.isBefore(tonight) && !it.isBefore(tonight.minusDays(7)) }
                .mapValues { (_, windows) -> total(windows) }
            if (byNight.isEmpty()) {
                return null
            }

            val average = byNight.values.fold(Duration.ZERO, Duration::plus).dividedBy(byNight.size.toLong())
            return SleepWeek(byNight[tonight.minusDays(1)], average, byNight.size)
        }

        private fun nightOf(time: LocalDateTime): LocalDate = time.minusHours(12).toLocalDate()

        // Overlapping sessions (two sources) count once.
        private fun total(windows: List<LocalWindow>): Duration =
            DayTimeline.union(windows).fold(Duration.ZERO) { sum, window -> sum.plus(Duration.between(window.start, window.end)) }
    }
}
