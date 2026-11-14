package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.SharedItem
import java.time.Instant
import java.time.OffsetDateTime

/**
 * Where a tag is shown (ADR 89; the PC's `SyncTagScope`) as the sealed sync files carry it ([TaskParts.tagScopes]), merged item by item by
 * [com.accountabilibuddy.shared.sync.SharedListMerge] ([TagScopeMerge]): the newest version wins, tombstones are kept 30 days. A tag has a
 * scope only while it is device-only; a tag with no live scope is shared. The id is [TaskScopes.tagIdFor] the name. The name is the user's
 * text: sealed, never logged.
 *
 * @property name The tag as first written; null on a tombstone.
 * @property onlyOn The devices that show the tag (device ids, lower case, in id order); null on a tombstone.
 */
data class SyncTagScope(
    override val id: String,
    val name: String?,
    val onlyOn: List<String>?,
    val edited: OffsetDateTime,
    val deleted: OffsetDateTime? = null,
) : SharedItem {
    override val editedAt: Instant get() = edited.toInstant()

    override val deletedAt: Instant? get() = deleted?.toInstant()

    /** Scopes sit in id order. */
    override val position: Double get() = 0.0

    override val tieBreak: String get() = listOf(name ?: "", TaskRingDevices.text(onlyOn)).joinToString("\u0001")

    override fun withStamps(editedAt: Instant, deletedAt: Instant?): SyncTagScope =
        copy(edited = SyncTaskItem.keep(edited, editedAt)!!, deleted = SyncTaskItem.keep(deleted, deletedAt))

    /** The scope as a tombstone: the tag is shared again; no name or devices left. */
    fun deleted(at: OffsetDateTime): SyncTagScope = copy(name = null, onlyOn = null, edited = at, deleted = at)
}
