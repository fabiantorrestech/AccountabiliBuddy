package com.accountabilibuddy.shared.sync

import java.time.Duration
import java.time.Instant

/**
 * Merges a shared list item by item (ADR 48): the next session's tasks and the come-backs. Each item has a stable id.
 * For one id the newest version wins; a version's time is its edit time, or its deletion time when that is later, so a
 * tombstone newer than the last edit removes the item and an edit newer than the tombstone brings it back. Items that
 * only one list has are all kept, so two devices editing different items both survive. Tombstones are kept for
 * [TOMBSTONES_KEPT]. The PC's twin is Core's SharedListMerge; both give the same result.
 */
object SharedListMerge {
    /** How long a tombstone is kept and sent, so a device that was away that long can still learn of a deletion. */
    val TOMBSTONES_KEPT: Duration = Duration.ofDays(30)

    /**
     * The merged list, tombstones included, ordered by position and then id; the same whichever list is [first].
     */
    fun <T : SharedItem> merge(first: List<T>, second: List<T>, now: Instant): List<T> {
        val winners = LinkedHashMap<String, T>()
        for (item in first + second) {
            val key = item.id.lowercase()
            val held = winners[key]
            if (held == null || compare(item, held) > 0) {
                winners[key] = item
            }
        }

        val keptFrom = now.minus(TOMBSTONES_KEPT)
        return winners.values
            .filter { item -> item.deletedAt?.let { it.ticks() > keptFrom.ticks() } ?: true }
            .sortedWith(compareBy<T> { it.position }.thenBy { it.id.lowercase() })
    }

    /** A position between two neighbours (either may be null at an end of the list). */
    fun positionBetween(before: Double?, after: Double?): Double = when {
        before != null && after != null -> (before + after) / 2
        before != null -> before + 1
        after != null -> after - 1
        else -> 1.0
    }

    // More than 0 when [candidate] is the version to keep over [held].
    private fun compare(candidate: SharedItem, held: SharedItem): Int {
        val byTime = stamp(candidate).compareTo(stamp(held))
        if (byTime != 0) {
            return byTime
        }

        // The same time: a deletion wins, then the later position, then the fields, so the order the lists arrive in never matters.
        val byDeletion = (candidate.deletedAt != null).compareTo(held.deletedAt != null)
        if (byDeletion != 0) {
            return byDeletion
        }

        val byPosition = candidate.position.compareTo(held.position)
        return if (byPosition != 0) byPosition else candidate.tieBreak.compareTo(held.tieBreak)
    }

    private fun stamp(item: SharedItem): Long {
        val edited = item.editedAt.ticks()
        val deleted = item.deletedAt?.ticks()
        return if (deleted != null && deleted > edited) deleted else edited
    }
}
