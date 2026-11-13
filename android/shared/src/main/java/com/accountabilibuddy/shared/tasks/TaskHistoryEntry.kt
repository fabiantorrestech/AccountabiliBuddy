package com.accountabilibuddy.shared.tasks

import kotlinx.serialization.json.JsonObject
import java.time.Duration
import java.time.OffsetDateTime

/**
 * A task in History (ADR 76; the PC's TaskHistoryEntry): finished, archived or deleted, with the task as it was. Its id is the
 * task's, so a task has at most one entry. Separate from ADR 72's session records (`taskHistory`).
 *
 * @property at When it was finished, archived or deleted (the sync file's `when`).
 * @property task The task as it was then.
 * @property editedAt The entry's newest change; the newest version wins.
 * @property deletedAt Set on a tombstone: the entry was removed (brought back, done again or aged out).
 */
data class TaskHistoryEntry(
    val id: String,
    val kind: TaskHistoryKind,
    val at: OffsetDateTime,
    val task: TaskItem,
    val editedAt: OffsetDateTime,
    val deletedAt: OffsetDateTime? = null,
    val extra: JsonObject = TaskUnknown.NONE,
) {
    val isLive: Boolean get() = deletedAt == null

    /** The entry as a tombstone: the snapshot reduced to the task's own tombstone. */
    fun deleted(time: OffsetDateTime): TaskHistoryEntry = copy(task = task.deleted(time), editedAt = time, deletedAt = time)

    companion object {
        /** How long an entry is kept after [at]. */
        val KEPT: Duration = Duration.ofDays(90)
    }
}
