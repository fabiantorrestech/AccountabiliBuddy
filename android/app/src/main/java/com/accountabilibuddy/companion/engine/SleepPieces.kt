package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.LocalWindow
import java.time.Duration

/**
 * One measured sleep split where the user was awake for a while (Health Connect's awake, awake-in-bed and out-of-bed
 * stages): waking at 5:30 and falling asleep again at 6:00 is two pieces, so the gap isn't counted as sleep, the slept
 * strip shows it and the night's last piece ends at the real wake. Short wakes (under [MIN_GAP]) stay sleep.
 */
object SleepPieces {
    val MIN_GAP: Duration = Duration.ofMinutes(15)

    fun split(session: LocalWindow, awake: List<LocalWindow>): List<LocalWindow> {
        val gaps = DayTimeline.union(awake.mapNotNull { DayTimeline.clip(it, session) })
            .filter { Duration.between(it.start, it.end) >= MIN_GAP }
        if (gaps.isEmpty()) return listOf(session)

        val pieces = mutableListOf<LocalWindow>()
        var start = session.start
        for (gap in gaps.sortedBy { it.start }) {
            if (gap.start.isAfter(start)) pieces += LocalWindow(start, gap.start)
            start = gap.end
        }
        if (session.end.isAfter(start)) pieces += LocalWindow(start, session.end)
        return pieces
    }
}
