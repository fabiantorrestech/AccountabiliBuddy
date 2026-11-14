package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.goals.GoalLabelId
import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.TaskLabels
import java.time.OffsetDateTime
import java.util.UUID

/**
 * Which labels and tags a device shows (ADR 89, device-only labels and tags; the PC's TaskScopes, the same rules). A label or tag is shared
 * by default. A device-only one still syncs (its name travels sealed, and the task keeps it wherever the task is edited) but is shown only on
 * the devices in its list ([TaskLabel.onlyOn]; for a tag, [SyncTagScope.onlyOn]):
 * - A null or empty list is shared. A list names several devices when they made the same name: it is shown on each of them.
 * - A removed device (ADR 54) drops out of every list at display time, nothing is written: a list whose devices are all removed counts as
 *   shared. A device not seen yet stays in its list, hidden here.
 * - A device with no id ([self] [FieldStamp.NOBODY] or blank: it isn't in a group) shows everything.
 * - Every writer of a task's tags or label keeps the hidden ones ([withHiddenTags], [withHiddenLabel]): a device can't delete what it can't
 *   see. A label chosen on this device replaces a hidden one; that is the user's act.
 */
object TaskScopes {
    // The PC's TaskScopes.TagNameSpace (the same bytes on every device), distinct from the labels'.
    private val TAG_NAMESPACE: UUID = UUID.fromString("9d2c7e41-5b8a-4f36-a0d9-3e61c7b2f854")

    /** The scope id for a tag name: as [TaskLabel.idFor] but in the tags' namespace. "Work" and "work" are one tag. */
    fun tagIdFor(name: String): String = GoalLabelId.idFor(TAG_NAMESPACE, name).toString()

    /** A device list in its stored form: no empty id, each once, in id order (lower case); none left is null (shared). */
    fun normalized(devices: List<String>?): List<String>? = TaskRingDevices.normalized(devices)?.takeIf { it.isNotEmpty() }

    /** The devices that still show an item: the list without the removed devices; null when shared (no list, or every device in it removed). */
    fun effective(onlyOn: List<String>?, removed: Collection<String>?): List<String>? {
        val list = normalized(onlyOn) ?: return null
        val gone = removed.orEmpty().map { it.lowercase() }.toSet()
        return list.filter { it !in gone }.ifEmpty { null }
    }

    /** Whether the list counts as shared: none, or every device in it removed. */
    fun isShared(onlyOn: List<String>?, removed: Collection<String>?): Boolean = effective(onlyOn, removed) == null

    /** Whether [self] shows an item with this list. A device with no id shows everything. */
    fun isVisible(onlyOn: List<String>?, self: String, removed: Collection<String>?): Boolean {
        if (self.isBlank() || self.equals(FieldStamp.NOBODY, ignoreCase = true)) return true
        val effective = effective(onlyOn, removed) ?: return true
        return effective.any { it.equals(self, ignoreCase = true) }
    }

    /** Whether the label is shown on [self]. */
    fun labelVisible(label: TaskLabel, self: String, removed: Collection<String>?): Boolean = isVisible(label.onlyOn, self, removed)

    /** The live labels shown on [self], in the order given. */
    fun visibleLabels(labels: List<TaskLabel>, self: String, removed: Collection<String>?): List<TaskLabel> =
        labels.filter { it.isLive && labelVisible(it, self, removed) }

    /** The live scope of a tag; null when the tag has none (it is shared). */
    fun scopeOf(tag: String, scopes: List<SyncTagScope>?): SyncTagScope? {
        val id = tagIdFor(tag)
        return scopes?.firstOrNull { it.id.equals(id, ignoreCase = true) && it.deleted == null }
    }

    /** Whether the tag is shown on [self] (a tag with no live scope is shared). */
    fun tagVisible(tag: String, scopes: List<SyncTagScope>?, self: String, removed: Collection<String>?): Boolean {
        val scope = scopeOf(tag, scopes) ?: return true
        return isVisible(scope.onlyOn, self, removed)
    }

    /** A search's label test ([TaskSearch.matchingIds]): true for the labels shown on [self]. */
    fun labelTest(self: String, removed: Collection<String>?): (TaskLabel) -> Boolean = { labelVisible(it, self, removed) }

    /** A search's tag test ([TaskSearch.matchingIds]): true for the tags shown on [self]. */
    fun tagTest(scopes: List<SyncTagScope>?, self: String, removed: Collection<String>?): (String) -> Boolean = { tagVisible(it, scopes, self, removed) }

