package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.SharedListMerge
import java.time.OffsetDateTime

/**
 * Merges two tag-scope lists (ADR 89; the PC's TagScopeMerge) item by item with [SharedListMerge]: the newest version of each scope wins (a
 * tombstone newer than an edit makes the tag shared again), tombstones are kept 30 days, a time far ahead is clamped to 2 minutes ahead
 * (here, so the clamped time keeps the receiver's offset as on the PC). Either side null means none; nothing left is null, so a device that
 * never used scopes keeps writing none. At most [MAX_SCOPES] entries are kept (a file's size): past it the oldest tombstones go first, then
 * the oldest live scopes.
 */
object TagScopeMerge {
    /** The most scopes (live and tombstones) a device keeps and sends. */
    const val MAX_SCOPES = 2000

    fun merge(held: List<SyncTagScope>?, incoming: List<SyncTagScope>?, now: OffsetDateTime): List<SyncTagScope>? {
        val limit = now.plus(SharedListMerge.FUTURE_LIMIT)
        val merged = SharedListMerge.merge(held.orEmpty().map { clamped(it, limit) }, incoming.orEmpty().map { clamped(it, limit) }, now.toInstant())
        return if (merged.isEmpty()) null else capped(merged)
    }

    // Past MAX_SCOPES: the oldest tombstones go first, then the oldest live scopes; what stays keeps its (id) order.
    private fun capped(merged: List<SyncTagScope>): List<SyncTagScope> {
        if (merged.size <= MAX_SCOPES) return merged
        val drop = merged
            .sortedWith(
                compareBy<SyncTagScope> { if (it.deleted == null) 1 else 0 }
                    .thenBy { DotNetTime.ticks(it.deleted ?: it.edited) }
                    .thenBy { it.id.lowercase() },
            )
            .take(merged.size - MAX_SCOPES)
            .toSet()
        return merged.filter { it !in drop }
    }

    private fun clamped(item: SyncTagScope, limit: OffsetDateTime): SyncTagScope =
        item.copy(edited = DotNetTime.atMost(item.edited, limit), deleted = item.deleted?.let { DotNetTime.atMost(it, limit) })
}
