package com.accountabilibuddy.companion.stats

import com.accountabilibuddy.companion.engine.DayTimeline
import com.accountabilibuddy.companion.model.LocalWindow
import java.time.Duration
import java.time.LocalDate
import java.time.LocalDateTime

/**
 * One day in detail (the PC's Stats right column, board 5k): the 12 AM to 12 AM strip with focus, drift and planned sleep, deep
 * work against the goal, the stretches off track (times only) and the day's sessions with the device that counted each.
 */
object DayPanels {
    private const val DAY_SECONDS = 86_400f

    /** @param sleepOn The planned nights touching a date, from the sleep schedule (empty with sleep mode off). */
    fun build(data: StatsData, date: LocalDate, sleepOn: (LocalDate) -> List<LocalWindow>): DayPanel {
        val start = date.atStartOfDay()
        val day = LocalWindow(start, start.plusDays(1))
        val spans = mutableListOf<DayStripSpan>()
        sleepOn(date).forEach { add(spans, day, it, DayStripKind.Sleep, "Planned sleep") }
        data.windows.forEach { add(spans, day, it, DayStripKind.Focus, "Focus") }
        val visits = data.driftBetween(date, date)
        visits.forEach { add(spans, day, LocalWindow(local(data, it.start), local(data, it.end)), DayStripKind.Drift, "Drift") }

        val focused = data.dayOf(date)?.focused ?: Duration.ZERO
        val goal = data.goal
        val met = goal != null && focused >= goal
        val note = when {
            goal == null -> ""
            met -> "goal met"
            date == data.today -> "${StatsFormat.duration(goal.minus(focused))} to go"
            else -> "goal ${StatsFormat.duration(goal)}"
        }

        // The sessions that touch the day, cut to it: one that began the evening before still counts for the hours after midnight.
        val devices = data.sessions.map { it.device }.distinct().size
        val rows = mutableListOf<Pair<LocalDateTime, DayListRow>>()
        data.sessions.forEach { session ->
            val shown = DayTimeline.clip(LocalWindow(session.start, session.end), day) ?: return@forEach
            val title = if (devices > 1) "${session.title} · ${session.device}" else session.title
            rows += shown.start to DayListRow(StatsFormat.time(shown.start), title, StatsFormat.duration(Duration.between(shown.start, shown.end)), isDrift = false)
        }

        visits.forEach { visit ->
            val at = local(data, visit.start)
            val minutes = maxOf(1, Math.rint(StatsFormat.minutes(visit.length)).toInt())
            rows += at to DayListRow(StatsFormat.time(at), "Off track, $minutes min", "off track", isDrift = true)
        }

        val driftTime = visits.fold(Duration.ZERO) { sum, visit -> sum.plus(visit.length) }
        return DayPanel(
            date, StatsFormat.dayText(date), spans, StatsFormat.hoursMinutes(focused), note, met, visits.size, StatsFormat.duration(driftTime),
            rows.sortedBy { it.first }.map { it.second }, data.dayOf(date) != null,
        )
    }

    private fun add(spans: MutableList<DayStripSpan>, day: LocalWindow, window: LocalWindow, kind: DayStripKind, name: String) {
        val shown = DayTimeline.clip(window, day) ?: return
        val from = Duration.between(day.start, shown.start).seconds / DAY_SECONDS
        val to = Duration.between(day.start, shown.end).seconds / DAY_SECONDS
        spans += DayStripSpan(from, to, kind, "$name ${StatsFormat.time(shown.start)} – ${StatsFormat.time(shown.end)}")
    }

    private fun local(data: StatsData, at: java.time.Instant): LocalDateTime = LocalDateTime.ofInstant(at, data.zone)
}
