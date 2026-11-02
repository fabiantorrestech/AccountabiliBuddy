package com.accountabilibuddy.companion.stats

import com.accountabilibuddy.companion.engine.DayTimeline
import com.accountabilibuddy.companion.model.LocalWindow
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
     * The stretches of counted focus in [files] (each device's merged sync file), earliest first, overlaps merged. A session
     * still open when its file was written ends then; one that starts after that (a PC's open piece while it plans) counts for
     * nothing and is left out, as on the PC.
     */
    fun of(files: List<SyncFile>, zone: ZoneId): List<LocalWindow> {
        val windows = mutableListOf<LocalWindow>()
        for (file in files) {
            val written = file.device.writtenAt
            for (session in file.sessions) {
                if (session.end == null && session.start.isAfter(written)) continue
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
            days += StatsDay(date, pieces.fold(Duration.ZERO) { sum, piece -> sum.plus(piece) }, pieces.maxOrNull() ?: Duration.ZERO)
            date = date.plusDays(1)
        }

        return days
    }

    fun local(instant: Instant, zone: ZoneId): LocalDateTime = LocalDateTime.ofInstant(instant, zone)
}
