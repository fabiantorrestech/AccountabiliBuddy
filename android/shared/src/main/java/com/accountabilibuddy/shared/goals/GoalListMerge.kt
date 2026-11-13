package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.DotNetTime
import java.time.Duration
import java.time.OffsetDateTime

/**
 * Merges a goal list item by item (the PC's `SharedListMerge`, on goal items so their times keep their offsets): for one id the newest
 * version wins (its edit time, or its deletion time when later); the same time: a deletion wins, then the later position, then the
 * item's text, so the order the lists come in never matters. A time more than [FUTURE_LIMIT] past the receiver's now counts as that
 * far ahead. Tombstones are kept for [TOMBSTONES_KEPT] unless asked to keep them for good.
 */
object GoalListMerge {
    val TOMBSTONES_KEPT: Duration = Duration.ofDays(30)
    val FUTURE_LIMIT: Duration = Duration.ofMinutes(2)

    /** The merged list, tombstones included, ordered by position and then id text. */
    fun merge(first: List<GoalMergeItem>, second: List<GoalMergeItem>, now: OffsetDateTime, keepTombstones: Boolean = false): List<GoalMergeItem> {
        val limit = now.plus(FUTURE_LIMIT)
        val winners = LinkedHashMap<java.util.UUID, GoalMergeItem>()
        for (original in first + second) {
            val item = clamped(original, limit)
            val held = winners[item.id]
            if (held == null || compare(item, held) > 0) winners[item.id] = item
        }

        val keptFrom = now.minus(TOMBSTONES_KEPT)
        return winners.values
            .filter { item -> keepTombstones || item.deletedAt == null || DotNetTime.compare(item.deletedAt!!, keptFrom) > 0 }
            .sortedWith { a, b ->
                val byPosition = comparePosition(a.position, b.position)
                if (byPosition != 0) byPosition else a.id.toString().compareTo(b.id.toString())
            }
    }

    /** The item's newest time: its edit time, or its deletion time when that is later. */
    fun stampOf(item: GoalMergeItem): OffsetDateTime {
        val deleted = item.deletedAt
        return if (deleted != null && DotNetTime.compare(deleted, item.editedAt) > 0) deleted else item.editedAt
    }

    /** The item with its times no later than [limit] (the future clamp keeps [limit]'s offset). */
    fun clamped(item: GoalMergeItem, limit: OffsetDateTime): GoalMergeItem {
        val deleted = item.deletedAt
        if (DotNetTime.compare(item.editedAt, limit) <= 0 && (deleted == null || DotNetTime.compare(deleted, limit) <= 0)) return item
        val edited = if (DotNetTime.compare(item.editedAt, limit) > 0) limit else item.editedAt
        val clampedDeleted = if (deleted != null && DotNetTime.compare(deleted, limit) > 0) limit else deleted
        return item.withStamps(edited, clampedDeleted)
    }

    /** .NET's double order: NaN first, zeros equal. */
    fun comparePosition(a: Double, b: Double): Int = when {
        a < b -> -1
        a > b -> 1
        a == b -> 0
        a.isNaN() -> if (b.isNaN()) 0 else -1
        else -> 1
    }

    // More than 0 when [candidate] is the version to keep over [held].
    private fun compare(candidate: GoalMergeItem, held: GoalMergeItem): Int {
        val byTime = DotNetTime.compare(stampOf(candidate), stampOf(held))
        if (byTime != 0) return byTime
        val byDeletion = (candidate.deletedAt != null).compareTo(held.deletedAt != null)
        if (byDeletion != 0) return byDeletion
        val byPosition = comparePosition(candidate.position, held.position)
        return if (byPosition != 0) byPosition else candidate.tieBreak.compareTo(held.tieBreak)
    }
}
