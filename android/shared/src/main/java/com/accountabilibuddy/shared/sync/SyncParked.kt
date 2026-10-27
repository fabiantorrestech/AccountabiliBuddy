package com.accountabilibuddy.shared.sync

import java.time.Instant

/**
 * A come-back (ADR 30, 48), the user's parked thought, as the sealed sync files share it between the user's devices. A
 * ticked one is kept with when it was ticked; a deleted one travels as a tombstone with no text. Only the sealed files
 * carry it; the text is never logged.
 *
 * @property doneAt When it was ticked; null while it is open.
 */
data class SyncParked(
    override val id: String,
    val text: String,
    val createdAt: Instant,
    val doneAt: Instant?,
    override val editedAt: Instant,
    override val deletedAt: Instant?,
    override val position: Double,
) : SharedItem {
    override val tieBreak: String
        get() = listOf(text, createdAt.ticks().toString(), doneAt?.ticks()?.toString() ?: "").joinToString("\u0001")

    /** The come-back as a tombstone: its text is gone, and it is deleted at [at]. */
    fun deleted(at: Instant): SyncParked = copy(text = "", doneAt = null, editedAt = at, deletedAt = at)
}
