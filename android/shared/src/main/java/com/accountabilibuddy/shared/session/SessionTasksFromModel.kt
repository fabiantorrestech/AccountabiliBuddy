package com.accountabilibuddy.shared.session

import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.TaskLabels
import com.accountabilibuddy.shared.tasks.ShelfSession
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskLabel
import com.accountabilibuddy.shared.tasks.TaskLabelColors
import com.accountabilibuddy.shared.tasks.TaskLists

/**
 * The phone session's "This session" list as the rules need it (PS3a, plan check B6, PS2a Q1171): the shared task model's This session tasks, read as the
 * [SessionTaskItem]s the checklist ([PhoneSessionChecklist]) keeps. Pure.
 *
 * While a session runs the list is not just `bin == SESSION`: a task ticked on Today moves to the Done bin (`prevBin` = where it was), so the list is every
 * task that was part of the session (an id the checklist already holds, or a task now in This session) and is still in This session or finished. A task that
 * went to Queued or any other bin, or was deleted, is not in the list any more: if it was open the checklist counts it as dropped, so taking tasks off This
 * session never ends a session at once (the only way is to tick them).
 */
object SessionTasksFromModel {
    /** The tasks a session starts with: This session's open tasks, in their order. */
    fun atStart(lists: TaskLists): List<SessionTaskItem> {
        val labels = lists.labels.map { it.label }
        return ShelfSession.open(lists).map { item(it, labels) }
    }

    /** This session's list now, for the checklist [checklist] of the running session (see the class text). */
    fun whileRunning(lists: TaskLists, checklist: PhoneSessionChecklist): List<SessionTaskItem> {
        val labels = lists.labels.map { it.label }
        val known = checklist.tasks.mapNotNull { it.id?.lowercase() }.toSet()
        val live = lists.tasks.map { it.task }.filter { it.isLive }
        val open = live.filter { it.bin == TaskBin.SESSION && !it.isDone }.sortedBy { it.position }
        val finished = live.filter { it.bin == TaskBin.DONE && it.isDone && it.id.lowercase() in known }.sortedBy { it.doneAt }
        // Open tasks in the model's own order (a reorder on the Tasks page shows, an Undo puts a task back where it was); finished ones as they were ticked.
        return (open + finished).map { item(it, labels) }
    }

    /** What a tick of [id] on the session's list means: the task, or one of its steps. Null when [id] is on neither (the list changed under the tap). */
    fun tickOf(list: List<SessionTaskItem>, id: String?): Tick? {
        if (id == null) return null
        list.firstOrNull { it.id == id }?.let { return Tick.OfTask(it.id!!) }
        list.forEach { task -> task.subtasks.firstOrNull { it.id == id }?.let { return Tick.OfStep(task.id ?: return null, id) } }
        return null
    }

    sealed interface Tick {
        data class OfTask(val taskId: String) : Tick
        data class OfStep(val taskId: String, val stepId: String) : Tick
    }

    private fun item(task: TaskItem, labels: List<TaskLabel>): SessionTaskItem {
        val label = task.labelId?.let { id -> labels.firstOrNull { it.id == id && it.isLive } }
        return SessionTaskItem(
            id = task.id.lowercase(),
            title = task.title,
            doneAt = task.doneAt?.toInstant(),
            subtasks = task.steps.orEmpty().map { SessionTaskItem(it.id.lowercase(), it.title, it.doneAt?.toInstant(), emptyList()) },
            categoryId = label?.id,
            category = label?.name,
            categoryColor = label?.let { TaskLabelColors.opaque(it.color) },
            labels = TaskLabels.clean(task.tags).orEmpty(),
            localTo = task.localTo,
        )
    }
}
