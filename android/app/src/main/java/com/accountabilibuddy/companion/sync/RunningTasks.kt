package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SessionTaskItem
import java.time.Duration
import java.time.Instant

/**
 * A running session's checklist the phone knows (ADR 24), which device it's from, and the task or subtask picked there
 * to show ([currentId]; null: the first open task).
 */
data class RunningTasks(val source: String, val tasks: List<SessionTaskItem>, val currentId: String? = null) {
    val doneCount: Int get() = tasks.count { it.isDone }

    /** The item marked with the pin icon: the shown subtask, else the shown task (as the PC's bar shows it). */
    val markedId: String? get() = SessionTaskItem.shown(tasks, currentId)?.let { (task, subtask) -> subtask?.id ?: task.id }

    companion object {
        // A file older than this doesn't say the session still runs (it may have ended since).
        private val FRESH: Duration = Duration.ofHours(12)

        /** The newest sync file (written in the last 12 hours) whose running session has tasks, e.g. from the work computer through Drive. */
        fun of(received: List<ReceivedDevice>, now: Instant): RunningTasks? =
            received
                .filter { Duration.between(it.file.device.writtenAt, now) <= FRESH }
                .mapNotNull { device ->
                    // The task picked on the PC comes with its running session (ADR 42).
                    device.file.sessions.lastOrNull { it.end == null && it.tasks.isNotEmpty() }
                        ?.let { device.file.device.writtenAt to RunningTasks(device.file.device.name, it.tasks, device.file.running?.currentTask) }
                }
                .maxByOrNull { it.first }
                ?.second
    }
}
