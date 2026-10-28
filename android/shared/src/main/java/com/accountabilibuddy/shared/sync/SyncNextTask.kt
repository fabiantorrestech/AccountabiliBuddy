package com.accountabilibuddy.shared.sync

import java.time.Instant

/**
 * A task on the next session's list, as the sealed sync files share it between the user's devices (ADR 48, amends
 * ADR 24): the task with its steps and its category's name and colour (a category's id is local to a device). A deleted
 * task travels as a tombstone with no text. Only the sealed files carry it; the text is never logged.
 *
 * @property doneAt When it was ticked; null while it is open.
 * @property category The category's name; null for none.
 * @property categoryColor The category's colour, "#RRGGBB".
 * @property subtasks Its steps in order (id, title, doneAt); null for none.
 * @property estimateMinutes The user's estimate in minutes (ADR 50); null for none.
 * @property note The user's private note (ADR 50); null for none. Sealed here at rest, never shown on the watch.
 * @property plannedFor The day part it was put in the plan for (ADR 50); null for none.
 */
data class SyncNextTask(
    override val id: String,
    val title: String,
    val doneAt: Instant?,
    val category: String?,
    val categoryColor: String?,
    val subtasks: List<SessionTaskItem>?,
    override val editedAt: Instant,
    override val deletedAt: Instant?,
    override val position: Double,
    val estimateMinutes: Int? = null,
    val note: String? = null,
    val plannedFor: TaskPlannedFor? = null,
) : SharedItem {
    val isDone: Boolean get() = doneAt != null

    override val tieBreak: String
        get() = listOf(
            title,
            doneAt?.ticks()?.toString() ?: "",
            category ?: "",
            categoryColor ?: "",
            subtasks.orEmpty().joinToString("\u0002") { step -> "${step.id?.lowercase() ?: ""}\u0003${step.title}\u0003${step.doneAt?.ticks() ?: ""}" },
            estimateMinutes?.toString() ?: "",
            note ?: "",
            plannedFor?.tieText ?: "",
        ).joinToString("\u0001")

    override fun withStamps(editedAt: Instant, deletedAt: Instant?): SyncNextTask = copy(editedAt = editedAt, deletedAt = deletedAt)

    /** The task as a tombstone: its text is gone, and it is deleted at [at]. */
    fun deleted(at: Instant): SyncNextTask =
        copy(
            title = "", doneAt = null, category = null, categoryColor = null, subtasks = null,
            estimateMinutes = null, note = null, plannedFor = null, editedAt = at, deletedAt = at,
        )
}
