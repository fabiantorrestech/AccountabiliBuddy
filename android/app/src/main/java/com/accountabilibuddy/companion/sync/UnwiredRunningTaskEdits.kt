package com.accountabilibuddy.companion.sync

import java.util.UUID

/** Stands in for [RunningTaskEdits] until the PC's task actions are wired in: nothing can be sent. */
object UnwiredRunningTaskEdits : RunningTaskEdits {
    override val isAvailable: Boolean = false

    override fun addTask(pcId: UUID, title: String, categoryId: String?) = Unit

    override fun editTask(pcId: UUID, taskId: String, title: String?, steps: List<String>?) = Unit

    override fun reorderTasks(pcId: UUID, orderedIds: List<String>) = Unit
}
