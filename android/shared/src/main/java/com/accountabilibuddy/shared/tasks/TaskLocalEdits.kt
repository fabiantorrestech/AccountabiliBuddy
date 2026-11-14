package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.FieldStamps
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SyncAutoSavedItem
import com.accountabilibuddy.shared.sync.SyncLaterItem
import com.accountabilibuddy.shared.sync.SyncNextTask
import com.accountabilibuddy.shared.sync.SyncParked
import com.accountabilibuddy.shared.sync.TaskLabels
import com.accountabilibuddy.shared.sync.TaskPlannedFor
import java.time.Instant
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset
import java.time.format.DateTimeParseException
import java.util.UUID

/**
 * The phone's own changes to its old lists' views ([TaskFacadeLists]) put into the task list as this device's edits (ADR 76, R7.2):
 * every changed field goes through [TaskEdits], so its stamp is right; the phone's own counterpart of the PC's TaskListRules.ApplyLocal,
 * without the PC's pairing of an older app's moves (moves on the phone are [move], a bin edit that keeps the task's id, number, steps,
 * reminders and History).
 *
 * - A live old item of a live task in the same list: each field the old item carries and that differs from what the list would show
 *   (title, steps, label by name, tags, estimate, note, plan, due date, devices, ticked, place) is edited. Fields the old item can't
 *   carry (reminders, highlight, due time, number, step focus and estimates, calendar, can't miss, Next) are never touched.
 * - A live old item with no live task: a new task (in the list's bin: the next session's list fills This session while its actions
 *   fit, then Queued), or one that comes back from History (an untick of a finished Later item or come-back, an Undo of a delete),
 *   stamped after its tombstone so every device takes it back.
 * - A tombstone of a live task in that list: the task is deleted into History ("deleted"); one of a task that lives in another list
 *   now is the other half of a move and is left out; one of a ticked item from History closes its entry.
 */
object TaskLocalEdits {
    /** The session's places the phone fills (the PC's default cap; the phone has no setting of its own). */
    const val SESSION_ACTIONS = 3

    // Two positions closer than this are the same place (an old list's place maps back with a rounding error at most).
    private const val SAME_PLACE = 1e-6

    /**
     * The old lists' changes read in as this device's edits. ADR 89: the old lists show only what this device shows, so a task keeps the labels
     * and tags it holds that are hidden here, and a hidden label or tag the user wrote is joined ([TaskScopes.keepHidden]; [removed]: the devices
     * removed from the group, null for none).
     */
    fun apply(
        lists: TaskLists, changed: OldTaskFields, self: String, now: OffsetDateTime, zone: ZoneId, sessionActions: Int = SESSION_ACTIONS, removed: Collection<String>? = null,
    ): TaskLists {
        val items = changed.nextTasks.map(::of) + changed.later.map(::of) + changed.parked.map(::of) + changed.autoSaved.map(::of)
        val work = Work(lists, self.lowercase(), now, zone, sessionActions)
        val live = items.filter { it.deletedAt == null }

        // Arrivals first (a task that lives in another list now), so the other half of a move in the same change is seen as one.
        val (arrivals, others) = live.partition { item -> work.liveTask(item.id)?.let { !OldTaskFieldsWriter.sameHome(TaskFacadeLists.listOf(it.task, now), item.list) } ?: false }
        arrivals.forEach(work::applyLive)
        for (item in others) {
            val task = work.liveTask(item.id)
            if (task != null && !OldTaskFieldsWriter.sameHome(TaskFacadeLists.listOf(task.task, now), item.list)) {
                continue
            }

            work.applyLive(item)
        }

        items.filter { it.deletedAt != null }.forEach(work::applyTombstone)
        // Nobody typed these here (a copy, a handover, a restore): a new task never makes this phone join a hidden label or tag it carries.
        return TaskScopes.keepHidden(lists, work.result(), self, removed, now, joinNew = false)
    }

