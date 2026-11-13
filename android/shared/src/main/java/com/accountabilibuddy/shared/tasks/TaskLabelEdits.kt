package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import java.time.OffsetDateTime

/**
 * The phone's label edits (ADR 76; the PC's TaskLabelEdits and TaskCategoryEdits, the same rules), behind its old category screens
 * until R7.3/R7.4: add, recolour, rename and delete. A name another live label has (any case) is refused. Deleting a label leaves its
 * tasks with No label (a stamped edit of each) and keeps its tombstone for good ([TaskLabelMerge]); a label added again under a
 * deleted one's id comes back ringing on every device, stamped at the revive.
 */
object TaskLabelEdits {
    /** The live label named [name] (any case, trimmed). */
    fun named(lists: TaskLists, name: String): TaskLabel? = lists.labels.map { it.label }.firstOrNull { it.isLive && TaskLabel.sameName(it.name, name) }

    /** A new label (or the live one of that name, unchanged); [color] when valid, else the first unused colour. */
    fun add(lists: TaskLists, name: String, color: String?, now: OffsetDateTime, self: String): TaskLists {
        val trimmed = name.trim()
        if (trimmed.isEmpty() || named(lists, trimmed) != null) {
            return lists
        }

        val labels = lists.labels.map { it.label }.toMutableList()
        val id = TaskLabelBook.freeIdFor(trimmed, labels)
        val chosen = if (TaskLabelColors.isValid(color)) color!!.trim() else TaskLabelColors.firstUnused(labels)
        val position = (labels.filter { it.isLive }.maxOfOrNull { it.position } ?: 0.0) + 1
        val existing = labels.indexOfFirst { it.id == id }
        val stamp = if (existing >= 0) after(now, labels[existing].deletedAt ?: labels[existing].editedAt) else now
        val label = TaskLabel(id, trimmed, chosen, position, stamp)
        if (existing >= 0) {
            labels[existing] = label.copy(ringOnStamp = FieldStamp(stamp, self.lowercase()))
        } else {
            labels += label
        }

        return with(lists, labels)
    }

    /** A label's new colour (an invalid one changes nothing). */
    fun recolour(lists: TaskLists, name: String, color: String, now: OffsetDateTime): TaskLists {
        if (!TaskLabelColors.isValid(color)) return lists
        val wanted = color.trim()
        return changed(lists, name) { label -> if (label.color.equals(wanted, ignoreCase = true)) null else label.copy(color = wanted, editedAt = after(now, label.editedAt)) }
    }

    /** A label's new name; refused (unchanged) when blank or another live label has it. Its tasks keep it (they hold its id). */
    fun rename(lists: TaskLists, oldName: String, newName: String, now: OffsetDateTime): TaskLists {
        val trimmed = newName.trim()
        val other = named(lists, trimmed)
        if (trimmed.isEmpty() || (other != null && !TaskLabel.sameName(other.name, oldName))) return lists
        return changed(lists, oldName) { label -> if (label.name == trimmed) null else label.copy(name = trimmed, editedAt = after(now, label.editedAt)) }
    }

    /** Deletes the label named [name]: its tasks are left with No label on every device. */
    fun delete(lists: TaskLists, name: String, now: OffsetDateTime, self: String): TaskLists {
        val label = named(lists, name) ?: return lists
        val labels = lists.labels.map { item -> if (item.label.id == label.id) SyncTaskLabel(label.deleted(after(now, label.editedAt))) else item }
        val tasks = lists.tasks.map { item ->
            if (item.task.isLive && item.task.labelId == label.id) TaskEdits.edit(item, TaskField.LABEL, item.task.copy(labelId = null), now, self) else item
        }
        return lists.copy(tasks = tasks, labels = labels)
    }

    private fun changed(lists: TaskLists, name: String, change: (TaskLabel) -> TaskLabel?): TaskLists {
        val label = named(lists, name) ?: return lists
        val updated = change(label) ?: return lists
        return lists.copy(labels = lists.labels.map { if (it.label.id == label.id) it.copy(label = updated) else it })
    }

    private fun with(lists: TaskLists, labels: List<TaskLabel>): TaskLists {
        val byId = lists.labels.associateBy { it.label.id }
        return lists.copy(labels = labels.map { label -> byId[label.id]?.takeIf { it.label == label } ?: SyncTaskLabel(label) })
    }

    private fun after(now: OffsetDateTime, held: OffsetDateTime): OffsetDateTime = if (DotNetTime.compare(held, now) >= 0) DotNetTime.plusTick(held) else now
}
