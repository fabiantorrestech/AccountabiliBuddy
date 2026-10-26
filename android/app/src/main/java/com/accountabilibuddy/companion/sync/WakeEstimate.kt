package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.companion.engine.NightKind
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import java.util.Locale

/**
 * The estimated wake time per kind of morning (ADR 40), for mornings woken by the alarm with no alarm known: the median
 * of the last 8 mornings of that kind on which the wake-up app's alarm fell in the morning window (the earliest alarm of
 * each morning), when there are at least 3; else the median of Health Connect's measured wake times by the same rule;
 * else none for that kind.
 */
object WakeEstimate {
    const val MORNINGS = 8
    const val MORNINGS_NEEDED = 3

    // Before this hour a time belongs to that day's morning; from it, to the next day's (an alarm set for 11:50 PM).
    private const val EVENING_FROM_HOUR = 18

    private const val MINUTES_PER_DAY = 24 * 60

    /**
     * [alarms]: the wake-up app's past alarms already found in the morning window. [measuredWakes]: the ends of measured
     * sleep. [kindOf]: the kind of a morning by its date.
     */
    fun of(alarms: List<LocalDateTime>, measuredWakes: List<LocalDateTime>, kindOf: (LocalDate) -> NightKind): Map<NightKind, LocalTime> {
        // The earliest alarm of each morning; the last sleep end before noon of each morning (the woke time).
        val alarmMornings = alarms.groupBy(::morningOf).mapValues { (_, times) -> times.min().toLocalTime() }
        val measuredMornings = measuredWakes
            .filter { it.hour < 12 }
            .groupBy { it.toLocalDate() }
            .mapValues { (_, times) -> times.max().toLocalTime() }

        return NightKind.entries
            .mapNotNull { kind -> (median(alarmMornings, kind, kindOf) ?: median(measuredMornings, kind, kindOf))?.let { kind to it } }
            .toMap()
    }

    /** [time] as the sync file's "HH:mm". */
    fun format(time: LocalTime): String = String.format(Locale.ROOT, "%02d:%02d", time.hour, time.minute)

    private fun morningOf(at: LocalDateTime): LocalDate =
        if (at.hour >= EVENING_FROM_HOUR) at.toLocalDate().plusDays(1) else at.toLocalDate()

    // The median of the last 8 mornings of [kind], or null with fewer than 3.
    private fun median(mornings: Map<LocalDate, LocalTime>, kind: NightKind, kindOf: (LocalDate) -> NightKind): LocalTime? {
        val recent = mornings.entries
            .filter { kindOf(it.key) == kind }
            .sortedByDescending { it.key }
            .take(MORNINGS)
        if (recent.size < MORNINGS_NEEDED) {
            return null
        }

        // Counted from the evening before (6 PM), so a late-evening alarm and an early-morning one sort in order.
        val sorted = recent.map { fromEvening(it.value) }.sorted()
        val middle = sorted.size / 2
        val minutes = if (sorted.size % 2 == 1) sorted[middle] else (sorted[middle - 1] + sorted[middle]) / 2
        return LocalTime.of(0, 0).plusMinutes(((minutes + EVENING_FROM_HOUR * 60) % MINUTES_PER_DAY).toLong())
    }

    private fun fromEvening(time: LocalTime): Int =
        (time.hour * 60 + time.minute - EVENING_FROM_HOUR * 60 + MINUTES_PER_DAY) % MINUTES_PER_DAY
}
