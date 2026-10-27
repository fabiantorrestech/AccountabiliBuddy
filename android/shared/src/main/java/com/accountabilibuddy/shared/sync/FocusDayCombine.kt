package com.accountabilibuddy.shared.sync

import com.accountabilibuddy.shared.DayTotal
import com.accountabilibuddy.shared.FocusDay
import com.accountabilibuddy.shared.FocusSession
import com.accountabilibuddy.shared.TimeWindow
import com.accountabilibuddy.shared.WatchSnapshot
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

/**
 * Today across the user's devices (ADR 22): every device's sync file, each session tagged with the device that counted
 * it; time two devices both counted counts once.
 */
object FocusDayCombine {
    private const val WEEK_DAYS = 7L

    /**
     * @param received Each device's name and its merged sync file.
     * @param now Where a session a device is still running stops counting.
     * @param planParts The imported day plan's parts on a date (ADR 42): today's parts and each day's planned minutes.
     * @return Null when there's nothing to combine.
     */
    fun combine(
        received: List<Pair<String, SyncFile>>,
        today: LocalDate,
        zone: ZoneId,
        now: Instant = Instant.now(),
        planParts: (LocalDate) -> List<com.accountabilibuddy.shared.FocusPart> = { emptyList() },
    ): FocusDay? {
        if (received.isEmpty()) {
            return null
        }

        val dayStart = today.atStartOfDay(zone).toInstant()
        val dayEnd = today.plusDays(1).atStartOfDay(zone).toInstant()
        val todayParts = planParts(today)
        val sessions = received.flatMap { (name, file) -> sessionsOn(file, name, dayStart, dayEnd) }
        val drift = received.flatMap { (_, file) ->
            file.drift.mapNotNull { mark -> clip(mark.start, mark.end, dayStart, dayEnd) }
        }

        val focused = WatchSnapshot.minutes(sessions.mapNotNull { clip(it.start, it.end ?: now, dayStart, dayEnd) })
        val todayPlanned = plannedMinutes(todayParts)
        return FocusDay(
            date = today,
            parts = todayParts,
            sessions = sessions.sortedBy { it.start },
            drift = drift.sortedBy { it.start },
            focusedMinutes = focused,
            plannedMinutes = todayPlanned,
            week = week(received, today, zone, focused, todayPlanned, planParts),
            // ADR 29: the newest PC file's goal (ADR 42).
            goalMinutes = received.map { it.second }
                .filter { it.device.kind == SyncDeviceCard.KIND_PC && it.goalMinutes != null }
                .maxByOrNull { it.device.writtenAt }?.goalMinutes?.takeIf { it > 0 },
        )
    }

    /** The planned work in [parts] (Watch and Comms parts, kind "watch"), in minutes, overlaps once. */
    private fun plannedMinutes(parts: List<com.accountabilibuddy.shared.FocusPart>): Int =
        WatchSnapshot.minutes(parts.filter { it.kind == "watch" }.map { TimeWindow(it.start, it.end) })

    // A device's sessions touching the day, cut to it; one running when its file was written ends at that time. A PC's open piece
    // while it plans starts at the planning end or later (SyncFileBuilder), so it ends before it starts and counts nothing, however
    // long planning is overdue; the watch's WatchSnapshot.openUntil counts an open session as nothing while planning too.
    private fun sessionsOn(file: SyncFile, name: String, dayStart: Instant, dayEnd: Instant): List<FocusSession> =
        file.sessions.mapNotNull { session ->
            val window = clip(session.start, session.end ?: file.device.writtenAt, dayStart, dayEnd) ?: return@mapNotNull null
            FocusSession(window.start, window.end, session.profile, session.added, name)
        }

    /** The last 7 days: today from the union above; earlier days the devices' time that day and the plan's planned work. */
    private fun week(
        received: List<Pair<String, SyncFile>>,
        today: LocalDate,
        zone: ZoneId,
        todayFocused: Int,
        todayPlanned: Int,
        planParts: (LocalDate) -> List<com.accountabilibuddy.shared.FocusPart>,
    ): List<DayTotal> {
        return (WEEK_DAYS - 1 downTo 1).map { back ->
            val date = today.minusDays(back)
            val start = date.atStartOfDay(zone).toInstant()
            val end = date.plusDays(1).atStartOfDay(zone).toInstant()
            val focusedMinutes = WatchSnapshot.minutes(received.flatMap { (name, file) -> sessionsOn(file, name, start, end) }.map { TimeWindow(it.start, it.end!!) })
            // The plan's planned work that day (from the imported plan).
            DayTotal(date, focusedMinutes, plannedMinutes(planParts(date)))
        } + DayTotal(today, todayFocused, todayPlanned)
    }

    private fun clip(start: Instant, end: Instant, from: Instant, to: Instant): TimeWindow? {
        val clippedStart = maxOf(start, from)
        val clippedEnd = minOf(end, to)
        return if (clippedEnd.isAfter(clippedStart)) TimeWindow(clippedStart, clippedEnd) else null
    }
}
