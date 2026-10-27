package com.accountabilibuddy.companion.sync

import java.util.UUID

/**
 * [RunningTaskEdits] over [PcFileRemote]'s task actions (ADR 48): each change goes in this phone's sync file for the PC,
 * which applies it under its own rules and answers in its file. A change that can't be sent (no such PC, a PC too old,
 * nothing typed) is shown through [PcFileRemote.news] like a PC's refusal.
 */
class PcFileRunningTaskEdits(private val remote: PcFileRemote) : RunningTaskEdits {
    override val isAvailable: Boolean = true

    override fun addTask(pcId: UUID, title: String, categoryId: String?) {
        remote.addTask(pcId, title, categoryId = categoryId)?.let(remote::tell)
    }

    override fun editTask(pcId: UUID, taskId: String, title: String?, steps: List<PcFileRemote.TaskStep>?) {
        remote.editTask(pcId, taskId, title, steps)?.let(remote::tell)
    }

    override fun reorderTasks(pcId: UUID, orderedIds: List<String>) {
        remote.reorderTasks(pcId, orderedIds)?.let(remote::tell)
    }
}
