package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.SharedItem
import kotlinx.serialization.json.JsonObject
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneOffset

/**
 * A task of the one shared task list (ADR 76) as the sealed sync files carry it (the PC's SyncTaskItem): the task and its per-field
 * stamps. Merged by [TaskFieldMerge] (field by field), which leaves tombstones and positions to the shared lists' merge. A deleted
 * task travels as a tombstone with no text; a task kept on one device is written by every other device as a stub. Only the sealed
 * files carry it; the text is never logged.
 */
data class SyncTaskItem(val task: TaskItem, val stamps: TaskFieldStamps, val extra: JsonObject = TaskUnknown.NONE) : SharedItem {
    override val id: String get() = task.id

    override val editedAt: Instant get() = task.editedAt.toInstant()

    override val deletedAt: Instant? get() = task.deletedAt?.toInstant()

    override val position: Double get() = task.position

    override val localTo: String? get() = task.localTo

    /** The PC's tie-break: the item as the PC's JSON text (known fields only). Never logged or shown. */
    override val tieBreak: String get() = TaskJson.text(TaskJson.writeSyncTask(this, unknown = false))

    // [TaskFieldMerge] clamps with the receiver's offset before the shared merge runs, so this only ever sees times it keeps.
    override fun withStamps(editedAt: Instant, deletedAt: Instant?): SyncTaskItem =
        copy(task = task.copy(editedAt = keep(task.editedAt, editedAt)!!, deletedAt = keep(task.deletedAt, deletedAt)))

    override fun asLocalStub(): SyncTaskItem = deleted(task.editedAt)

    /** The task as a tombstone deleted at [at] (it keeps its bin and devices, no text, no stamps). */
    fun deleted(at: OffsetDateTime): SyncTaskItem = SyncTaskItem(task.deleted(at), TaskFieldStamps.NONE)

    companion object {
        /** [held] when it is the moment [wanted], so its offset stays; else [wanted] in UTC. */
        internal fun keep(held: OffsetDateTime?, wanted: Instant?): OffsetDateTime? = when {
            wanted == null -> null
            held != null && DotNetTime.same(held, wanted.atOffset(ZoneOffset.UTC)) -> held
            else -> wanted.atOffset(ZoneOffset.UTC)
        }
    }
}
