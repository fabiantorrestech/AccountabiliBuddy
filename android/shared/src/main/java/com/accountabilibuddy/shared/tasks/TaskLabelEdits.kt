package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import java.time.OffsetDateTime

/**
 * The phone's label edits (ADR 76; the PC's TaskLabelEdits and TaskCategoryEdits, the same rules), behind its old category screens
 * until R7.3/R7.4: add, recolour, rename and delete. A name another live label has (any case) is refused. Deleting a label leaves its
 * tasks with No label (a stamped edit of each) and keeps its tombstone for good ([TaskLabelMerge]); a label added again under a
 * deleted one's id comes back ringing on every device, stamped at the revive. ADR 89: a label can be device-only ([setOnlyOn], [add]): a name
 * only on other devices doesn't make the name taken, and adding it device-only joins that label's list.
 */
object TaskLabelEdits {
    /** The live label named [name] (any case, trimmed). */
    fun named(lists: TaskLists, name: String): TaskLabel? = lists.labels.map { it.label }.firstOrNull { it.isLive && TaskLabel.sameName(it.name, name) }

    /**
     * Whether [name] can be the name of the label [labelId] (null: a new one): blank or another live label's name can't. A label this device
     * can't see (device-only on other devices, ADR 89) doesn't make a NEW label's name taken ([add] joins it); a rename still sees it (no two
     * live labels share a name). With no [self] every label is seen.
     */
    fun checkName(lists: TaskLists, labelId: String?, name: String, self: String = FieldStamp.NOBODY, removed: Collection<String>? = null): TaskLabelNameCheck {
        val trimmed = name.trim()
        if (trimmed.isEmpty()) return TaskLabelNameCheck.Blank
        val taken = lists.labels.map { it.label }.any {
            it.isLive && !it.id.equals(labelId, ignoreCase = true) && TaskLabel.sameName(it.name, trimmed) && (labelId != null || TaskScopes.labelVisible(it, self, removed))
        }
        return if (taken) TaskLabelNameCheck.Taken else TaskLabelNameCheck.Fine
    }

    /**
     * The devices that show the label (null: every device), stamped on its own ([TaskLabel.onlyOnStamp]: now, or just after a stamp ahead)
     * by [self]; unchanged when it already is shown on those (the PC's SetOnlyOn). "Make shared" is null; "only on this device" is a list of
     * [self], or [joined] when other devices hold it too. A choice is display only, so it applies at once, in a session too.
     */
    fun setOnlyOn(lists: TaskLists, labelId: String, onlyOn: List<String>?, now: OffsetDateTime, self: String): TaskLists {
        val wanted = TaskScopes.normalized(onlyOn)
        val label = lists.labels.map { it.label }.firstOrNull { it.isLive && it.id.equals(labelId, ignoreCase = true) } ?: return lists
        if (TaskRingDevices.same(TaskScopes.normalized(label.onlyOn), wanted)) return lists
        val changed = withOnlyOn(label, wanted, now, self)
        return lists.copy(labels = lists.labels.map { if (it.label.id == label.id) it.copy(label = changed) else it })
    }

    /** The label with [devices] as its list, stamped on its own (the whole label's editedAt stays: a join nobody sees can't revert a rename). */
    fun withOnlyOn(label: TaskLabel, devices: List<String>?, now: OffsetDateTime, self: String): TaskLabel {
        val at = label.onlyOnStamp?.let { after(now, it.at) } ?: now
        return label.copy(onlyOn = TaskScopes.normalized(devices), onlyOnStamp = FieldStamp(at, self.lowercase()))
    }

    /** A device list with [self] added (a list of just [self] for a shared label). */
    fun joined(onlyOn: List<String>?, self: String): List<String> = TaskScopes.normalized(onlyOn.orEmpty() + self.lowercase())!!

    /** How many live, unfinished tasks wear the label [labelId]. */
    fun useCount(lists: TaskLists, labelId: String): Int = lists.tasks.count { it.task.isLive && !it.task.isDone && it.task.labelId.equals(labelId, ignoreCase = true) }

    /**
     * The phones the label's reminders ring on (null: every device), stamped on its own ([TaskLabel.ringOnStamp]: now, or just after a stamp
     * ahead) by [self]; unchanged when it already rings on those (the PC's SetRingOn).
     */
    fun setRingOn(lists: TaskLists, labelId: String, ringOn: List<String>?, now: OffsetDateTime, self: String): TaskLists {
        val wanted = TaskRingDevices.normalized(ringOn)
        val label = lists.labels.map { it.label }.firstOrNull { it.isLive && it.id.equals(labelId, ignoreCase = true) } ?: return lists
        if (TaskRingDevices.same(label.ringOn, wanted)) return lists
        val at = label.ringOnStamp?.let { after(now, it.at) } ?: now
        val changed = label.copy(ringOn = wanted, ringOnStamp = FieldStamp(at, self.lowercase()), editedAt = after(now, label.editedAt))
        return lists.copy(labels = lists.labels.map { if (it.label.id == label.id) it.copy(label = changed) else it })
    }

