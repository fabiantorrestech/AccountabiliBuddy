package com.accountabilibuddy.watch.model

import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime

/** One stretch on a ring, e.g. a plan part ("Work" 9:00–12:00) or a focus session. */
data class RingSpan(val start: LocalTime, val end: LocalTime, val color: Long, val name: String)

/** The focus session running on the PC, if any. */
data class RunningSession(val profile: String, val startedAt: LocalDateTime, val endsAt: LocalDateTime?, val color: Long) {
    val hasEnd: Boolean get() = endsAt != null
}

/** One day of the week ring: how much was focused against what was planned. */
data class DayTotal(val date: LocalDate, val focusedMinutes: Int, val plannedMinutes: Int)

/**
 * What the watch shows: today's plan and focus on a 24-hour ring, the running session, and the week. For now made
 * from sample data ([SampleData]); later sent by the phone (ADR 21, Proposed).
 */
data class WatchSnapshot(
    val plan: List<RingSpan>,
    val focus: List<RingSpan>,
    val drifts: List<LocalTime>,
    val wake: LocalTime,
    val bedtime: LocalTime,
    val running: RunningSession?,
    val week: List<DayTotal>,
) {
    val focusedMinutes: Int get() = focus.sumOf { minutes(it) }
    val plannedMinutes: Int get() = plan.filter { it.name != "Sleep" && it.name != "Lunch" }.sumOf { minutes(it) }

    private fun minutes(span: RingSpan): Int {
        val start = span.start.toSecondOfDay() / 60
        val end = if (span.end == LocalTime.MIDNIGHT) 24 * 60 else span.end.toSecondOfDay() / 60
        return if (end >= start) end - start else end + 24 * 60 - start
    }
}
