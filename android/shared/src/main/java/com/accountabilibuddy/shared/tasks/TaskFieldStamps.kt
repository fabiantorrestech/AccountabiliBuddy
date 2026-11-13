package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import kotlinx.serialization.json.JsonObject
import java.time.OffsetDateTime

/**
 * A task's per-part stamps (ADR 76; the PC's TaskFieldStamps), one per [TaskField]. A missing stamp counts as made by nobody when
 * the task was created, older than any edit.
 *
 * @property extra Stamps of parts a newer app knows and this version doesn't, written back unchanged.
 */
data class TaskFieldStamps(private val stamps: Map<TaskField, FieldStamp> = emptyMap(), val extra: JsonObject = TaskUnknown.NONE) {
    fun of(field: TaskField): FieldStamp? = stamps[field]

    fun with(field: TaskField, stamp: FieldStamp?): TaskFieldStamps =
        copy(stamps = if (stamp == null) stamps - field else stamps + (field to stamp))

    /** The newest of the stamps (the first of them in the fields' order when several share it); null when there are none. */
    val newest: OffsetDateTime?
        get() {
            var newest: OffsetDateTime? = null
            for (field in TaskField.entries) {
                val at = stamps[field]?.at ?: continue
                if (newest == null || DotNetTime.compare(at, newest) > 0) {
                    newest = at
                }
            }

            return newest
        }

    companion object {
        val NONE = TaskFieldStamps()

        /** Every field stamped the same (a new task, or one the migration made). */
        fun allAt(stamp: FieldStamp): TaskFieldStamps = TaskFieldStamps(TaskField.entries.associateWith { stamp })
    }
}
