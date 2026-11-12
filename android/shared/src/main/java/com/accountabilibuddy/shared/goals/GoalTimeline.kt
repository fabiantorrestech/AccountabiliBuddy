package com.accountabilibuddy.shared.goals

/** Window maths the goals need, the same as the PC's `DayTimeline.Union` and `Clip`. */
object GoalTimeline {
    /** Merges overlapping and touching windows, earliest first. */
    fun union(windows: Iterable<GoalWindow>): List<GoalWindow> {
        val merged = mutableListOf<GoalWindow>()
        for (window in windows.sortedBy { it.start }) {
            val last = merged.lastOrNull()
            if (last != null && window.start <= last.end) {
                merged[merged.size - 1] = last.copy(end = maxOf(last.end, window.end))
            } else {
                merged += window
            }
        }

        return merged
    }

    /** The part of [window] inside [range]; null when they don't overlap. */
    fun clip(window: GoalWindow, range: GoalWindow): GoalWindow? {
        val start = maxOf(window.start, range.start)
        val end = minOf(window.end, range.end)
        return if (end > start) GoalWindow(start, end) else null
    }
}
