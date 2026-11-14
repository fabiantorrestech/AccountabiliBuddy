package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.TaskLabels
import java.time.OffsetDateTime

/** One group of a label or tag picker: "Shared" or "This device" (ADR 89) and what is in it. */
data class ScopeGroup<T>(val title: String, val deviceOnly: Boolean, val items: List<T>)

/**
 * What ONE device shows of the labels and tags (ADR 89, device-only labels and tags; LB1c on the phone, the PC's LB1b the same rules): built
 * from the task list, this device's id ([self]) and the devices removed from the group ([removed], ADR 54: their device-only labels count
 * as shared at display). Every phone screen that reads labels or tags reads [shown], never the raw list; every writer keeps the hidden ones
 * ([TaskScopes.keepHidden]), so hiding here never deletes anything.
 *
 * - A label or tag that is hidden here doesn't show anywhere: pickers, chips, pages, filters, search, Today, the old lists, the watch.
 * - A task whose only label is hidden here reads as "No label"; the label itself stays on the task.
 * - A device with no id shows everything.
 */
class TaskScopeView(val lists: TaskLists, val self: String, val removed: Set<String>) {
    companion object {
        /**
         * The view of a phone: a phone that is not in a group works alone, so it shows everything (no list can name it); a joined one shows what
         * its own id [deviceId] shows. The phone's screens build their view through this.
         */
        fun forPhone(lists: TaskLists, deviceId: String, joined: Boolean, removed: Set<String>): TaskScopeView =
            TaskScopeView(lists, if (joined) deviceId.lowercase() else FieldStamp.NOBODY, removed)
    }

    /** Whether this device shows everything (it has no id, so no list can name it). */
    val showsAll: Boolean = self.isBlank() || self.equals(FieldStamp.NOBODY, ignoreCase = true)

    /** The live labels shown here, in their order. */
    val labels: List<TaskLabel> = TaskScopes.visibleLabels(lists.labels.map { it.label }, self, removed).sortedBy { it.position }

    fun labelShown(label: TaskLabel): Boolean = TaskScopes.labelVisible(label, self, removed)

    fun tagShown(tag: String): Boolean = TaskScopes.tagVisible(tag, lists.tagScopes, self, removed)

    /** Whether the label named [name] is shown here; a name no live label has (an old category) is shown. */
    fun categoryShown(name: String?): Boolean {
        if (name.isNullOrBlank()) return true
        val label = TaskLabelEdits.named(lists, name) ?: return true
        return labelShown(label)
    }

    /** [tags] without the ones hidden here; null when none is left. */
    fun tags(tags: List<String>?): List<String>? = TaskScopes.shownTags(tags, lists.tagScopes, self, removed)

    /**
     * The task list as this device shows it: hidden labels left out, a task whose label is hidden with no label, hidden tags off its tags.
     * The same object when nothing is hidden. For READING only: a change goes through the store's writers on the real list, which keep what is
     * hidden here ([TaskScopes.keepHidden]).
     */
    fun shown(): TaskLists {
        if (showsAll) return lists
        val hiddenIds = lists.labels.filter { it.label.isLive && !labelShown(it.label) }.map { it.label.id.lowercase() }.toSet()
        val scopes = lists.tagScopes.orEmpty().filter { it.deleted == null }
        if (hiddenIds.isEmpty() && scopes.none { !tagShown(it.name.orEmpty()) }) return lists
        val kept = lists.labels.filter { it.label.id.lowercase() !in hiddenIds }
        val tasks = lists.tasks.map { item ->
            val task = item.task
            if (!task.isLive) return@map item
            val label = TaskScopes.shownLabel(task.labelId, lists.labels.map { it.label }, self, removed)
            val tags = tags(task.tags)
            if (label == task.labelId && tags == TaskLabels.clean(task.tags)) item else item.copy(task = task.copy(labelId = label, tags = tags))
        }
        return lists.copy(tasks = tasks, labels = kept)
    }

    /** The shown labels in the picker's two groups: "Shared", then "This device" (a label that names this device among several is in the second). */
    fun labelGroups(): List<ScopeGroup<TaskLabel>> {
        val (shared, own) = labels.partition { TaskScopes.isShared(it.onlyOn, removed) }
        return groups(shared, own)
    }

    /** [tags] (the shown ones) in the picker's two groups. */
    fun tagGroups(tags: List<String>): List<ScopeGroup<String>> {
        val (shared, own) = tags.filter { tagShown(it) }.partition { tag -> TaskScopes.scopeOf(tag, lists.tagScopes)?.let { TaskScopes.isShared(it.onlyOn, removed) } ?: true }
        return groups(shared, own)
    }

