package com.accountabilibuddy.companion.stats

import com.accountabilibuddy.shared.sync.SyncDrift
import java.time.Duration

/** Turns the drift of the received files into visits: one per visit to a place, as the PC's DriftMarks does. */
object DriftMarks {
    /** Leaving and coming back within this is one visit. */
    val GAP: Duration = Duration.ofMinutes(2)

    /** The received drift stretches as visits, each keyed by its site, else its app; ordered by start, joined as [merge] says. */
    fun of(drift: List<SyncDrift>): List<DriftVisit> = merge(drift.map { DriftVisit(it.start, it.end, it.site ?: it.app) }, GAP)

    /**
     * Joins stretches on the same place when one starts within [gap] of the previous one's end, so leaving and coming back
     * soon is one visit, even with another place in between.
     */
    fun merge(stretches: List<DriftVisit>, gap: Duration): List<DriftVisit> {
        val marks = mutableListOf<DriftVisit>()
        for ((_, place) in stretches.groupBy { it.key.lowercase() }) {
            var current: DriftVisit? = null
            for (stretch in place.sortedBy { it.start }) {
                val open = current
                if (open != null && Duration.between(open.end, stretch.start) <= gap) {
                    current = open.copy(end = if (stretch.end.isAfter(open.end)) stretch.end else open.end)
                    continue
                }

                if (open != null) marks += open
                current = stretch
            }

            current?.let { marks += it }
        }

        return marks.sortedBy { it.start }
    }
}
