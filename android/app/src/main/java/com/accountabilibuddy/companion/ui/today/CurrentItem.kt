package com.accountabilibuddy.companion.ui.today

import com.accountabilibuddy.companion.sync.RunningTasks
import com.accountabilibuddy.shared.sync.SessionTaskItem

/**
 * What a PC session is on now (CN §3): the open step the PC shows, else its task ([SessionTaskItem.shown], the PC's own rule).
 * Done on Today finishes this item: the step when it is a step, otherwise the task. [task] is the task it belongs to.
 */
data class CurrentItem(val item: SessionTaskItem, val task: SessionTaskItem) {
    val isStep: Boolean get() = item !== task

    companion object {
        fun of(running: RunningTasks?): CurrentItem? {
            val (task, step) = running?.let { SessionTaskItem.shown(it.tasks, it.currentId) } ?: return null
            return CurrentItem(step ?: task, task)
        }
    }
}
