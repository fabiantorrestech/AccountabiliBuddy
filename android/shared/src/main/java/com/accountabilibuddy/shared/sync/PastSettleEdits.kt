package com.accountabilibuddy.shared.sync

import java.time.Instant

/**
 * What a device does to a past task it doesn't own (ADR 72's settle records, AR1c), pure: every tick, untick, delete and restore is one stamped
 * settle record, never an edit of the owner's history. A record is stamped with [SharedListMerge.nextStamp] so it outranks the one it replaces,
 * which is what makes a Restore beat the delete it undoes on every device.
 */
object PastSettleEdits {
    /** The records with [id]'s record replaced by a new [state] stamped at [now] (or one tick past the old record's own newest time). */
    fun settle(records: List<SyncPastSettled>, id: String, state: String, now: Instant): List<SyncPastSettled> {
        val held = records.firstOrNull { it.id.equals(id, ignoreCase = true) }
        val record = SyncPastSettled(id, state, SharedListMerge.nextStamp(now, held))
        return records.filterNot { it.id.equals(id, ignoreCase = true) } + record
    }

    /**
     * Restore (Recently deleted): the task's state when it was deleted ([SyncPastSettled.DONE] or [SyncPastSettled.OPEN], what the builder's
     * `restoreAs` says), stamped over the delete. Anything else changes nothing; no record is ever removed.
     */
    fun restore(records: List<SyncPastSettled>, id: String, state: String, now: Instant): List<SyncPastSettled> =
        if (state != SyncPastSettled.DONE && state != SyncPastSettled.OPEN) records else settle(records, id, state, now)
}