    /**
     * A move between the old lists that keeps the task (Q52): the task with [id] goes to [to]'s bin (the next session's list: This
     * session while its actions fit, else Queued; Later: Next when [toNext]; come-backs: To sort), at the end of it, open, steps out
     * of focus outside This session and its first open steps in focus inside it. Nothing changes when it is there already.
     */
    fun move(lists: TaskLists, id: String, to: OldTaskList, self: String, now: OffsetDateTime, zone: ZoneId, toNext: Boolean = false, sessionActions: Int = SESSION_ACTIONS): TaskLists {
        val work = Work(lists, self.lowercase(), now, zone, sessionActions)
        work.move(id, to, toNext)
        return work.result()
    }

    /** An old list's item in one shape (only what its list carries is known). */
    internal data class OldItem(
        val list: OldTaskList,
        val id: String,
        val title: String,
        val deletedAt: Instant?,
        val doneAt: Instant?,
        val position: Double,
        val category: String?,
        val categoryColor: String?,
        val tags: List<String>?,
        val localTo: String?,
        val subtasks: List<SessionTaskItem>? = null,
        val hasDetails: Boolean = false,
        val estimateMinutes: Int? = null,
        val note: String? = null,
        val plannedFor: TaskPlannedFor? = null,
        val hasDue: Boolean = false,
        val dueOn: String? = null,
        val marker: String? = null,
        val createdAt: Instant? = null,
        val autoSavedFor: LocalDate? = null,
    )

    internal fun of(item: SyncNextTask) = OldItem(
        OldTaskList.NEXT_TASKS, item.id, item.title, item.deletedAt, item.doneAt, item.position, item.category, item.categoryColor, item.labels, item.localTo,
        item.subtasks, true, item.estimateMinutes, item.note, item.plannedFor,
    )

    internal fun of(item: SyncLaterItem) = OldItem(
        OldTaskList.LATER, item.id, item.title, item.deletedAt, item.doneAt, item.position, item.category, item.categoryColor, item.labels, item.localTo,
        item.subtasks, true, item.estimateMinutes, item.note, item.plannedFor, hasDue = true, dueOn = item.dueOn, marker = item.bin,
    )

    internal fun of(item: SyncParked) = OldItem(
        OldTaskList.PARKED, item.id, item.text, item.deletedAt, item.doneAt, item.position, item.category, item.categoryColor, item.labels, item.localTo,
        createdAt = item.createdAt,
    )

    internal fun of(item: SyncAutoSavedItem) = OldItem(
        OldTaskList.AUTO_SAVED, item.id, item.title, item.deletedAt, null, item.position, item.category, item.categoryColor, item.labels, item.localTo,
        createdAt = item.savedAt, autoSavedFor = dateOf(item.wrapUpOf),
    )

    private class Work(var lists: TaskLists, val self: String, val now: OffsetDateTime, val zone: ZoneId, val sessionActions: Int) {
        private val book = TaskLabelBook(lists.labels.map { it.label }, self)

        fun result(): TaskLists = if (book.made == 0) lists else lists.copy(labels = mergedLabels())

        fun liveTask(id: String): SyncTaskItem? = lists.tasks.firstOrNull { it.id.equals(id, ignoreCase = true) && it.task.isLive }

        fun applyLive(item: OldItem) {
            val index = lists.tasks.indexOfFirst { it.id.equals(item.id, ignoreCase = true) }
            val held = lists.tasks.getOrNull(index)
            if (held == null || !held.task.isLive) {
                arrive(item, index, held)
                return
            }

            var working = held
            val task = working.task
            val home = TaskFacadeLists.listOf(task, now)
            if (!OldTaskFieldsWriter.sameHome(home, item.list)) {
                val target = defaultBin(item.list, item.marker, countsInSession = true)
                val doneAt = item.doneAt?.let(::time)
                val moved = if (doneAt != null) {
                    task.copy(bin = TaskBin.DONE, prevBin = target, doneAt = doneAt, position = TaskListRules.endOf(lists.tasks, TaskBin.DONE))
                } else {
                    task.copy(bin = target, prevBin = null, doneAt = null, position = OldTaskFieldsWriter.newPosition(target, item.position))
                }
                working = edit(working, TaskField.BIN, moved)
            } else {
                binChange(task, item)?.let { working = edit(working, TaskField.BIN, it) }
            }

            working = fields(working, item)
            put(index, working)
        }

