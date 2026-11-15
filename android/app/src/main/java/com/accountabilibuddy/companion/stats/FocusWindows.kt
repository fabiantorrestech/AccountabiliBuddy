package com.accountabilibuddy.companion.stats

import com.accountabilibuddy.companion.engine.DayTimeline
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.shared.sync.GroupSessions
import com.accountabilibuddy.shared.sync.ReceivedSyncFile
import com.accountabilibuddy.shared.sync.SyncFile
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Counted focus across the user's devices, as the PC's Stats adds it up (Core's DeepWork): every received session as a local
 * wall-clock window, time two devices both had in focus counted once. Plain Kotlin, no Android types.
 */
object FocusWindows {
    /**
     * The stretches of counted focus in [files] (each device's merged sync file with its arrival time), earliest first, overlaps merged. A
     * session still open ends where [GroupSessions.openPieceEnd] says, for any device: when it was last heard from (a stamp ahead of the
     * arrival is not believed), never after [now], and where the user went away (`awaySince`), so a skewed clock can't count focus time
     * that hasn't happened and paused time is never focus (ADR 85, plan check B4, PS1a review B1, B2). One that starts after that (a PC's
     * open piece while it plans) counts for nothing.
     *
     * @param now The trusted time.
     */
    fun of(files: List<ReceivedSyncFile>, zone: ZoneId, now: Instant): List<LocalWindow> {
        val windows = mutableListOf<LocalWindow>()
        for (received in files) {
            val file = received.file
            val written = GroupSessions.openPieceEnd(file, received.receivedAt, now)
            for (session in file.sessions) {
                if (session.end == null && !written.isAfter(session.start)) continue
                val end = session.end ?: written
                if (end.isAfter(session.start)) windows += LocalWindow(local(session.start, zone), local(end, zone))
            }
        }

        return DayTimeline.union(windows)
    }

    /** Each date from [from] to [to] with the part of [focused] inside it, empty days included (the PC's DeepWork.DaysIn). */
    fun daysIn(focused: List<LocalWindow>, from: LocalDate, to: LocalDate): List<StatsDay> {
        val days = mutableListOf<StatsDay>()
        var date = from
        while (!date.isAfter(to)) {
            val start = date.atStartOfDay()
            val day = LocalWindow(start, start.plusDays(1))
            val pieces = focused.mapNotNull { DayTimeline.clip(it, day) }.map { Duration.between(it.start, it.end) }
            days += StatsDay(date, pieces.fold(Duration.ZERO) { sum, piece -> sum.plus(piece) })
            date = date.plusDays(1)
        }

        return days
    }

    fun local(instant: Instant, zone: ZoneId): LocalDateTime = LocalDateTime.ofInstant(instant, zone)
}
