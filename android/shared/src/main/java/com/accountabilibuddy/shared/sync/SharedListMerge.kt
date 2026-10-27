package com.accountabilibuddy.shared.sync

import java.time.Duration
import java.time.Instant

/**
 * Merges a shared list item by item (ADR 48): the next session's tasks and the come-backs. Each item has a stable id.
 * For one id the newest version wins; a version's time is its edit time, or its deletion time when that is later, so a
 * tombstone newer than the last edit removes the item and an edit newer than the tombstone brings it back. Items that
 * only one list has are all kept, so two devices editing different items both survive. A time more than
 * [FUTURE_LIMIT] past the receiver's now is taken as that far ahead. Tombstones are kept for [TOMBSTONES_KEPT]. The
 * PC's twin is Core's SharedListMerge; both give the same result.
 */
object SharedListMerge {
    /** How long a tombstone is kept and sent, so a device that was away that long can still learn of a deletion. */
    val TOMBSTONES_KEPT: Duration = Duration.ofDays(30)

    /**
     * How far past the receiver's own time an incoming edit or deletion time may be: a later one (a device whose clock
     * runs fast) is taken as this far ahead, so it can't win over every later edit for good.
     */
    val FUTURE_LIMIT: Duration = Duration.ofMinutes(2)

    /**
     * The merged list, tombstones included, ordered by position and then id; the same whichever list is [first].
     */
    fun <T : SharedItem> merge(first: List<T>, second: List<T>, now: Instant): List<T> {
        val limit = now.plus(FUTURE_LIMIT)
        val winners = LinkedHashMap<String, T>()
        for (item in (first + second).map { clamped(it, limit) }) {
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

    /**
     * The edit time for a local change to [held] (null for a new item): [now], or one tick past the item's own newest
     * time when that is later, so a change made right after a faster clock's edit still wins.
     */
    fun nextStamp(now: Instant, held: SharedItem?): Instant {
        if (held == null) {
            return now
        }

        val after = Instant.ofEpochSecond(stampOf(held).epochSecond, (stampOf(held).nano / 100 + 1) * 100L)
        return if (after.ticks() > now.ticks()) after else now
    }

    /** A position between two neighbours (either may be null at an end of the list). */
    fun positionBetween(before: Double?, after: Double?): Double = when {
        before != null && after != null -> (before + after) / 2
        before != null -> before + 1
        after != null -> after - 1
        else -> 1.0
    }

    /** The item's newest time: its edit time, or its deletion time when that is later. */
    fun stampOf(item: SharedItem): Instant {
        val deleted = item.deletedAt
        return if (deleted != null && deleted.ticks() > item.editedAt.ticks()) deleted else item.editedAt
    }

    @Suppress("UNCHECKED_CAST")
    private fun <T : SharedItem> clamped(item: T, limit: Instant): T {
        val deleted = item.deletedAt
        if (item.editedAt.ticks() <= limit.ticks() && (deleted == null || deleted.ticks() <= limit.ticks())) {
            return item
        }

        val edited = if (item.editedAt.ticks() > limit.ticks()) limit else item.editedAt
        val clampedDeleted = if (deleted != null && deleted.ticks() > limit.ticks()) limit else deleted
        return item.withStamps(edited, clampedDeleted) as T
    }

    // More than 0 when [candidate] is the version to keep over [held].
    private fun compare(candidate: SharedItem, held: SharedItem): Int {
        val byTime = stampOf(candidate).ticks().compareTo(stampOf(held).ticks())
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
}
