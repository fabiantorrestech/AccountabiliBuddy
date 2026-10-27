package com.accountabilibuddy.shared

import java.time.Duration
import java.time.Instant
import java.time.ZoneId

/**
 * What the snapshot is made from. [parts] is today's plan as the sender has it (the phone's own plan, edits included).
 * [focusDay] is today across the devices' sync files. [running] is the PC's running focus session as its file last said,
 * at [runningAt].
 */
data class SnapshotInput(
    val now: Instant,
    val zone: ZoneId,
    val linked: Boolean,
    val pcName: String?,
    val parts: List<FocusPart>,
    val focusDay: FocusDay?,
    val running: RunningSession?,
    val runningAt: Instant? = null,
    val relayAllowed: Boolean = true,
    /** The devices whose focus is in [focusDay], with their data's age (ADR 22). */
    val sources: List<DataSource> = emptyList(),
    /** Today's flexible lunch on the phone, for the watch's Lunch page; null when the sender has none to give. */
    val lunch: WatchLunch? = null,
)

/**
 * Builds the watch's [WatchSnapshot] (ADR 21) for today. A focus day from another date never reaches today's rings
 * (its dated week totals stay). The session the PC's file last said runs is taken to run until its end, or for up to 30
 * minutes with no set end; after that, or when the PC says nothing runs, an open session ends at that end or now. A
 * session running across midnight shows only its part after midnight.
 */
object SnapshotBuilder {
    private const val WEEK_DAYS = 7L

    /** A session with no set end is taken to run this long after the PC's file said so (the PC writes every ~10 s in a session). */
    val NO_END_LIMIT: Duration = Duration.ofMinutes(30)

    fun build(input: SnapshotInput): WatchSnapshot {
        val today = input.now.atZone(input.zone).toLocalDate()
        val dayStart = today.atStartOfDay(input.zone).toInstant()
        val dayEnd = today.plusDays(1).atStartOfDay(input.zone).toInstant()
        val current = input.focusDay?.takeIf { it.date == today }
        val lastRunning = input.running
        val running = when {
            lastRunning == null -> null
            lastRunning.endsAt != null -> lastRunning.takeIf { it.isLiveAt(input.now) }
            else -> lastRunning.takeIf { input.runningAt != null && Duration.between(input.runningAt, input.now) < NO_END_LIMIT }
        }

        // Nothing runs (any more): an open session ends at the known end, else now.
        val closeAt = lastRunning?.takeIf { !it.isPlanning }?.endsAt?.let { minOf(it, input.now) } ?: input.now
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
            dataAt = input.runningAt ?: input.now,
            relayAllowed = input.relayAllowed,
            sources = input.sources,
            // The goal is a setting, not today's data: an older focus day's goal still holds.
            goalMinutes = input.focusDay?.goalMinutes,
            lunch = input.lunch,
        )

        // The PC's totals for the days before today (dated, so an older focus day still counts), and today live.
        val earlier = input.focusDay?.week.orEmpty().filter { it.date.isBefore(today) && !it.date.isBefore(today.minusDays(WEEK_DAYS - 1)) }
        val week = earlier.sortedBy { it.date } + DayTotal(today, snapshot.focusedMinutes(input.now, input.zone), planned)
        return snapshot.copy(week = week)
    }
}
