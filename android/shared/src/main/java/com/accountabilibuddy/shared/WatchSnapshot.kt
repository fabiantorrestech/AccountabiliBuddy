package com.accountabilibuddy.shared

import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

/** The focus session running on the PC: its profile, when this phase started and when it ends (null: no set end). */
data class RunningSession(val profile: String?, val startedAt: Instant, val endsAt: Instant?, val onBreak: Boolean)

/**
 * What the watch shows (ADR 21), as the phone sends it: [date]'s plan parts, the focus sessions that count (cut to the
 * day; one with no end runs until now), drift marks, planned minutes, the week, and the session running on the PC.
 * [linked] false tells the watch it's no longer linked. Never titles, addresses, reasons or notes.
 */
data class WatchSnapshot(
    val createdAt: Instant,
    val linked: Boolean,
    val pcName: String?,
    val date: LocalDate,
    val parts: List<FocusPart>,
    val sessions: List<FocusSession>,
    val drift: List<TimeWindow>,
    val plannedMinutes: Int,
    val week: List<DayTotal>,
    val running: RunningSession?,
) {
    /** Focused on [date] up to [now]: each session cut to the day, one with no end until [now], overlaps counted once. */
    fun focusedMinutes(now: Instant, zone: ZoneId): Int {
        val dayStart = date.atStartOfDay(zone).toInstant()
        val dayEnd = date.plusDays(1).atStartOfDay(zone).toInstant()
        val until = minOf(now, dayEnd)
        val windows = sessions.mapNotNull { session ->
            val start = maxOf(session.start, dayStart)
            val end = minOf(session.end ?: until, until)
            if (end.isAfter(start)) TimeWindow(start, end) else null
        }
        return minutes(windows)
    }

    companion object {
        /** Whole minutes of [windows], overlaps counted once (rounded to the nearest minute). */
        fun minutes(windows: List<TimeWindow>): Int {
            var seconds = 0L
            var reached: Instant? = null
            for (window in windows.sortedBy { it.start }) {
                val start = reached?.let { maxOf(it, window.start) } ?: window.start
                if (window.end.isAfter(start)) {
                    seconds += Duration.between(start, window.end).seconds
                }
                reached = reached?.let { maxOf(it, window.end) } ?: window.end
            }
            return ((seconds + 30) / 60).toInt()
        }
    }
}
