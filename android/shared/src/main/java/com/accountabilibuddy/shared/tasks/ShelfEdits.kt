package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.TaskLabels
import java.time.OffsetDateTime

/**
 * The changes the phone's Tasks page makes to the task list (round 2 R7.3), each as this device's stamped edit: every changed field
 * goes through [TaskEdits] (a move is a bin edit that keeps the task's id, number, steps, reminders and History), a delete through
 * [TaskHistoryRules]. Pure: the store applies the result ([ShelfChange.lists]) and normalizes it.
 *
 * The session's action cap is kept here as on the PC's strip and in actions from the phone: a task asked into a full This session
 * goes to Queued ([ShelfChange.queuedInstead]) and never past the cap.
 */
object ShelfEdits {
    /** The longest time limit a task takes (the PC's SessionChecklist.MaxEstimateMinutes). */
    const val MAX_ESTIMATE_MINUTES = 24 * 60

    /** The longest note kept (the PC's SessionChecklist.MaxNoteLength). */
    const val MAX_NOTE_LENGTH = 4000

    /** The reminder rules tied to a task's due date and time, as the Add sheet offers them. */
    val REMINDER_RULES: List<String> = listOf(TaskReminder.AT_START, TaskReminder.TEN_MINUTES_BEFORE, TaskReminder.ONE_HOUR_BEFORE, TaskReminder.DAY_BEFORE_AT_NINE)

    /** A reminder rule in words: "At the time", "10 min before", "1 hr before", "The day before, 9:00 AM". */
    fun reminderWords(rule: String): String = when (rule) {
        TaskReminder.AT_START -> "At the time"
        TaskReminder.TEN_MINUTES_BEFORE -> "10 min before"
        TaskReminder.ONE_HOUR_BEFORE -> "1 hr before"
        TaskReminder.DAY_BEFORE_AT_NINE -> "The day before, 9:00 AM"
        else -> "Reminder"
    }

    /** The fields [restore] puts back after a move, a drop on a label or tag page, or a tick. */
    val MOVE_FIELDS: List<TaskField> = listOf(TaskField.BIN, TaskField.STEPS, TaskField.LABEL, TaskField.TAGS)

    /**
     * Moves the live tasks among [ids] to [bin], each to the end of it, open, in the order given. Into This session only while its
     * actions fit ([sessionActions]), else Queued; steps lose their focus outside This session and the first open ones get it inside.
     * A task already there is left alone.
     */
    fun move(lists: TaskLists, ids: List<String>, bin: TaskBin, self: String, now: OffsetDateTime, sessionActions: Int = TaskLocalEdits.SESSION_ACTIONS): ShelfChange {
        var tasks = lists.tasks
        val touched = ArrayList<String>()
        val before = ArrayList<TaskItem>()
        var queuedInstead = 0
        for (id in ids.map { it.lowercase() }.distinct()) {
            val index = tasks.indexOfFirst { it.id.equals(id, ignoreCase = true) && it.task.isLive }
            if (index < 0) continue
            val held = tasks[index]
            val task = held.task
            var target = bin
            if (bin == TaskBin.SESSION && free(tasks, sessionActions, task.id) < task.actionCount.coerceAtLeast(1)) {
                target = TaskBin.QUEUED
            }

            if (task.bin == target && task.doneAt == null) continue
            if (target != bin) queuedInstead++
            var working = TaskEdits.edit(held, TaskField.BIN, task.copy(bin = target, prevBin = null, doneAt = null, position = TaskListRules.endOf(tasks, target)), now, self)
            val steps = working.task.steps
            if (steps != null) {
                val unfocused = steps.map { it.copy(focus = false) }
                val wanted = if (target == TaskBin.SESSION) {
                    TaskMigration.fillSession(listOf(working.task.copy(steps = unfocused)), maxOf(1, free(tasks, sessionActions, task.id))).single().steps
                } else {
                    unfocused
                }
                working = TaskEdits.edit(working, TaskField.STEPS, working.task.copy(steps = wanted), now, self)
            }

            tasks = tasks.toMutableList().also { it[index] = working }
            touched += held.id
            before += task
        }

        return ShelfChange(if (touched.isEmpty()) lists else lists.copy(tasks = tasks), touched, before, queuedInstead)
    }

    /**
     * The Today strip's drop (the PC's TodayStripDrop): the live Shelf tasks among [ids] go to Today › Queued. Never into This
     * session, so the cap is never in play; a task of Today is left where it is.
     */
    fun stripDrop(lists: TaskLists, ids: List<String>, self: String, now: OffsetDateTime): ShelfChange = move(lists, stripMovable(lists, ids), TaskBin.QUEUED, self, now)

    /** The ids among [ids] the strip takes: live tasks in a Shelf bin, each once. */
    fun stripMovable(lists: TaskLists, ids: List<String>): List<String> {
        val shelf = lists.tasks.filter { it.task.isLive && TaskBins.isShelf(it.task.bin) }.map { it.id.lowercase() }.toSet()
        return ids.map { it.lowercase() }.distinct().filter { it in shelf }
    }

