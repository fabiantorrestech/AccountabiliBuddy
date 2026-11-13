package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.SharedItem
import kotlinx.serialization.json.JsonObject
import java.time.Instant

/**
 * A History entry (ADR 76: finished, archived or deleted, with the task's snapshot) as the sealed sync files carry it in `taskLog`
 * (the PC's SyncTaskLogItem), merged item by item ([TaskListMerge]). Not ADR 72's session records. The snapshot's text is sealed,
 * never logged.
 */
data class SyncTaskLogItem(val entry: TaskHistoryEntry, val extra: JsonObject = TaskUnknown.NONE) : SharedItem {
    override val id: String get() = entry.id

    override val editedAt: Instant get() = entry.editedAt.toInstant()

    override val deletedAt: Instant? get() = entry.deletedAt?.toInstant()

    /** Newest first is the UI's order; the list itself goes by when it happened (Unix milliseconds, as on the PC). */
    override val position: Double get() = (Math.floorDiv(DotNetTime.ticks(entry.at), TICKS_PER_MILLISECOND) - UNIX_EPOCH_MILLISECONDS).toDouble()

    override val tieBreak: String get() = TaskJson.text(TaskJson.writeSyncLogItem(this, unknown = false))

    override fun withStamps(editedAt: Instant, deletedAt: Instant?): SyncTaskLogItem =
        copy(entry = entry.copy(editedAt = SyncTaskItem.keep(entry.editedAt, editedAt)!!, deletedAt = SyncTaskItem.keep(entry.deletedAt, deletedAt)))

    private companion object {
        const val TICKS_PER_MILLISECOND = 10_000L

        // 0001-01-01 to 1970-01-01 in milliseconds (.NET's ToUnixTimeMilliseconds).
        const val UNIX_EPOCH_MILLISECONDS = 62_135_596_800_000L
    }
}