    /** A new label (or the live one of that name, unchanged); [color] when valid, else the first unused colour. */
    fun add(
        lists: TaskLists, name: String, color: String?, now: OffsetDateTime, self: String, onlyOnThisDevice: Boolean = false, removed: Collection<String>? = null,
    ): TaskLists = addWithId(lists, name, color, now, self, onlyOnThisDevice, removed).first

    /**
     * [add] with the id of the label the name came to (null when nothing was added). ADR 89: [onlyOnThisDevice] makes the label device-only
     * on [self]. A label of that name that this device can't see is joined, whatever was asked (this device is added to its list, a stamped
     * edit), so the same name made on two devices is one label shared by them; it is never made shared for everyone. A device with no id
     * makes a shared label. [removed] devices' device-only labels count as shared.
     */
    fun addWithId(
        lists: TaskLists, name: String, color: String?, now: OffsetDateTime, self: String, onlyOnThisDevice: Boolean = false, removed: Collection<String>? = null,
    ): Pair<TaskLists, String?> {
        val trimmed = name.trim()
        if (checkName(lists, null, trimmed, self, removed) != TaskLabelNameCheck.Fine) {
            return lists to null
        }

        val deviceOnly = onlyOnThisDevice && !self.equals(FieldStamp.NOBODY, ignoreCase = true) && self.isNotBlank()
        named(lists, trimmed)?.let { hidden ->
            return setOnlyOn(lists, hidden.id, joined(hidden.onlyOn, self), now, self) to hidden.id
        }

        val labels = lists.labels.map { it.label }.toMutableList()
        val id = TaskLabelBook.freeIdFor(trimmed, labels)
        val chosen = if (TaskLabelColors.isValid(color)) color!!.trim() else TaskLabelColors.firstUnused(labels)
        val position = (labels.filter { it.isLive }.maxOfOrNull { it.position } ?: 0.0) + 1
        val existing = labels.indexOfFirst { it.id == id }
        val stamp = if (existing >= 0) after(now, labels[existing].deletedAt ?: labels[existing].editedAt) else now
        val label = TaskLabel(id, trimmed, chosen, position, stamp)
        val me = self.lowercase()
        if (existing >= 0) {
            // Revived: rings on every device and shown on every device again unless asked otherwise, both stamped at the revive.
            labels[existing] = label.copy(ringOnStamp = FieldStamp(stamp, me), onlyOn = if (deviceOnly) listOf(me) else null, onlyOnStamp = FieldStamp(stamp, me))
        } else {
            labels += if (deviceOnly) label.copy(onlyOn = listOf(me), onlyOnStamp = FieldStamp(stamp, me)) else label
        }

        return with(lists, labels) to id
    }

    /**
     * A label's new colour (an invalid one changes nothing). ADR 89: a name that is a hidden label's (the user wrote it, so it is theirs)
     * joins that label first (this device is added to its list), never binds to it unseen; no [self] shows every label.
     */
    fun recolour(lists: TaskLists, name: String, color: String, now: OffsetDateTime, self: String = FieldStamp.NOBODY, removed: Collection<String>? = null): TaskLists {
        if (!TaskLabelColors.isValid(color)) return lists
        val wanted = color.trim()
        return changed(joinedIfHidden(lists, name, now, self, removed), name) { label -> if (label.color.equals(wanted, ignoreCase = true)) null else label.copy(color = wanted, editedAt = after(now, label.editedAt)) }
    }

    /**
     * A label's new name; refused (unchanged) when blank or another live label has it (a hidden one too: no two live labels share a name).
     * Its tasks keep it (they hold its id). A hidden label named [oldName] is joined first, as [recolour] does.
     */
    fun rename(lists: TaskLists, oldName: String, newName: String, now: OffsetDateTime, self: String = FieldStamp.NOBODY, removed: Collection<String>? = null): TaskLists {
        val trimmed = newName.trim()
        val other = named(lists, trimmed)
        if (trimmed.isEmpty() || (other != null && !TaskLabel.sameName(other.name, oldName))) return lists
        return changed(joinedIfHidden(lists, oldName, now, self, removed), oldName) { label -> if (label.name == trimmed) null else label.copy(name = trimmed, editedAt = after(now, label.editedAt)) }
    }

    // The label named [name] joined when this device can't see it; unchanged when it is shown, missing or there is no [self].
    private fun joinedIfHidden(lists: TaskLists, name: String, now: OffsetDateTime, self: String, removed: Collection<String>?): TaskLists {
        val label = named(lists, name) ?: return lists
        if (self.isBlank() || self.equals(FieldStamp.NOBODY, ignoreCase = true) || TaskScopes.labelVisible(label, self, removed)) return lists
        return setOnlyOn(lists, label.id, joined(label.onlyOn, self), now, self)
    }

    /** Deletes the label named [name]: its tasks are left with No label on every device. A label hidden here isn't deleted (a device can't delete what it can't see). */
    fun delete(lists: TaskLists, name: String, now: OffsetDateTime, self: String, removed: Collection<String>? = null): TaskLists {
        val label = named(lists, name) ?: return lists
        if (!TaskScopes.labelVisible(label, self, removed)) return lists
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
