package com.accountabilibuddy.companion.sync

import java.util.UUID

/**
 * What the phone can ask a PC to change in its running session's checklist (ADR 48): the sender side of the sync
 * actions the PC applies under its own rules (three tasks, no loosening, late actions refused; the answer comes back in
 * the PC's file). Ticking and parking are not here: [ActionStore] already sends those.
 *
 * Until the actions are wired in, [isAvailable] is false and the checklist shows no edit controls.
 */
interface RunningTaskEdits {
    /** False when this build has no way to send the edits yet. */
    val isAvailable: Boolean

    /** Asks the PC [pcId] to add a task to its running session, in the PC's category [categoryId] (null: none). */
    fun addTask(pcId: UUID, title: String, categoryId: String?)

    /** Asks the PC to change a task's title and/or its steps' titles in order (null leaves that part). */
    fun editTask(pcId: UUID, taskId: String, title: String?, steps: List<String>?)

    /** Asks the PC to put the tasks in this order (their ids). */
    fun reorderTasks(pcId: UUID, orderedIds: List<String>)
}