    /** The shown tags of live tasks, one each, in the order they first appear. */
    fun knownTags(): List<String> = TaskLabels.allOf(lists.tasks.filter { it.task.isLive }.map { it.task.tags }).filter { tagShown(it) }

    /**
     * Whether [tag] exists anywhere in the list: on any task (done and deleted ones too, which still hold their words and are seen by
     * other devices) or with a live scope. A typed tag that exists is never "new", so "Only on this phone" can't hide a tag other devices use.
     */
    fun tagExists(tag: String): Boolean =
        TaskScopes.scopeOf(tag, lists.tagScopes) != null || lists.tasks.any { TaskLabels.has(TaskLabels.clean(it.task.tags), tag) }

    /** The device-only tags shown here (their scope is live and names this device or, with no id, any), in name order. */
    fun deviceTags(): List<SyncTagScope> =
        lists.tagScopes.orEmpty().filter { it.deleted == null && !it.name.isNullOrBlank() && !TaskScopes.isShared(it.onlyOn, removed) && tagShown(it.name!!) }.sortedBy { it.name!!.lowercase() }

    /**
     * Whether the label is device-only here (not shared once removed devices are left out). The label's devices as names, this device first
     * ("this phone"), for "on <device>, <device>"; null when it is shared. [nameOf] gives a device's name from its id.
     */
    fun devicesLine(onlyOn: List<String>?, nameOf: (String) -> String): String? {
        val list = TaskScopes.effective(onlyOn, removed) ?: return null
        val me = self.lowercase()
        val names = list.sortedBy { if (it == me) 0 else 1 }.map { if (it == me) "this phone" else nameOf(it) }
        return "on " + names.joinToString(", ")
    }

    /** The other devices a hidden label of [name] is on: the note when a typed name already exists elsewhere ("'Work' is also on <device>"); null when it isn't hidden here. */
    fun hiddenNameDevices(name: String, nameOf: (String) -> String): List<String>? {
        val label = TaskLabelEdits.named(lists, name) ?: return null
        if (labelShown(label)) return null
        val list = TaskScopes.effective(label.onlyOn, removed) ?: return null
        return list.map(nameOf)
    }

    /** The device-only tag [tag] is hidden here and its devices; null when it is shown or has no scope. */
    fun hiddenTagDevices(tag: String, nameOf: (String) -> String): List<String>? {
        val scope = TaskScopes.scopeOf(tag, lists.tagScopes) ?: return null
        if (tagShown(tag)) return null
        return TaskScopes.effective(scope.onlyOn, removed)?.map(nameOf)
    }

    /**
     * The note for a saved view or filter that names labels or tags hidden here ("Uses a label from Work PC", the device's name only, never a
     * label's name); null when it names none. [labelIds] and [tags] are what the view filters by.
     */
    fun viewNote(labelIds: List<String>?, tags: List<String>?, nameOf: (String) -> String): String? {
        val labelDevices = labelIds.orEmpty().flatMap { id ->
            val label = lists.labels.map { it.label }.firstOrNull { it.isLive && it.id.equals(id, ignoreCase = true) }
            if (label == null || labelShown(label)) emptyList() else TaskScopes.effective(label.onlyOn, removed).orEmpty().map(nameOf)
        }.distinct()
        if (labelDevices.isNotEmpty()) return "Uses a label from " + labelDevices.joinToString(", ")
        val tagDevices = tags.orEmpty().flatMap { hiddenTagDevices(it, nameOf).orEmpty() }.distinct()
        return if (tagDevices.isNotEmpty()) "Uses a tag from " + tagDevices.joinToString(", ") else null
    }

    /**
     * The "Rings on" line for [task] (a task as this phone shows it): computed from the REAL task and list, because a task whose label is hidden
     * here reads as having none but still rings by that label's list. The line never names the label.
     */
    fun ringsLine(task: TaskItem, device: String): String? =
        TaskReminderRings.line(lists, lists.tasks.firstOrNull { it.id.equals(task.id, ignoreCase = true) && it.task.isLive }?.task ?: task, device)

    /** How many of the view's label ids are shown here (a hidden one doesn't count). */
    fun shownLabelCount(labelIds: List<String>?): Int = labelIds.orEmpty().count { id -> labels.any { it.id.equals(id, ignoreCase = true) } }

    /** How many of the view's tags are shown here. */
    fun shownTagCount(tags: List<String>?): Int = tags.orEmpty().count { tagShown(it) }

    private fun <T> groups(shared: List<T>, own: List<T>): List<ScopeGroup<T>> = buildList {
        if (shared.isNotEmpty()) add(ScopeGroup(TaskScopeRules.SHARED, false, shared))
        if (own.isNotEmpty()) add(ScopeGroup(TaskScopeRules.THIS_DEVICE, true, own))
    }
}

