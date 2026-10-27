package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.model.CategoryBehavior
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.temporal.ChronoUnit

/**
 * One day at a glance: which stretch of time "today" covers and the work the day plan has in it.
 * A port of the Windows Core's DayTimeline (ADR 13: same behaviour, kept in step by hand). Local wall-clock times.
 */
object DayTimeline {
    /**
     * The day [now] belongs to, from [dayStart] to [dayEnd]. When the end is not after the start, the day ends on
     * the next date, and until that end the time still belongs to the previous day.
     */
    fun dayContaining(now: LocalDateTime, dayStart: LocalTime, dayEnd: LocalTime): LocalWindow {
        val endsNextDay = !dayEnd.isAfter(dayStart)
        var date = now.toLocalDate()
        if (endsNextDay && now.toLocalTime().isBefore(dayEnd)) {
            date = date.minusDays(1)
        }

        val start = date.atTime(dayStart)
        val end = date.atTime(dayEnd)
        return LocalWindow(start, if (endsNextDay) end.plusDays(1) else end)
    }

    /** Midnight to midnight of the date [now] is on. */
    fun fullDay(now: LocalDateTime): LocalWindow {
        val midnight = now.toLocalDate().atStartOfDay()
        return LocalWindow(midnight, midnight.plusDays(1))
    }

    /** The whole calendar dates [day] touches, from the midnight before it to the one after. */
    fun calendarSpan(day: LocalWindow): LocalWindow {
        val endMidnight = day.end.toLocalDate().atStartOfDay()
        val end = if (day.end == endMidnight) day.end else endMidnight.plusDays(1)
        return LocalWindow(day.start.toLocalDate().atStartOfDay(), end)
    }

    /** Grows [day] to whole hours so it covers every window in [content], but never past [limit]. */
    fun fitTo(day: LocalWindow, content: Iterable<LocalWindow>, limit: LocalWindow): LocalWindow {
        var start = day.start
        var end = day.end
        for (window in content) {
            if (window.start.isBefore(start)) {
                start = window.start.truncatedTo(ChronoUnit.HOURS)
            }

            if (window.end.isAfter(end)) {
                end = ceilingToHour(window.end)
            }
        }

        return LocalWindow(maxOf(start, limit.start), minOf(end, limit.end))
    }

    /** Merges overlapping and touching windows, earliest first. */
    fun union(windows: Iterable<LocalWindow>): List<LocalWindow> {
        val merged = mutableListOf<LocalWindow>()
        for (window in windows.sortedBy { it.start }) {
            val last = merged.lastOrNull()
            if (last != null && !window.start.isAfter(last.end)) {
                merged[merged.size - 1] = last.copy(end = maxOf(last.end, window.end))
            } else {
                merged.add(window)
            }
        }

        return merged
    }

    /** What's left of [windows] outside every one of [holes], earliest first (e.g. work time with no focus session). */
    fun subtract(windows: Iterable<LocalWindow>, holes: Iterable<LocalWindow>): List<LocalWindow> {
        val cuts = union(holes)
        val left = mutableListOf<LocalWindow>()
        for (window in union(windows)) {
            var from = window.start
            for (cut in cuts.filter { it.end.isAfter(window.start) && it.start.isBefore(window.end) }) {
                if (cut.start.isAfter(from)) {
                    left.add(LocalWindow(from, cut.start))
                }

                from = maxOf(from, cut.end)
            }

            if (window.end.isAfter(from)) {
                left.add(LocalWindow(from, window.end))
            }
        }

        return left
    }

    /** The part of [window] inside [range], or null if none. */
    fun clip(window: LocalWindow, range: LocalWindow): LocalWindow? {
        val start = maxOf(window.start, range.start)
        val end = minOf(window.end, range.end)
        return if (end.isAfter(start)) LocalWindow(start, end) else null
    }

    private fun ceilingToHour(time: LocalDateTime): LocalDateTime {
        val floor = time.truncatedTo(ChronoUnit.HOURS)
        return if (floor == time) time else floor.plusHours(1)
    }
}