    /**
     * A drop on another page (change note §2 "Edge cases"): a bin page moves the tasks there; a label page gives them that label in
     * place of the old one ("No label" clears it); a tag page adds the tag and keeps the others ("No tag" changes nothing).
     */
    fun dropOnPage(
        lists: TaskLists, ids: List<String>, page: ShelfPage, self: String, now: OffsetDateTime, sessionActions: Int = TaskLocalEdits.SESSION_ACTIONS, removed: Collection<String>? = null,
    ): ShelfChange = when {
        page.bin != null -> move(lists, ids, page.bin, self, now, sessionActions)
        page.tag != null -> each(lists, ids, TaskField.TAGS, self, now, removed) { it.copy(tags = TaskLabels.with(it.tags, listOf(page.tag))) }
        page.labelId != null || page.key == TaskViewRules.LABEL_NONE -> each(lists, ids, TaskField.LABEL, self, now, removed) { it.copy(labelId = page.labelId) }
        else -> ShelfChange(lists, emptyList(), emptyList())
    }

    /** Ticks the task: Finished today, remembering the bin it came from; its place in History comes when the day is over. */
    fun finish(lists: TaskLists, id: String, self: String, now: OffsetDateTime): ShelfChange =
        each(lists, listOf(id), TaskField.BIN, self, now, null) { task ->
            if (task.isDone) task else task.copy(bin = TaskBin.DONE, prevBin = task.bin, doneAt = now, position = TaskListRules.endOf(lists.tasks, TaskBin.DONE))
        }

    /** Unticks a finished task: back to the bin it came from (Queued when that was This session and it is full now). */
    fun reopen(lists: TaskLists, id: String, self: String, now: OffsetDateTime, sessionActions: Int = TaskLocalEdits.SESSION_ACTIONS): ShelfChange {
        val task = lists.tasks.firstOrNull { it.id.equals(id, ignoreCase = true) && it.task.isLive }?.task
        if (task == null || !task.isDone) return ShelfChange(lists, emptyList(), emptyList())
        return move(lists, listOf(id), task.prevBin ?: TaskBin.QUEUED, self, now, sessionActions)
    }

    /** One field of one task changed by [change] (the same task back: nothing changes). */
    fun edit(lists: TaskLists, id: String, field: TaskField, self: String, now: OffsetDateTime, removed: Collection<String>? = null, change: (TaskItem) -> TaskItem): ShelfChange =
        each(lists, listOf(id), field, self, now, removed, change)

    /**
     * One field of each live task among [ids] changed by [change] (select mode's Label, Tags and Time limit for several). ADR 89: a task keeps
     * the labels and tags it holds that are hidden here, and a hidden one the user set is joined ([TaskScopes.keepHidden]; [removed]: devices
     * removed from the group, null for none). Every edit through [edit], [editAll], [dropOnPage], [rename] and [add] ends this way.
     */
    fun editAll(lists: TaskLists, ids: List<String>, field: TaskField, self: String, now: OffsetDateTime, removed: Collection<String>? = null, change: (TaskItem) -> TaskItem): ShelfChange =
        each(lists, ids, field, self, now, removed, change)

    /** The title, trimmed; a blank one changes nothing. */
    fun rename(lists: TaskLists, id: String, title: String, self: String, now: OffsetDateTime, removed: Collection<String>? = null): ShelfChange =
        if (title.isBlank()) ShelfChange(lists, emptyList(), emptyList()) else edit(lists, id, TaskField.TITLE, self, now, removed) { it.copy(title = title.trim()) }