        fun applyTombstone(item: OldItem) {
            val task = liveTask(item.id)
            if (task != null) {
                // The other half of a move (the task lives in another list now): not a delete.
                if (!OldTaskFieldsWriter.sameHome(TaskFacadeLists.listOf(task.task, now), item.list)) {
                    return
                }

                lists = TaskHistoryRules.remove(lists, listOf(task.id), TaskHistoryKind.DELETED, now, self).lists
                return
            }

            // A ticked item shown from History deleted: its entry goes too (it would come back ticked otherwise).
            val entry = TaskHistoryRules.liveEntry(lists, item.id)
            if (entry != null && entry.kind == TaskHistoryKind.FINISHED && item.localTo == null) {
                lists = TaskHistoryRules.closed(lists, entry.id, now)
            }
        }

        fun move(id: String, to: OldTaskList, toNext: Boolean) {
            val index = lists.tasks.indexOfFirst { it.id.equals(id, ignoreCase = true) && it.task.isLive }
            if (index < 0) return
            var working = lists.tasks[index]
            val task = working.task
            val target = if (to == OldTaskList.LATER && toNext) TaskBin.NEXT else defaultBin(to, null, countsInSession = true)
            if (task.bin == target && task.doneAt == null) return

            working = edit(working, TaskField.BIN, task.copy(bin = target, prevBin = null, doneAt = null, position = TaskListRules.endOf(lists.tasks, target)))
            val steps = working.task.steps
            if (steps != null) {
                val wanted = if (target == TaskBin.SESSION) {
                    val unfocused = working.task.copy(steps = steps.map { it.copy(focus = false) })
                    TaskMigration.fillSession(listOf(unfocused), maxOf(1, free(exceptId = task.id))).single().steps
                } else {
                    steps.map { it.copy(focus = false) }
                }
                working = edit(working, TaskField.STEPS, working.task.copy(steps = wanted))
            }

            put(index, working)
        }

        // An old item whose task isn't live here: back from History, back over its tombstone, or new.
        private fun arrive(item: OldItem, index: Int, held: SyncTaskItem?) {
            val entry = TaskHistoryRules.liveEntry(lists, item.id)
            if (entry != null && entry.kind == TaskHistoryKind.FINISHED && item.doneAt != null) {
                // Still ticked: it is in History, as it shows.
                return
            }

            var heldStamp: OffsetDateTime? = held?.task?.let { newest(it.editedAt, it.deletedAt) }
            if (entry != null) {
                heldStamp = laterOf(heldStamp, newest(entry.editedAt, entry.deletedAt))
                if (entry.kind == TaskHistoryKind.FINISHED) {
                    lists = TaskHistoryRules.closed(lists, entry.id, now)
                }
            }

            val at = if (heldStamp == null) now else FieldStamps.next(now, heldStamp)
            val open = defaultBin(item.list, item.marker, countsInSession = true)
            val doneAt = item.doneAt?.let(::time)
            val bin = if (doneAt == null) open else TaskBin.DONE
            val position = if (doneAt == null) OldTaskFieldsWriter.newPosition(open, item.position) else TaskListRules.endOf(lists.tasks, TaskBin.DONE)
            val base = entry?.task
            val start = base?.copy(id = base.id.lowercase(), steps = base.steps?.map { it.copy(focus = false) }, deletedAt = null)
                ?: TaskItem(
                    item.id.lowercase(), item.title, bin, position, item.createdAt?.let(::time) ?: at, at, at,
                    origin = originOf(item),
                    autoSavedFor = item.autoSavedFor,
                )
            var task = start.copy(
                title = item.title,
                bin = bin,
                prevBin = if (doneAt == null) null else open,
                doneAt = doneAt,
                position = position,
                labelId = book.idForName(item.category, item.categoryColor, at),
                tags = TaskLabels.clean(item.tags),
                devices = item.localTo?.let { listOf(it.lowercase()) } ?: start.devices,
                editedAt = at,
                touchedAt = at,
            )
            if (item.subtasks != null || base == null) task = task.copy(steps = stepsFrom(item.id, item.subtasks, base?.steps))
            if (item.hasDetails) task = task.copy(estimateMinutes = item.estimateMinutes, note = item.note, plannedFor = item.plannedFor)
            if (item.hasDue) {
                val due = dateOf(item.dueOn)
                task = task.copy(dueOn = due, dueAt = if (due == null) null else base?.dueAt)
            }

            if (bin == TaskBin.SESSION && task.steps != null) {
                task = TaskMigration.fillSession(listOf(task), maxOf(1, free(exceptId = task.id))).single().copy(bin = TaskBin.SESSION)
            }

            put(index, SyncTaskItem(task, TaskFieldStamps.allAt(FieldStamp(at, self))))
        }

