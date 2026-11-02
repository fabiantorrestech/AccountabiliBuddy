package com.accountabilibuddy.shared.sync

import java.time.Instant

/**
 * A task on the Later list (ADR 50), the shared list beside the next session's tasks (ADR 48): things the user keeps for
 * another day, with the steps, category, estimate and note they had and an optional due date. A deleted item travels as a
 * tombstone with no text. Only the sealed files carry it; the text is never logged. The phone edits the list, and an item the
 * user ticked off stays on it with [doneAt] (and its text) so every device shows it as done.
 *
 * @property subtasks Its steps in order (id, title, doneAt); null for none.
 * @property category The category's name; null for none.
 * @property categoryColor The category's colour, "#RRGGBB".
 * @property estimateMinutes The user's estimate in minutes; null for none.
 * @property note The user's private note; null for none. Sealed here at rest, never shown on the watch.
 * @property dueOn The local date it is due ("yyyy-MM-dd"); null for none.
 * @property plannedFor The day part it was put in the plan for; null for none.
 * @property doneAt When it was ticked off; null while it is open. An app that predates the field ignores it and passes the item on
 * without it; a newer edit there brings the item back as open. A ticked item ages out after 90 days.
 */
data class SyncLaterItem(
    override val id: String,
    val title: String,
    val subtasks: List<SessionTaskItem>?,
    val category: String?,
    val categoryColor: String?,
    val estimateMinutes: Int?,
    val note: String?,
    val dueOn: String?,
    override val editedAt: Instant,
    override val deletedAt: Instant?,
    override val position: Double,
    val plannedFor: TaskPlannedFor? = null,
    val doneAt: Instant? = null,
) : SharedItem {
    val isDone: Boolean get() = doneAt != null

    // The done time joins the tie-break only when there is one (as on the PC), so an item without it breaks ties exactly as before.
    override val tieBreak: String
        get() {
            val parts = listOf(
                title,
                subtasks.orEmpty().joinToString("\u0002") { step -> "${step.id?.lowercase() ?: ""}\u0003${step.title}\u0003${step.doneAt?.ticks() ?: ""}" },
                category ?: "",
                categoryColor ?: "",
                estimateMinutes?.toString() ?: "",
                note ?: "",
                dueOn ?: "",
                plannedFor?.tieText ?: "",
            )
            return (if (doneAt == null) parts else parts + doneAt.ticks().toString()).joinToString("\u0001")
        }

    override fun withStamps(editedAt: Instant, deletedAt: Instant?): SyncLaterItem = copy(editedAt = editedAt, deletedAt = deletedAt)

    /** The item as a tombstone: its text is gone, and it is deleted at [at]. */
    fun deleted(at: Instant): SyncLaterItem = copy(
        title = "", subtasks = null, category = null, categoryColor = null, estimateMinutes = null, note = null,
        dueOn = null, plannedFor = null, doneAt = null, editedAt = at, deletedAt = at,
    )
}