    /**
     * A new task at the end of [bin] (Queued when This session is full), made on this phone. [labelId] must be a live label's id or
     * null. Its number comes from the store's own numbering. The Add sheet's extras: a time limit ([estimateMinutes]; none for null
     * or 0, held to [MAX_ESTIMATE_MINUTES]), a [note], [steps] (their titles, blank ones dropped; [stepId] makes each one's id), a
     * due date with an optional time ([dueOn], [dueAt]) and reminders tied to it ([reminders]: the rules [REMINDER_RULES], each once,
     * at most [TaskReminder.MAX_PER_TASK]; without a due date they could never ring, so none is kept; [reminderId] makes each id).
     */
    fun add(
        lists: TaskLists, id: String, title: String, bin: TaskBin, labelId: String?, tags: List<String>?, self: String, now: OffsetDateTime,
        sessionActions: Int = TaskLocalEdits.SESSION_ACTIONS,
        removed: Collection<String>? = null,
        estimateMinutes: Int? = null, note: String? = null, steps: List<String>? = null, stepId: (Int) -> String = { "" },
        dueOn: java.time.LocalDate? = null, dueAt: java.time.LocalTime? = null, reminders: List<String>? = null, reminderId: (Int) -> String = { "" },
    ): ShelfChange {
        val trimmed = title.trim()
        if (trimmed.isEmpty() || lists.tasks.any { it.id.equals(id, ignoreCase = true) }) return ShelfChange(lists, emptyList(), emptyList())
        val target = if (bin == TaskBin.SESSION && free(lists.tasks, sessionActions, id) < 1) TaskBin.QUEUED else if (bin == TaskBin.DONE) TaskBin.QUEUED else bin
        val task = TaskItem(
            id.lowercase(), trimmed, target, TaskListRules.endOf(lists.tasks, target), now, now, now,
            labelId = labelId, tags = TaskLabels.clean(tags), origin = TaskOrigins.FROM_PHONE,
            estimateMinutes = estimateMinutes?.takeIf { it > 0 }?.coerceAtMost(MAX_ESTIMATE_MINUTES),
            note = note?.trim()?.take(MAX_NOTE_LENGTH)?.takeIf { it.isNotEmpty() },
            steps = steps.orEmpty().map { it.trim() }.filter { it.isNotEmpty() }.mapIndexed { index, step -> TaskStep(stepId(index).lowercase(), step) }.ifEmpty { null },
            dueOn = dueOn, dueAt = dueAt.takeIf { dueOn != null },
            reminders = reminders.orEmpty().takeIf { dueOn != null }.orEmpty().filter { it in REMINDER_RULES }.distinct().take(TaskReminder.MAX_PER_TASK)
                .mapIndexed { index, rule -> TaskReminder(reminderId(index).lowercase(), rule) }.ifEmpty { null },
        )
        val item = SyncTaskItem(task, TaskFieldStamps.allAt(FieldStamp(now, self.lowercase())))
        return ShelfChange(TaskScopes.keepHidden(lists, lists.copy(tasks = lists.tasks + item), self, removed, now), listOf(task.id), emptyList(), if (target != bin) 1 else 0)
    }

    /** Deletes the live tasks among [ids] into History ("deleted", 30 days, Bring back). */
    fun delete(lists: TaskLists, ids: List<String>, self: String, now: OffsetDateTime): ShelfChange {
        val before = lists.tasks.map { it.task }.filter { task -> task.isLive && ids.any { it.equals(task.id, ignoreCase = true) } }
        val result = TaskHistoryRules.remove(lists, ids, TaskHistoryKind.DELETED, now, self)
        return ShelfChange(result.lists, result.ids, before.filter { task -> result.ids.any { it.equals(task.id, ignoreCase = true) } })
    }

    /** Undo of [delete]: each task comes back from History under its own id and number. */
    fun undoDelete(lists: TaskLists, ids: List<String>, self: String, now: OffsetDateTime): ShelfChange {
        var working = lists
        val back = ArrayList<String>()
        for (id in ids) {
            val result = TaskHistoryRules.bringBack(working, id, now, self)
            working = result.lists
            back += result.ids
        }

        return ShelfChange(working, back, emptyList())
    }

    /**
     * Undo: each task of [before] that is still live gets [fields] back as they were, as new edits (stamped now, so every device
     * takes them). A moved task goes back to its bin and its old place; a re-labelled or tagged one gets its label and tags back.
     */
    fun restore(lists: TaskLists, before: List<TaskItem>, fields: List<TaskField>, self: String, now: OffsetDateTime): ShelfChange {
        var tasks = lists.tasks
        val touched = ArrayList<String>()
        for (old in before) {
            val index = tasks.indexOfFirst { it.id.equals(old.id, ignoreCase = true) && it.task.isLive }
            if (index < 0) continue
            var working = tasks[index]
            for (field in fields) working = TaskEdits.edit(working, field, TaskFieldValues.with(working.task, old, field), now, self)
            if (working !== tasks[index]) {
                tasks = tasks.toMutableList().also { it[index] = working }
                touched += working.id
            }
        }

        return ShelfChange(if (touched.isEmpty()) lists else lists.copy(tasks = tasks), touched, emptyList())
    }

    /** This session's free places, leaving [exceptId] out (it is the one moving in). */
    fun free(tasks: List<SyncTaskItem>, sessionActions: Int, exceptId: String = ""): Int = sessionActions -
        tasks.filter { it.task.isLive && it.task.bin == TaskBin.SESSION && !it.task.isDone && !it.id.equals(exceptId, ignoreCase = true) }.sumOf { it.task.actionCount }

    private fun each(lists: TaskLists, ids: List<String>, field: TaskField, self: String, now: OffsetDateTime, removed: Collection<String>?, change: (TaskItem) -> TaskItem): ShelfChange {
        var tasks = lists.tasks
        val touched = ArrayList<String>()
        val before = ArrayList<TaskItem>()
        for (id in ids.map { it.lowercase() }.distinct()) {
            val index = tasks.indexOfFirst { it.id.equals(id, ignoreCase = true) && it.task.isLive }
            if (index < 0) continue
            val held = tasks[index]
            val edited = TaskEdits.edit(held, field, change(held.task), now, self)
            if (edited === held) continue
            tasks = tasks.toMutableList().also { it[index] = edited }
            touched += held.id
            before += held.task
        }

        return ShelfChange(if (touched.isEmpty()) lists else TaskScopes.keepHidden(lists, lists.copy(tasks = tasks), self, removed, now), touched, before)
    }
}