        // The ticked state or place an old item in the task's own list asks for, or null.
        private fun binChange(task: TaskItem, item: OldItem): TaskItem? {
            val doneAt = item.doneAt?.let(::time)
            if (doneAt != null && task.doneAt == null) {
                return task.copy(bin = TaskBin.DONE, prevBin = task.bin, doneAt = doneAt, position = TaskListRules.endOf(lists.tasks, TaskBin.DONE))
            }

            if (doneAt == null && task.bin == TaskBin.DONE) {
                // Unticked: back where it was, This session only with room.
                var back = task.prevBin ?: defaultBin(item.list, item.marker, countsInSession = true)
                if (back == TaskBin.SESSION && free(exceptId = task.id) < task.actionCount) back = TaskBin.QUEUED
                return task.copy(bin = back, prevBin = null, doneAt = null, position = TaskListRules.endOf(lists.tasks, back))
            }

            if ((doneAt == null) == (task.doneAt == null) && task.bin == TaskFacadeLists.shownBin(task)) {
                val wanted = OldTaskFieldsWriter.newPosition(task.bin, item.position)
                if (kotlin.math.abs(wanted - task.position) > SAME_PLACE) return task.copy(position = wanted)
            }

            return null
        }

        // Each field the old item carries that differs from what its list shows of the task, edited.
        private fun fields(start: SyncTaskItem, item: OldItem): SyncTaskItem {
            var working = start
            val labels = lists.labels.map { it.label }
            val shown = projected(working.task, item.list, labels)
            if (item.title.isNotBlank() && item.title != shown.title) working = edit(working, TaskField.TITLE, working.task.copy(title = item.title))
            if (item.subtasks != null && stepKeys(item.subtasks) != stepKeys(shown.subtasks)) {
                working = edit(working, TaskField.STEPS, working.task.copy(steps = stepsFrom(item.id, item.subtasks, working.task.steps)))
            }

            if (item.hasDetails) {
                if (item.estimateMinutes != shown.estimateMinutes) working = edit(working, TaskField.ESTIMATE, working.task.copy(estimateMinutes = item.estimateMinutes))
                if (item.note != shown.note) working = edit(working, TaskField.NOTE, working.task.copy(note = item.note))
                if (item.plannedFor?.tieText != shown.plannedFor?.tieText) working = edit(working, TaskField.PLAN, working.task.copy(plannedFor = item.plannedFor))
            }

            if (item.hasDue && item.dueOn != shown.dueOn) {
                val due = dateOf(item.dueOn)
                if (due != working.task.dueOn) working = edit(working, TaskField.DUE, working.task.copy(dueOn = due, dueAt = if (due == null) null else working.task.dueAt))
            }

            if (!sameCategory(item.category, shown.category)) {
                working = edit(working, TaskField.LABEL, working.task.copy(labelId = book.idForName(item.category, item.categoryColor, now)))
            }

            val tags = TaskLabels.clean(item.tags)
            if (tags != TaskLabels.clean(shown.tags)) working = edit(working, TaskField.TAGS, working.task.copy(tags = tags))
            if (!item.localTo.equals(shown.localTo, ignoreCase = true)) {
                working = edit(working, TaskField.DEVICES, working.task.copy(devices = item.localTo?.let { listOf(it.lowercase()) }))
            }

            return working
        }

        private fun edit(item: SyncTaskItem, field: TaskField, changed: TaskItem): SyncTaskItem = TaskEdits.edit(item, field, changed, now, self)

        private fun put(index: Int, item: SyncTaskItem) {
            val tasks = lists.tasks.toMutableList()
            if (index >= 0) tasks[index] = item else tasks += item
            lists = lists.copy(tasks = tasks)
        }

        private fun mergedLabels(): List<SyncTaskLabel> {
            val byId = lists.labels.associateBy { it.label.id }
            return book.labels.map { label -> byId[label.id]?.takeIf { it.label == label } ?: SyncTaskLabel(label) }
        }

