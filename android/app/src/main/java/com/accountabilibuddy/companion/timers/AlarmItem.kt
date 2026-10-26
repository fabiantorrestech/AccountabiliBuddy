package com.accountabilibuddy.companion.timers

import java.time.DayOfWeek
import java.time.Instant
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.ZoneId

/**
 * An alarm on this phone (V1, local only): its name, a time of day and the days it rings (none = once). [nextMs] is
 * when it rings next (null while off); [snoozeUntilMs] a snooze (9 min) still to ring.
 */
data class AlarmItem(
    val id: Int,
    val name: String,
    val time: LocalTime,
    val days: Set<DayOfWeek> = emptySet(),
    val on: Boolean = true,
    val nextMs: Long? = null,
    val snoozeUntilMs: Long? = null,
) {
    val once: Boolean get() = days.isEmpty()

    /** The soonest of its next ring and its snooze; null when there's neither. */
    fun dueMs(): Long? = listOfNotNull(nextMs.takeIf { on }, snoozeUntilMs).minOrNull()

    /** The first time after [after] it rings: its time on the next allowed day (any day when it's once). */
    fun nextAfter(after: LocalDateTime): LocalDateTime {
        var candidate = after.toLocalDate().atTime(time)
        while (!candidate.isAfter(after) || (days.isNotEmpty() && candidate.dayOfWeek !in days)) {
            candidate = candidate.plusDays(1)
        }
        return candidate
    }

    /** [nextAfter] in epoch milliseconds, in the phone's time zone. */
    fun nextAfterMs(afterMs: Long): Long {
        val zone = ZoneId.systemDefault()
        val after = LocalDateTime.ofInstant(Instant.ofEpochMilli(afterMs), zone)
        return nextAfter(after).atZone(zone).toInstant().toEpochMilli()
    }

    companion object {
        const val DEFAULT_NAME = "Alarm"
    }
}
