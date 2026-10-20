package com.accountabilibuddy.companion.usage

import com.accountabilibuddy.companion.engine.DayTimeline
import com.accountabilibuddy.companion.model.LocalWindow
import java.time.Duration

/**
 * Turns "app came to the front / left the front" moments into time per app, and the part of it inside planned
 * focus windows. Pure (no Android), so it can be checked on its own.
 */
object UsageIntervals {
    /** A change: [packageName] came to the front ([resumed]) or left it; null package = the screen went off. */
    data class Change(val time: java.time.LocalDateTime, val packageName: String?, val resumed: Boolean)

    /** Front-app windows from a time-ordered list of changes, cut to [range]; the last one runs until [range]'s end. */
    fun windows(changes: List<Change>, range: LocalWindow): Map<String, List<LocalWindow>> {
        val result = mutableMapOf<String, MutableList<LocalWindow>>()
        var current: String? = null
        var since = range.start

        fun close(at: java.time.LocalDateTime) {
            val app = current ?: return
            DayTimeline.clip(LocalWindow(since, at), range)?.let { result.getOrPut(app) { mutableListOf() }.add(it) }
            current = null
        }

        for (change in changes.sortedBy { it.time }) {
            when {
                change.packageName == null -> close(change.time)
                change.resumed -> {
                    close(change.time)
                    current = change.packageName
                    since = change.time
                }
                change.packageName == current -> close(change.time)
            }
        }

        close(range.end)
        return result
    }

    fun total(windows: List<LocalWindow>): Duration =
        DayTimeline.union(windows).fold(Duration.ZERO) { sum, w -> sum.plus(Duration.between(w.start, w.end)) }

    /** How much of [windows] falls inside [focus]. */
    fun inside(windows: List<LocalWindow>, focus: List<LocalWindow>): Duration {
        val merged = DayTimeline.union(focus)
        return windows.fold(Duration.ZERO) { sum, window ->
            merged.fold(sum) { inner, f -> DayTimeline.clip(window, f)?.let { inner.plus(Duration.between(it.start, it.end)) } ?: inner }
        }
    }
}
