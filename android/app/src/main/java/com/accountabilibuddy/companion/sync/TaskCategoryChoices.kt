package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SyncNextTask

/**
 * The task categories the phone can offer. The PC does not send its category list, so these are the categories the phone
 * has seen on tasks: the next session's tasks and the checklists in the PCs' files, by name (a category's id is local to
 * a device, ADR 48). A receiving PC creates a category it lacks, with the name and colour the task carries.
 */
object TaskCategoryChoices {
    /** One category: its name, its colour ("#RRGGBB", null when unknown) and, on a running checklist, the PC's id for it. */
    data class Choice(val name: String, val color: String?, val id: String? = null)

    /** The categories of the next session's tasks, of every PC's checklists in [received], one per name (first colour seen wins), by name. */
    fun forNextTasks(nextTasks: List<SyncNextTask>, received: List<ReceivedDevice>): List<Choice> {
        val seen = nextTasks.mapNotNull { task -> task.category?.let { Choice(it, task.categoryColor) } } +
            received.flatMap { device -> device.file.sessions.flatMap { session -> forChecklist(session.tasks) } }
        return seen.distinctBy { it.name.lowercase() }.sortedBy { it.name.lowercase() }
    }

    /** The categories on a running checklist's tasks, with the PC's ids, in the order they first appear. */
    fun forChecklist(tasks: List<SessionTaskItem>): List<Choice> =
        tasks.mapNotNull { task -> task.category?.let { Choice(it, task.categoryColor, task.categoryId) } }.distinctBy { it.name.lowercase() }
}
