package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.SharedListMerge
import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * What the phone's task store (ADR 76) does to its [TaskLists], pure (the PC's TaskListRules, the same rules):
 * - [normalize]: finished Shelf tasks go to History, archived and deleted entries of tasks that are live again go
 *   ([TaskHistoryRules.reconciled]), numbers are given, History past its 90 days and tombstones past their 30 go.
 * - [mergeFile]: another device's parts merged in. **A file without `tasks` leaves the tasks untouched** (R7.2, Q583): the phone
 *   does not read an older app's old fields into the task list (the PC does, with OldTaskFieldsReader).
 * - [fileParts]: the new parts and the old fields (rebuilt from the list, never stored) for this phone's file.
 */
object TaskListRules {
    /** Settles, numbers and prunes the lists; stamped by [self]. [numberingDevice]: the device that gives final numbers (null: none, temporary ones here). */
    fun normalize(lists: TaskLists, self: String, numberingDevice: String?, now: OffsetDateTime, zone: ZoneId?): TaskLists =
        pruned(numbered(TaskHistoryRules.reconciled(settled(lists, now, zone), now), self, numberingDevice, now), now, self)

    /**
     * A finished Shelf task (Next, Later, To sort) goes to History: an entry with its snapshot and a tombstone in the list, both stamped
     * with the task's own edit time, so two devices settling it agree. A finished Queued task stays in Finished today for the rest of its
     * finish day (in [zone]) and goes the same way once that day is over; with no zone it stays.
     */
    fun settled(lists: TaskLists, now: OffsetDateTime, zone: ZoneId?): TaskLists {
        val today = zone?.let { now.atZoneSameInstant(it).toLocalDate() }
        val tasks = lists.tasks.toMutableList()
        val entries = ArrayList<SyncTaskLogItem>()
        for (index in tasks.indices) {
            val task = tasks[index].task
            val doneAt = task.doneAt
            if (!task.isLive || task.bin != TaskBin.DONE || doneAt == null) {
                continue
            }

            val fromShelf = task.prevBin == TaskBin.SORT || task.prevBin == TaskBin.NEXT || task.prevBin == TaskBin.LATER
            val queuedDayOver = task.prevBin == TaskBin.QUEUED && today != null && doneAt.atZoneSameInstant(zone).toLocalDate() < today
            if (!fromShelf && !queuedDayOver) {
                continue
            }

            val at = task.editedAt
            entries += SyncTaskLogItem(TaskHistoryEntry(task.id, TaskHistoryKind.FINISHED, doneAt, task, at))
            tasks[index] = tasks[index].deleted(at)
        }

        return if (entries.isEmpty()) lists else lists.copy(tasks = tasks, log = SharedListMerge.merge(lists.log, entries, now.toInstant()))
    }

    /** Final numbers on the numbering device; elsewhere a temporary number for each live task without one, in creation order. */
    fun numbered(lists: TaskLists, self: String, numberingDevice: String?, now: OffsetDateTime): TaskLists {
        val inHistory = lists.log.map { it.entry.task }
        if (numberingDevice != null && numberingDevice.equals(self, ignoreCase = true)) {
            val final = TaskNumbering.assignFinal(lists.tasks, self, numberingDevice, now, inHistory)
            return if (final == lists.tasks) lists else lists.copy(tasks = final)
        }

        val tasks = lists.tasks.toMutableList()
        val missing = tasks.indices
            .filter { tasks[it].task.isLive && tasks[it].task.number == null }
            .sortedWith(compareBy<Int> { DotNetTime.ticks(tasks[it].task.createdAt) }.thenBy { tasks[it].id.lowercase() })
        for (index in missing) {
            val number = TaskNumbering.newNumber(tasks.map { it.task } + inHistory, isNumberingDevice = false)
            tasks[index] = TaskEdits.edit(tasks[index], TaskField.NUMBER, tasks[index].task.copy(number = number), now, self)
        }

        return if (missing.isEmpty()) lists else lists.copy(tasks = tasks)
    }

    /** History past its 90 days goes; tombstones past their 30 days go (a local item's stub stays), except labels' (kept for good). */
    fun pruned(lists: TaskLists, now: OffsetDateTime, self: String): TaskLists {
        val keptTicks = TaskHistoryEntry.KEPT.seconds * TICKS_PER_SECOND
        val log = lists.log.filter { !it.entry.isLive || DotNetTime.ticks(now) - DotNetTime.ticks(it.entry.at) < keptTicks }
        val instant = now.toInstant()
        return TaskLists(
            SharedListMerge.merge(lists.tasks, emptyList(), instant, self),
            TaskLabelMerge.merge(lists.labels, emptyList(), now),
            SharedListMerge.merge(lists.views, emptyList(), instant),
            SharedListMerge.merge(log, emptyList(), instant),
            lists.account,
            if (lists.tagScopes == null) null else TagScopeMerge.merge(lists.tagScopes, emptyList(), now),
        )
    }

    /** A place after every live task in [bin] (1 in an empty bin). */
    fun endOf(lists: TaskLists, bin: TaskBin): Double = endOf(lists.tasks, bin)

    fun endOf(tasks: List<SyncTaskItem>, bin: TaskBin): Double {
        val positions = tasks.map { it.task }.filter { it.isLive && it.bin == bin }.map { it.position }
        return if (positions.isEmpty()) 1.0 else kotlin.math.floor(positions.max()) + 1
    }

