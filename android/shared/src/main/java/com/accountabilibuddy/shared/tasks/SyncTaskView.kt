package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.SharedItem
import kotlinx.serialization.json.JsonObject
import java.time.Instant

/**
 * A saved view (ADR 76) as the sealed sync files carry it (the PC's SyncTaskView), merged item by item ([TaskListMerge]). Never
 * carries search words.
 */
data class SyncTaskView(val view: TaskView, val extra: JsonObject = TaskUnknown.NONE) : SharedItem {
    override val id: String get() = view.id

    override val editedAt: Instant get() = view.editedAt.toInstant()

    override val deletedAt: Instant? get() = view.deletedAt?.toInstant()

    override val position: Double get() = view.position

    override val tieBreak: String get() = TaskJson.text(TaskJson.writeSyncView(this, unknown = false))

    override fun withStamps(editedAt: Instant, deletedAt: Instant?): SyncTaskView =
        copy(view = view.copy(editedAt = SyncTaskItem.keep(view.editedAt, editedAt)!!, deletedAt = SyncTaskItem.keep(view.deletedAt, deletedAt)))
}
