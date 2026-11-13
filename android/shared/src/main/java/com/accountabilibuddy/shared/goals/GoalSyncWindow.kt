package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.DotNetTime
import java.time.Duration
import java.time.OffsetDateTime

/**
 * Which entries go into the regular sync file (ADR 79; the PC's `GoalSyncWindow`): the ones edited in the last 90 days (a deletion
 * counts as an edit); the rest of the history travels in the month and year files. Newer items that can't be read stay.
 */
object GoalSyncWindow {
    const val DAYS = 90
    val WINDOW: Duration = Duration.ofDays(DAYS.toLong())

    /** The entries (and newer items at their places) stamped within the window; the list as written, each kept or not. */
    fun recent(lists: GoalLists, now: OffsetDateTime): GoalLists {
        val from = now.minus(WINDOW)
        val newer = lists.newer.entries.sortedBy { it.index }
        val known = ArrayList<GoalEntry>()
        val keptNewer = ArrayList<GoalNewerItem>()
        var nextNewer = 0
        var written = 0
        fun takeNewer(all: Boolean) {
            while (nextNewer < newer.size && (all || newer[nextNewer].index <= written)) {
                val item = newer[nextNewer++]
                val raw = GoalRawItem.tryRead(item.json)
                if (raw == null || DotNetTime.compare(later(raw.editedAt, raw.deletedAt), from) >= 0) {
                    keptNewer.add(item.copy(index = known.size + keptNewer.size))
                }
                written++
            }
        }

        for (entry in lists.entries) {
            takeNewer(all = false)
            if (DotNetTime.compare(entry.stamp, from) >= 0) known.add(entry)
            written++
        }
        takeNewer(all = true)
        return lists.copy(entries = known, newer = lists.newer.copy(entries = keptNewer))
    }

    private fun later(editedAt: OffsetDateTime, deletedAt: OffsetDateTime?): OffsetDateTime =
        if (deletedAt != null && DotNetTime.compare(deletedAt, editedAt) > 0) deletedAt else editedAt
}