    /** The label a task shows: its label when shown on [self], else null ("No label": the label itself is kept). */
    fun shownLabel(labelId: String?, labels: List<TaskLabel>, self: String, removed: Collection<String>?): String? {
        val held = labels.firstOrNull { it.id.equals(labelId, ignoreCase = true) && it.isLive } ?: return null
        return if (labelVisible(held, self, removed)) held.id else null
    }

    /** The tags a task shows: the ones shown on [self]; null when none. */
    fun shownTags(tags: List<String>?, scopes: List<SyncTagScope>?, self: String, removed: Collection<String>?): List<String>? =
        TaskLabels.clean(TaskLabels.clean(tags)?.filter { tagVisible(it, scopes, self, removed) })

    /**
     * The tags to store after an edit of the shown ones: [shownEdit] (what the user left of the tags this device shows, or typed) plus
     * every tag [held] has that this device doesn't show. Every writer of a task's tags goes through this. A tag the user typed that is
     * hidden here counts as theirs. Hidden tags always stay: when the list is full, the shown ones give way.
     */
    fun withHiddenTags(shownEdit: List<String>?, held: List<String>?, scopes: List<SyncTagScope>?, self: String, removed: Collection<String>?): List<String>? {
        val edit = TaskLabels.clean(shownEdit).orEmpty()
        val hidden = TaskLabels.clean(held).orEmpty().filter { !tagVisible(it, scopes, self, removed) && !TaskLabels.has(edit, it) }
        val shown = edit.take(maxOf(0, TaskLabels.MAX_PER_TASK - hidden.size))
        return TaskLabels.clean(shown + hidden)
    }

    /**
     * The label to store after an edit of the label this device shows: [chosen] when the user picked one (it replaces a hidden one: their
     * act); else the label [held] when it is hidden here; else none.
     */
    fun withHiddenLabel(chosen: String?, held: String?, labels: List<TaskLabel>, self: String, removed: Collection<String>?): String? {
        if (chosen != null) return chosen
        val label = labels.firstOrNull { it.id.equals(held, ignoreCase = true) && it.isLive } ?: return null
        return if (!labelVisible(label, self, removed)) label.id else null
    }

    /**
     * ADR 89, a device never loses sight of its own label (the PC's TaskScopes.RejoinedLabels): after a merge, a live label that this
     * device's own copy ([held], before the merge) showed here (a list that named [self]) but whose merged list is a list without it (another
     * device's newer stamp won, the same name made device-only on two devices while offline) gets the merged list joined by [self], stamped by
     * [self] just after the winning stamp. The joined list holds the winner's devices and carries the newest stamp, so no other device's
     * rule fires and every device ends with the same list. No act removes a device from a live list, so this never fights the user; screens
     * never offer "remove device X", and changing the scope of a label several devices share uses [TaskLabelEdits.joined].
     */
    fun rejoinedLabels(held: List<SyncTaskLabel>, merged: List<SyncTaskLabel>, self: String, now: OffsetDateTime): List<SyncTaskLabel> {
        val me = self.lowercase()
        if (me == FieldStamp.NOBODY || me.isBlank()) return merged
        val own = HashMap<String, TaskLabel>()
        for (item in held) if (item.label.isLive) own.putIfAbsent(item.id.lowercase(), item.label)
        return merged.map { item ->
            val label = item.label
            val mine = own[item.id.lowercase()]
            val before = if (label.isLive && mine != null) normalized(mine.onlyOn) else null
            val winner = normalized(label.onlyOn)
            if (before == null || !before.contains(me) || winner == null || winner.contains(me)) {
                item
            } else {
                val at = label.onlyOnStamp?.let { DotNetTime.plusTick(it.at) } ?: now
                item.copy(label = label.copy(onlyOn = TaskLabelEdits.joined(winner, me), onlyOnStamp = FieldStamp(at, me)))
            }
        }
    }

    /** The same rule for the tag scopes ([rejoinedLabels]): the scope is edited just after the winning edit. */
    fun rejoinedScopes(held: List<SyncTagScope>?, merged: List<SyncTagScope>?, self: String): List<SyncTagScope>? {
        val me = self.lowercase()
        if (me == FieldStamp.NOBODY || me.isBlank() || merged == null || held == null) return merged
        val own = HashMap<String, SyncTagScope>()
        for (scope in held) if (scope.deleted == null) own.putIfAbsent(scope.id.lowercase(), scope)
        return merged.map { scope ->
            val mine = own[scope.id.lowercase()]
            val before = if (scope.deleted == null && mine != null) normalized(mine.onlyOn) else null
            val winner = normalized(scope.onlyOn)
            if (before == null || !before.contains(me) || winner == null || winner.contains(me)) {
                scope
            } else {
                scope.copy(onlyOn = TaskLabelEdits.joined(winner, me), edited = DotNetTime.plusTick(scope.edited))
            }
        }
    }

