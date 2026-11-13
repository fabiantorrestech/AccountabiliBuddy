package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.FieldStamps
import java.time.OffsetDateTime

/**
 * Stamps a local change to a task (ADR 76; the PC's TaskEdits) the way [TaskFieldMerge] expects: the changed field gets a new
 * stamp (now, or just past the task's newest stamp), a title or note remembers the value it was edited from, and the task's edit
 * and touch times move on. The phone's task store uses it for every change; nothing else writes stamps.
 */
object TaskEdits {
    /** The task with [field] taken from [changed] and stamped; the same item when the value didn't change. */
    fun edit(item: SyncTaskItem, field: TaskField, changed: TaskItem, now: OffsetDateTime, by: String): SyncTaskItem {
        val task = item.task
        if (TaskFieldValues.canonical(task, field) == TaskFieldValues.canonical(changed, field)) {
            return item
        }

        var bases: List<String>? = null
        if (field == TaskField.TITLE || field == TaskField.NOTE) {
            bases = withBase(item.stamps.of(field)?.bases, TaskFieldValues.hash(TaskFieldValues.text(task, field)))
        }

        return stamped(item, field, TaskFieldValues.with(task, changed, field), bases, now, by)
    }

    /**
     * The user's pick in "Changed on both devices": [kept] becomes the value, stamped as made from both versions, so the conflict
     * closes on every device once synced.
     */
    fun resolve(item: SyncTaskItem, conflict: TaskConflict, kept: String?, now: OffsetDateTime, by: String): SyncTaskItem {
        val field = if (conflict.field == TaskConflict.TITLE) TaskField.TITLE else TaskField.NOTE
        val task = item.task
        var bases = withBase(item.stamps.of(field)?.bases, TaskFieldValues.hash(TaskFieldValues.text(task, field)))
        bases = withBase(bases, TaskFieldValues.hash(conflict.otherValue))

        val open = task.conflicts.orEmpty().filter { !it.sameAs(conflict) }
        val changed = (if (field == TaskField.TITLE) task.copy(title = kept ?: "") else task.copy(note = kept)).copy(conflicts = open.ifEmpty { null })
        return stamped(item, field, changed, bases, now, by)
    }

    private fun stamped(item: SyncTaskItem, field: TaskField, changed: TaskItem, bases: List<String>?, now: OffsetDateTime, by: String): SyncTaskItem {
        // Now; just past the item's own newest time (the shared lists' next stamp); just past its newest field stamp; and, for a field
        // never stamped, just past the task's creation: the edit must be newer than all of them.
        val deleted = item.task.deletedAt
        val itemStamp = if (deleted != null && DotNetTime.compare(deleted, item.task.editedAt) > 0) deleted else item.task.editedAt
        // R7.1 review: the time cut to the PC's ticks first, as every stamp is.
        val at = FieldStamps.next(DotNetTime.cut(now), itemStamp, item.stamps.newest, item.stamps.of(field)?.at ?: item.task.createdAt)
        val task = changed.copy(editedAt = at, touchedAt = if (DotNetTime.compare(at, changed.touchedAt) > 0) at else changed.touchedAt)
        return item.copy(task = task, stamps = item.stamps.with(field, FieldStamp(at, by.lowercase(), bases)))
    }

    private fun withBase(bases: List<String>?, hash: String): List<String> =
        (bases.orEmpty().filter { it != hash } + hash).takeLast(TaskFieldMerge.MAX_BASES)
}