/**
 * The words and the scope changes of the phone's label and tag screens (ADR 89). "Make shared" is explicit and asks first
 * ([MAKE_SHARED_ASK]); nothing here ever removes one device from a list (the join rule): "only on this phone" is a [TaskLabelEdits.joined].
 */
object TaskScopeRules {
    const val SHARED = "Shared"
    const val THIS_DEVICE = "This device"
    const val ONLY_ON_THIS_PHONE = "Only on this phone"
    const val MAKE_SHARED = "Make shared"
    const val MAKE_SHARED_ASK = "It will show on all your devices"
    const val NO_LABEL = "No label"
    const val ONLY_HERE_ASK = "It will disappear from your other devices"

    /** What a typed label name means on this phone ([labelNameVerdict]). */
    enum class NameState { Blank, Fine, Taken, TakenElsewhere, JoinsElsewhere }

    /** The verdict on a typed name and the note under it (null: none); [saveable] is whether Add / Save may go ahead. */
    class NameVerdict(val state: NameState, val note: String?) {
        val saveable: Boolean get() = state == NameState.Fine || state == NameState.JoinsElsewhere
    }

    /**
     * A label name checked against the FULL list (a rename can't take a name another device's label holds, even one this phone doesn't show), so
     * nothing is a silent no-op: a name this phone shows is "taken"; a new name that exists on other devices only joins it (the join note, with
     * Make shared); a rename to such a name is refused with a note that names the device. [labelId] null: a new label. [nameOf]: a device's name.
     */
    fun labelNameVerdict(view: TaskScopeView, labelId: String?, name: String, nameOf: (String) -> String): NameVerdict {
        val check = TaskLabelEdits.checkName(view.lists, labelId, name, view.self, view.removed)
        if (check == TaskLabelNameCheck.Blank) return NameVerdict(NameState.Blank, null)
        val elsewhere = view.hiddenNameDevices(name, nameOf)
        return when {
            check == TaskLabelNameCheck.Taken && elsewhere != null -> NameVerdict(NameState.TakenElsewhere, "'${name.trim()}' is already a label on ${elsewhere.joinToString(", ")}. Pick another name.")
            check == TaskLabelNameCheck.Taken -> NameVerdict(NameState.Taken, "You already have a label with that name.")
            labelId == null && elsewhere != null -> NameVerdict(NameState.JoinsElsewhere, joinNote(name, elsewhere))
            else -> NameVerdict(NameState.Fine, null)
        }
    }

    /** The label made shared (every device shows it); only when the user [confirmed] it. A choice of display, so it applies at once. */
    fun makeLabelShared(lists: TaskLists, labelId: String, confirmed: Boolean, now: OffsetDateTime, self: String): TaskLists =
        if (!confirmed) lists else TaskLabelEdits.setOnlyOn(lists, labelId, null, now, self)

    /** The tag made shared (its scope is deleted); only when the user [confirmed] it. */
    fun makeTagShared(lists: TaskLists, tag: String, confirmed: Boolean, now: OffsetDateTime, self: String): TaskLists =
        if (!confirmed) lists else lists.copy(tagScopes = TagScopeEdits.setScope(lists.tagScopes, tag, false, now, self))

    /** The label shown on this device too, and only on the devices that hold it (this device joins its list); a shared label becomes this device's own. */
    fun makeLabelDeviceOnly(lists: TaskLists, labelId: String, now: OffsetDateTime, self: String): TaskLists {
        val label = lists.labels.map { it.label }.firstOrNull { it.isLive && it.id.equals(labelId, ignoreCase = true) } ?: return lists
        if (self.isBlank() || self.equals(FieldStamp.NOBODY, ignoreCase = true)) return lists
        return TaskLabelEdits.setOnlyOn(lists, labelId, TaskLabelEdits.joined(label.onlyOn, self), now, self)
    }

    /** The tag device-only on this device (joined when others hold it). */
    fun makeTagDeviceOnly(lists: TaskLists, tag: String, now: OffsetDateTime, self: String): TaskLists =
        lists.copy(tagScopes = TagScopeEdits.setScope(lists.tagScopes, tag, true, now, self))

    /** What a task row reads for its label: the label's name, or [NO_LABEL] when it has none or it is hidden here. */
    fun labelText(view: TaskScopeView, labelId: String?): String =
        view.labels.firstOrNull { it.id.equals(labelId, ignoreCase = true) }?.name ?: NO_LABEL

    /** The note under a new label's name when it exists on other devices only: "'Work' is also on <devices>. This phone will show it too." */
    fun joinNote(name: String, devices: List<String>): String =
        "'${name.trim()}' is also on ${devices.joinToString(", ")}. This phone will show it too, with the devices that have it."
}
