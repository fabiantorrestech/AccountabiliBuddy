package com.accountabilibuddy.shared.sync

import java.time.Instant

/**
 * A come-back (ADR 30, 48), the user's parked thought, as the sealed sync files share it between the user's devices. A
 * ticked one is kept with when it was ticked; a deleted one travels as a tombstone with no text. Only the sealed files
 * carry it; the text is never logged.
 *
 * @property doneAt When it was ticked; null while it is open.
 * @property category The category's name; null for none. Optional in the file: an app that predates it neither reads nor writes it.
 * @property categoryColor The category's colour, "#RRGGBB".
 */
data class SyncParked(
    override val id: String,
    val text: String,
    val createdAt: Instant,
    val doneAt: Instant?,
    override val editedAt: Instant,
    override val deletedAt: Instant?,
    override val position: Double,
    val category: String? = null,
    val categoryColor: String? = null,
) : SharedItem {
    // The category joins the tie-break only when there is one (as on the PC): a come-back without it breaks ties exactly as before,
    // and a copy that kept its category beats the same come-back passed on by an app that dropped it.
    override val tieBreak: String
        get() {
            val base = listOf(text, createdAt.ticks().toString(), doneAt?.ticks()?.toString() ?: "")
            val parts = if (category == null && categoryColor == null) base else base + listOf(category ?: "", categoryColor ?: "")
            return parts.joinToString("\u0001")
        }

    override fun withStamps(editedAt: Instant, deletedAt: Instant?): SyncParked = copy(editedAt = editedAt, deletedAt = deletedAt)

    /** The come-back as a tombstone: its text is gone, and it is deleted at [at]. */
    fun deleted(at: Instant): SyncParked = copy(text = "", doneAt = null, category = null, categoryColor = null, editedAt = at, deletedAt = at)
}
