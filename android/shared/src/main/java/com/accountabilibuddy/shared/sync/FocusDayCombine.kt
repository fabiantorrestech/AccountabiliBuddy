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
 * Today across the user's devices (ADR 22): the linked PC's live focus day plus the other devices' sync files, each
 * session tagged with the device that counted it. The linked PC's live day wins over its own sync file (found by its
 * name); time two devices both counted counts once.
 */
object FocusDayCombine {
    private const val WEEK_DAYS = 7L

    /**
     * @param received Each other device's name and its merged sync file.
     * @param now Where a session the linked PC is still running stops counting.
     * @return Null when there's nothing to combine.
     */
    fun combine(
        linkedPc: FocusDay?,
        linkedPcName: String?,
        received: List<Pair<String, SyncFile>>,
        today: LocalDate,
        zone: ZoneId,
        now: Instant = Instant.now(),
    ): FocusDay? {
        val others = received.filter { (name, file) -> !(linkedPc != null && file.device.kind == SyncDeviceCard.KIND_PC && name == linkedPcName) }
        if (linkedPc == null && others.isEmpty()) {
            return null
        }

        val dayStart = today.atStartOfDay(zone).toInstant()
        val dayEnd = today.plusDays(1).atStartOfDay(zone).toInstant()
        val live = linkedPc?.takeIf { it.date == today }
        val sessions = live?.sessions.orEmpty().map { it.copy(device = linkedPcName) } +
            others.flatMap { (name, file) -> sessionsOn(file, name, dayStart, dayEnd) }
        val drift = live?.drift.orEmpty() + others.flatMap { (_, file) ->
            file.drift.mapNotNull { mark -> clip(mark.start, mark.end, dayStart, dayEnd) }
        }

        val focused = WatchSnapshot.minutes(sessions.mapNotNull { clip(it.start, it.end ?: now, dayStart, dayEnd) })
        return FocusDay(
            date = today,
            parts = live?.parts.orEmpty(),
            sessions = sessions.sortedBy { it.start },
            drift = drift.sortedBy { it.start },
            focusedMinutes = focused,
            plannedMinutes = live?.plannedMinutes ?: 0,
            week = week(live, others, today, zone, focused),
        )
    }

    // A device's sessions touching the day, cut to it; one running when its file was written ends at that time.
    private fun sessionsOn(file: SyncFile, name: String, dayStart: Instant, dayEnd: Instant): List<FocusSession> =
        file.sessions.mapNotNull { session ->
            val window = clip(session.start, session.end ?: file.device.writtenAt, dayStart, dayEnd) ?: return@mapNotNull null
            FocusSession(window.start, window.end, session.profile, session.added, name)
        }

    /**
     * The last 7 days: today from the union above; earlier days the linked PC's own total plus the other devices'
     * time that day (their days aren't in the PC's total, and the PC's earlier windows aren't known here).
     */
    private fun week(live: FocusDay?, others: List<Pair<String, SyncFile>>, today: LocalDate, zone: ZoneId, todayFocused: Int): List<DayTotal> {
        val pcDays = live?.week.orEmpty().associateBy { it.date }
        return (WEEK_DAYS - 1 downTo 1).map { back ->
            val date = today.minusDays(back)
            val start = date.atStartOfDay(zone).toInstant()
            val end = date.plusDays(1).atStartOfDay(zone).toInstant()
            val othersMinutes = WatchSnapshot.minutes(others.flatMap { (name, file) -> sessionsOn(file, name, start, end) }.map { TimeWindow(it.start, it.end!!) })
            val pc = pcDays[date]
            DayTotal(date, (pc?.focusedMinutes ?: 0) + othersMinutes, pc?.plannedMinutes ?: 0)
        } + DayTotal(today, todayFocused, live?.plannedMinutes ?: 0)
    }

    private fun clip(start: Instant, end: Instant, from: Instant, to: Instant): TimeWindow? {
        val clippedStart = maxOf(start, from)
        val clippedEnd = minOf(end, to)
        return if (clippedEnd.isAfter(clippedStart)) TimeWindow(clippedStart, clippedEnd) else null
    }
}