    /**
     * ADR 89, a writer that doesn't see a label or tag never deletes it (the PC's TaskScopes.KeepHidden): [after] is [before] with a writer's
     * changes to tasks' tags and labels (the writer only knew what this device shows); every live task there that also is in [before] keeps
     * the hidden tags and the hidden label it held ([withHiddenTags], [withHiddenLabel]). A tag or label the writer set that is hidden here
     * and the task didn't hold was typed or chosen by the user, who joins it: this device is added to its list, never made shared for
     * everyone. Nothing changes on a device with no id. Every writer of a task's tags or label ends with this.
     * [joinNew] false: a task that is not in [before] (a copy, a handover, a restore) never makes this device join a hidden label or tag it
     * carries over from elsewhere, because nobody typed or picked it here; a typing path (the Add sheet) keeps the default true.
     */
    fun keepHidden(before: TaskLists, after: TaskLists, self: String, removed: Collection<String>?, now: OffsetDateTime, joinNew: Boolean = true): TaskLists {
        val me = self.lowercase()
        if (me == FieldStamp.NOBODY || me.isBlank()) return after
        val held = HashMap<String, TaskItem>()
        for (item in before.tasks) if (item.task.isLive) held.putIfAbsent(item.id.lowercase(), item.task)
        val labels = after.labels.map { it.label }
        val joinTags = ArrayList<String>()
        val joinLabels = ArrayList<String>()
        val tasks = after.tasks.map { item ->
            val task = item.task
            if (!task.isLive) return@map item
            val was = held[item.id.lowercase()]
            val tags = withHiddenTags(task.tags, was?.tags, after.tagScopes, me, removed)
            val mayJoin = was != null || joinNew
            if (mayJoin) joinTags += TaskLabels.clean(task.tags).orEmpty().filter { !TaskLabels.has(was?.tags, it) && !tagVisible(it, after.tagScopes, me, removed) }
            val chosen = task.labelId
            if (mayJoin && chosen != null && !chosen.equals(was?.labelId, ignoreCase = true)) {
                val picked = labels.firstOrNull { it.id.equals(chosen, ignoreCase = true) && it.isLive }
                if (picked != null && !labelVisible(picked, me, removed)) joinLabels += picked.id
            }

            val label = withHiddenLabel(task.labelId, was?.labelId, labels, me, removed)
            if (tags != TaskLabels.clean(task.tags) || label != task.labelId) item.copy(task = task.copy(tags = tags, labelId = label)) else item
        }

        var result = after.copy(tasks = tasks)
        for (tag in joinTags.distinctBy { it.trim().lowercase() }) {
            result = result.copy(tagScopes = TagScopeEdits.setScope(result.tagScopes, tag, true, now, me))
        }

        for (id in joinLabels.distinctBy { it.lowercase() }) {
            val label = result.labels.map { it.label }.firstOrNull { it.id.equals(id, ignoreCase = true) && it.isLive } ?: continue
            result = TaskLabelEdits.setOnlyOn(result, id, TaskLabelEdits.joined(label.onlyOn, me), now, me)
        }

        return result
    }

    /**
     * A file's tag scopes cleaned (the PC's TaskScopes.Clean): an entry with no id, a live one with no name or no devices, or a list over
     * [TaskRingDevices.MAX_DEVICES] devices is left out; device lists in their stored form; a tombstone holds neither name nor devices.
     */
    fun clean(scopes: List<SyncTagScope>): List<SyncTagScope> {
        val clean = ArrayList<SyncTagScope>()
        for (scope in scopes) {
            if (scope.id.equals(FieldStamp.NOBODY, ignoreCase = true)) continue
            if (scope.deleted != null) {
                clean += scope.copy(name = null, onlyOn = null)
                continue
            }

            val devices = normalized(scope.onlyOn)
            if (scope.name.isNullOrBlank() || devices == null || devices.size > TaskRingDevices.MAX_DEVICES) continue
            clean += scope.copy(onlyOn = devices)
        }

        return clean
    }
}
