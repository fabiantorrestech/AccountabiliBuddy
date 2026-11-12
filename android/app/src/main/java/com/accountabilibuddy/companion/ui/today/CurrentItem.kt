package com.accountabilibuddy.companion.ui.today

import com.accountabilibuddy.companion.sync.RunningTasks
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.TaskStepLetters

/**
 * What a PC session is on now (CN §3): the open step the PC shows, else its task ([SessionTaskItem.shown], the PC's own rule).
 * Done on Today finishes this item: the step when it is a step, otherwise the task (never the task's open steps along with it),
 * the same as the PC's session card (Q352). [task] is the task it belongs to.
 */
data class CurrentItem(val item: SessionTaskItem, val task: SessionTaskItem) {
    val isStep: Boolean get() = item !== task

    /** The step's letter from its place among all of its task's steps ("b"), as the PC numbers them; null for a task. */
    val stepLetter: String?
        get() = if (isStep) task.subtasks.indexOfFirst { it === item }.takeIf { it >= 0 }?.let(TaskStepLetters::of) else null

    /** Done's label, as the PC's session card (Q352): "Done · b" for a step, "Done" for a task. */
    val doneLabel: String get() = stepLetter?.let { "Done · $it" } ?: "Done"

    companion object {
        fun of(running: RunningTasks?): CurrentItem? {
            val (task, step) = running?.let { SessionTaskItem.shown(it.tasks, it.currentId) } ?: return null
            return CurrentItem(step ?: task, task)
        }
    }
}
