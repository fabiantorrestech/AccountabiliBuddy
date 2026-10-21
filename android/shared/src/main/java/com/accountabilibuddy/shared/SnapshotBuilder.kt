package com.accountabilibuddy.shared

import java.time.Instant
import java.time.ZoneId

/**
 * What the snapshot is made from. [parts] is today's plan as the sender has it (the phone's own plan, edits included).
 * [focusDay] is the PC's last "focusDay", which arrived at [focusDayAt]. [running] is the PC's running focus session,
 * known only while [pcConnected].
 */
data class SnapshotInput(
    val now: Instant,
    val zone: ZoneId,
    val linked: Boolean,
    val pcName: String?,
    val parts: List<FocusPart>,
    val focusDay: FocusDay?,
    val focusDayAt: Instant?,
    val running: RunningSession?,
    val pcConnected: Boolean,
)

/**
 * Builds the watch's [WatchSnapshot] (ADR 21) for today. A focus day from another date never reaches today's rings
 * (its dated week totals stay). A session the PC sent as running stays open only while the PC says so; otherwise it
 * ends when the PC last said anything. A session running across midnight shows only its part after midnight.
 */
object SnapshotBuilder {
    private const val WEEK_DAYS = 7L

    fun build(input: SnapshotInput): WatchSnapshot {
        val today = input.now.atZone(input.zone).toLocalDate()
        val dayStart = today.atStartOfDay(input.zone).toInstant()
        val dayEnd = today.plusDays(1).atStartOfDay(input.zone).toInstant()
        val current = input.focusDay?.takeIf { it.date == today }
        val running = input.running.takeIf { input.pcConnected }

        val closeAt = minOf(input.now, input.focusDayAt ?: input.now)
        val sessions = current?.sessions.orEmpty().map { session ->
            if (session.end == null && running == null) session.copy(end = maxOf(session.start, closeAt)) else session
        }.toMutableList()
        if (running != null && sessions.none { it.end == null } && running.startedAt.isBefore(input.now)) {
            // The PC says a session runs that its focus day doesn't have yet.
            sessions += FocusSession(maxOf(running.startedAt, dayStart), null, running.profile, added = false)
        }

        val parts = input.parts.filter { it.end.isAfter(dayStart) && it.start.isBefore(dayEnd) }
        val planned = WatchSnapshot.minutes(parts.filter { it.kind == "watch" }.map { TimeWindow(maxOf(it.start, dayStart), minOf(it.end, dayEnd)) })

        val snapshot = WatchSnapshot(
            createdAt = input.now,
            linked = input.linked,
            pcName = input.pcName,
            date = today,
            parts = parts,
            sessions = sessions.sortedBy { it.start },
            drift = current?.drift.orEmpty(),
            plannedMinutes = planned,
            week = emptyList(),
            running = running,
        )

        // The PC's totals for the days before today (dated, so an older focus day still counts), and today live.
        val earlier = input.focusDay?.week.orEmpty().filter { it.date.isBefore(today) && !it.date.isBefore(today.minusDays(WEEK_DAYS - 1)) }
        val week = earlier.sortedBy { it.date } + DayTotal(today, snapshot.focusedMinutes(input.now, input.zone), planned)
        return snapshot.copy(week = week)
    }
}
