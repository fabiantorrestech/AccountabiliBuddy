package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.SharedListMerge
import java.time.OffsetDateTime

/**
 * Merges two tag-scope lists (ADR 89; the PC's TagScopeMerge) item by item with [SharedListMerge]: the newest version of each scope wins (a
 * tombstone newer than an edit makes the tag shared again), tombstones are kept 30 days, a time far ahead is clamped to 2 minutes ahead
 * (here, so the clamped time keeps the receiver's offset as on the PC). Either side null means none; nothing left is null, so a device that
 * never used scopes keeps writing none.
 */
object TagScopeMerge {
    fun merge(held: List<SyncTagScope>?, incoming: List<SyncTagScope>?, now: OffsetDateTime): List<SyncTagScope>? {
        val limit = now.plus(SharedListMerge.FUTURE_LIMIT)
        val merged = SharedListMerge.merge(held.orEmpty().map { clamped(it, limit) }, incoming.orEmpty().map { clamped(it, limit) }, now.toInstant())
        return merged.ifEmpty { null }
    }

    private fun clamped(item: SyncTagScope, limit: OffsetDateTime): SyncTagScope =
        item.copy(edited = DotNetTime.atMost(item.edited, limit), deleted = item.deleted?.let { DotNetTime.atMost(it, limit) })
}
