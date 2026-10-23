package com.accountabilibuddy.shared

import java.time.Duration
import java.time.Instant
import java.time.ZoneId

/**
 * What the snapshot is made from. [parts] is today's plan as the sender has it (the phone's own plan, edits included).
 * [focusDay] is the PC's last "focusDay", which arrived at [focusDayAt]. [running] is the PC's running focus session as
 * it last said, at [runningAt] (now while [pcConnected]).
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
    val wifiAllowed: Boolean = true,
    val runningAt: Instant? = null,
    val relayAllowed: Boolean = true,
    /** The devices whose focus is in [focusDay], with their data's age (ADR 22). */
    val sources: List<DataSource> = emptyList(),
)

/**
 * Builds the watch's [WatchSnapshot] (ADR 21) for today. A focus day from another date never reaches today's rings
 * (its dated week totals stay). While the phone isn't connected to the PC (most of the time, ADR 14), the session the
 * PC last said runs is taken to run until its end, or for up to 12 hours with no set end; after that, or when the PC
 * says nothing runs, an open session ends at that end or when the PC last said anything. A session running across
 * midnight shows only its part after midnight.
 */
object SnapshotBuilder {
    private const val WEEK_DAYS = 7L

    // A session with no set end is taken to run this long after the PC last said so.
    private val NO_END_LIMIT: Duration = Duration.ofHours(12)

    fun build(input: SnapshotInput): WatchSnapshot {
        val today = input.now.atZone(input.zone).toLocalDate()
        val dayStart = today.atStartOfDay(input.zone).toInstant()
        val dayEnd = today.plusDays(1).atStartOfDay(input.zone).toInstant()
        val current = input.focusDay?.takeIf { it.date == today }
        val lastRunning = input.running
        val running = when {
            input.pcConnected || lastRunning == null -> lastRunning
            lastRunning.endsAt != null -> lastRunning.takeIf { lastRunning.endsAt.isAfter(input.now) }
            else -> lastRunning.takeIf { input.runningAt != null && Duration.between(input.runningAt, input.now) < NO_END_LIMIT }
        }

        // Nothing runs (any more): an open session ends at the known end, else when the PC last said anything.
        val closeAt = lastRunning?.endsAt?.takeIf { !input.pcConnected }?.let { minOf(it, input.now) }
            ?: minOf(input.now, input.focusDayAt ?: input.now)
        val sessions = current?.sessions.orEmpty().map { session ->
            if (session.end == null && running == null) session.copy(end = maxOf(session.start, closeAt)) else session
        }.toMutableList()
        if (running != null && sessions.none { it.end == null } && running.startedAt.isBefore(input.now)) {
            // The PC says a session runs that its focus day doesn't have yet.
            sessions += FocusSession(maxOf(running.startedAt, dayStart), null, running.profile, added = false, device = input.pcName.takeIf { input.sources.isNotEmpty() })
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
            wifiAllowed = input.wifiAllowed,
            dataAt = if (input.pcConnected) input.now else listOfNotNull(input.focusDayAt, input.runningAt).maxOrNull() ?: input.now,
            relayAllowed = input.relayAllowed,
            sources = input.sources,
            // The goal is a setting, not today's data: an older focus day's goal still holds.
            goalMinutes = input.focusDay?.goalMinutes,
        )

        // The PC's totals for the days before today (dated, so an older focus day still counts), and today live.
        val earlier = input.focusDay?.week.orEmpty().filter { it.date.isBefore(today) && !it.date.isBefore(today.minusDays(WEEK_DAYS - 1)) }
        val week = earlier.sortedBy { it.date } + DayTotal(today, snapshot.focusedMinutes(input.now, input.zone), planned)
        return snapshot.copy(week = week)
    }
}