    /**
     * A backup's task list as a restore takes it in (ADR 71, 89; the PC's TaskListRules.RestoredIncoming): its live tasks, labels, views and
     * scopes stamped [now] by [self], so they come back over later edits and deletions (their deletions don't). The device that wrote the backup
     * ([backupDevice], null in an older backup) is swapped for this device in every label list and tag scope, so a reinstalled device (a new
     * id) sees the labels and tags it made; a label's list stamp is stamped now like a scope's, so the backup's choice wins. A task keeps the
     * labels and tags this device holds that are hidden here ([TaskScopes.withHiddenTags], [TaskScopes.withHiddenLabel], over the lists as they
     * will be merged); [removed]: devices removed from the group.
     */
    fun restoredIncoming(current: TaskLists, backup: TaskLists, backupDevice: String?, self: String, now: OffsetDateTime, removed: Collection<String>?): TaskLists {
        val me = self.lowercase()
        val from = backupDevice?.lowercase()
        fun swapped(devices: List<String>?): List<String>? =
            if (from != null && from != FieldStamp.NOBODY && me != FieldStamp.NOBODY && from != me && devices != null) {
                TaskScopes.normalized(devices.map { if (it.lowercase() == from) me else it })
            } else {
                devices
            }

        val stamp = FieldStamp(now, me)
        val labels = backup.labels.filter { it.label.isLive }.map {
            it.copy(label = it.label.copy(editedAt = now, onlyOn = swapped(it.label.onlyOn), onlyOnStamp = if (it.label.onlyOnStamp == null) null else stamp))
        }
        val scopes = backup.tagScopes?.filter { it.deleted == null }?.map { it.copy(edited = now, onlyOn = swapped(it.onlyOn)) }
        val mergedLabels = TaskLabelMerge.merge(current.labels, labels, now).map { it.label }
        val mergedScopes = TagScopeMerge.merge(current.tagScopes, scopes, now)
        val held = HashMap<String, TaskItem>()
        for (item in current.tasks) if (item.task.isLive) held.putIfAbsent(item.id.lowercase(), item.task)
        val tasks = backup.tasks.filter { it.task.isLive }.map { item ->
            var task = item.task.copy(editedAt = now, touchedAt = now)
            val was = held[item.id.lowercase()]
            if (was != null) {
                task = task.copy(
                    tags = TaskScopes.withHiddenTags(task.tags, was.tags, mergedScopes, me, removed),
                    labelId = TaskScopes.withHiddenLabel(task.labelId, was.labelId, mergedLabels, me, removed),
                )
            }

            SyncTaskItem(task, TaskFieldStamps.allAt(stamp))
        }

        return TaskLists(
            tasks,
            labels,
            backup.views.filter { it.view.isLive }.map { it.copy(view = it.view.copy(editedAt = now)) },
            backup.log,
            backup.account,
            scopes,
        )
    }

    /**
     * Another device's (or a backup's) parts merged in: with `tasks`, tasks field by field and labels, views and History item by item
     * ([TaskListMerge]); **without `tasks` nothing of the task list changes** (Q583: a missing part is never read as an empty one, and an
     * older app's old fields are not read in). The account's Tasks settings merge whenever the file carries them. Null when nothing
     * was merged. ADR 89's tag scopes merge whenever the file carries them.
     */
    fun mergeFile(lists: TaskLists, parts: TaskParts?, self: String, now: OffsetDateTime): TaskLists? {
        val incomingTasks = parts?.tasks
        val merged = if (incomingTasks == null) {
            null
        } else {
            TaskListMerge.mergeLists(lists, TaskLists(incomingTasks, parts.labels.orEmpty(), parts.views.orEmpty(), parts.taskLog.orEmpty()), self, now)
        }

        var result = merged
        parts?.accountSettings?.let { account ->
            val basis = result ?: lists
            result = basis.copy(account = TaskAccountSettingsMerge.merge(basis.account, account, now))
        }

        // ADR 89: the tag scopes merge item by item whenever the file carries them; a file without (an older app, a QR file) leaves them alone.
        parts?.tagScopes?.let { scopes ->
            val basis = result ?: lists
            result = basis.copy(tagScopes = TaskListMerge.mergedScopes(lists.tagScopes, basis.tagScopes, TaskScopes.clean(scopes), self, now))
        }

        return result
    }

    /**
     * What this phone's file carries of the task list: the new parts (local items as their stubs; the account's settings once something
     * was chosen; a newer app's entries passed on as they came) and the old fields rebuilt from them for older apps
     * ([OldTaskFieldsWriter]; `later` always, even empty).
     */
    fun fileParts(lists: TaskLists, now: OffsetDateTime, zone: ZoneId, newer: TaskParts? = null): Pair<OldTaskFields, TaskParts> {
        val old = OldTaskFieldsWriter.write(lists.tasks, lists.labels, lists.log, now, zone)
        val parts = TaskParts(
            SharedListMerge.forFile(lists.tasks),
            lists.labels,
            lists.views,
            lists.log,
            lists.account?.takeIf { !it.isEmpty },
            newer?.newerTasks.orEmpty(),
            newer?.newerLabels.orEmpty(),
            newer?.newerViews.orEmpty(),
            newer?.newerTaskLog.orEmpty(),
            lists.tagScopes?.takeIf { it.isNotEmpty() },
        )
        return old to parts
    }

    private const val TICKS_PER_SECOND = 10_000_000L
}
