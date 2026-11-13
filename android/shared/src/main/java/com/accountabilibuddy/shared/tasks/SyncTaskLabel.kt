package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.SharedItem
import kotlinx.serialization.json.JsonObject
import java.time.Instant

/**
 * A label (ADR 76) as the sealed sync files carry it (the PC's SyncTaskLabel), merged by [TaskLabelMerge]: item by item, then
 * "Rings on" by its own stamp. The name is the user's text: sealed, never logged.
 */
data class SyncTaskLabel(val label: TaskLabel, val extra: JsonObject = TaskUnknown.NONE) : SharedItem {
    override val id: String get() = label.id

    override val editedAt: Instant get() = label.editedAt.toInstant()

    override val deletedAt: Instant? get() = label.deletedAt?.toInstant()

    override val position: Double get() = label.position

    override val tieBreak: String get() = TaskJson.text(TaskJson.writeSyncLabel(this, unknown = false))

    override fun withStamps(editedAt: Instant, deletedAt: Instant?): SyncTaskLabel =
        copy(label = label.copy(editedAt = SyncTaskItem.keep(label.editedAt, editedAt)!!, deletedAt = SyncTaskItem.keep(label.deletedAt, deletedAt)))
}