        // This session's free places, leaving [exceptId] out (it is the one moving in).
        private fun free(exceptId: String): Int = sessionActions -
            lists.tasks.filter { it.task.isLive && it.task.bin == TaskBin.SESSION && !it.id.equals(exceptId, ignoreCase = true) }.sumOf { it.task.actionCount }

        private fun defaultBin(list: OldTaskList, marker: String?, countsInSession: Boolean): TaskBin = when (list) {
            OldTaskList.NEXT_TASKS -> if (countsInSession && free(exceptId = "") >= 1) TaskBin.SESSION else TaskBin.QUEUED
            OldTaskList.LATER -> if (marker == SyncLaterItem.NEXT_BIN) TaskBin.NEXT else TaskBin.LATER
            else -> TaskBin.SORT
        }

        private fun originOf(item: OldItem): String? = when (item.list) {
            OldTaskList.PARKED -> TaskOrigins.parked(item.createdAt?.let(::time) ?: now, zone)
            OldTaskList.AUTO_SAVED -> item.autoSavedFor?.let(TaskOrigins::autoSaved)
            else -> TaskOrigins.FROM_PHONE
        }
    }

    // What the task's old list shows of it, in the old item's shape.
    private fun projected(task: TaskItem, list: OldTaskList, labels: List<TaskLabel>): OldItem {
        val shown = TaskFacadeLists.shownBin(task).let { if (it == task.bin) task else task.copy(bin = it) }
        return when (list) {
            OldTaskList.NEXT_TASKS -> of(OldTaskFieldsWriter.toNextTask(task, labels))
            OldTaskList.LATER -> of(OldTaskFieldsWriter.toLaterItem(shown, labels))
            OldTaskList.PARKED -> of(OldTaskFieldsWriter.toParked(shown, labels))
            OldTaskList.AUTO_SAVED -> of(OldTaskFieldsWriter.toAutoSaved(task, labels))
        }
    }

    private fun stepKeys(steps: List<SessionTaskItem>?): List<Triple<String?, String, Long?>> =
        steps.orEmpty().map { Triple(it.id?.lowercase(), it.title, it.doneAt?.let { at -> DotNetTime.ticks(at.atOffset(ZoneOffset.UTC)) }) }

    // The old steps as the task's steps: a step it had keeps its focus, estimate and unknown fields; null for none.
    private fun stepsFrom(taskId: String, subtasks: List<SessionTaskItem>?, held: List<TaskStep>?): List<TaskStep>? {
        val steps = subtasks.orEmpty().filter { it.title.isNotBlank() }.mapIndexed { index, subtask ->
            val id = subtask.id?.lowercase() ?: UUID.nameUUIDFromBytes("$taskId/$index/${subtask.title}".toByteArray()).toString()
            val doneAt = subtask.doneAt?.let(::time)
            val kept = held?.firstOrNull { it.id.equals(id, ignoreCase = true) }
            if (kept == null) {
                TaskStep(id, subtask.title, doneAt)
            } else {
                kept.copy(title = subtask.title, doneAt = if (kept.doneAt != null && doneAt != null && DotNetTime.same(kept.doneAt, doneAt)) kept.doneAt else doneAt)
            }
        }
        return steps.ifEmpty { null }
    }

    private fun sameCategory(first: String?, second: String?): Boolean {
        val a = first?.trim().orEmpty()
        val b = second?.trim().orEmpty()
        return a.equals(b, ignoreCase = true)
    }

    private fun newest(edited: OffsetDateTime, deleted: OffsetDateTime?): OffsetDateTime =
        if (deleted != null && DotNetTime.compare(deleted, edited) > 0) deleted else edited

    private fun laterOf(first: OffsetDateTime?, second: OffsetDateTime): OffsetDateTime =
        if (first == null || DotNetTime.compare(second, first) > 0) second else first

    private fun time(instant: Instant): OffsetDateTime = DotNetTime.cut(instant.atOffset(ZoneOffset.UTC))

    private fun dateOf(text: String?): LocalDate? = text?.let {
        try {
            LocalDate.parse(it)
        } catch (e: DateTimeParseException) {
            null
        }
    }
}
