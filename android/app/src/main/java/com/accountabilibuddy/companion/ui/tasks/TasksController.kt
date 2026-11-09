package com.accountabilibuddy.companion.ui.tasks

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.sync.OverLimitStore
import com.accountabilibuddy.companion.sync.PcFileRemote
import com.accountabilibuddy.companion.sync.TaskCategoryChoices
import com.accountabilibuddy.companion.sync.TaskCategoryChoices.Choice
import com.accountabilibuddy.companion.sync.TaskFields
import com.accountabilibuddy.companion.sync.TodayHandoff
import com.accountabilibuddy.companion.sync.deviceIdOf
import com.accountabilibuddy.shared.sync.SyncAction
import com.accountabilibuddy.shared.sync.SyncLaterItem
import com.accountabilibuddy.shared.sync.SyncNextTask
import java.util.UUID

/**
 * What the Tasks pane does when the user acts on a row. It decides where a change goes and nothing else does:
 *
 * - With no session running, the next session's tasks, the come-backs and the Later list are edited here ([com.accountabilibuddy.companion.sync.SharedListsStore]),
 *   each change stamped so the newest edit wins on every device.
 * - With a session running, This session is the PC's checklist: add, edit, reorder and tick go to the PC as sync actions and it
 *   applies them under its own rules (three tasks, nothing ends or shortens a session). The phone never shows such a change as
 *   done before the PC's file says so, except a tick, which shows as sending.
 * - Locks are untouched: nothing here ends a session or lifts a guard.
 *
 * [say] shows a short message, with an Undo when the change can be taken back.
 */
class TasksController(private val app: CompanionApp, private val say: (message: String, undo: (() -> Unit)?) -> Unit) {
    private val lists get() = app.sharedLists

    /** True when the next task would be a 4th on this phone's list (the "Keep it to 3" prompt; a running session's PC decides for itself). */
    fun needsReason(pc: TasksPc?): Boolean = pc == null && lists.nextTasks.value.size >= OverLimitStore.MAX_TASKS

    // ---- Categories (ADR 62) ----

    /** A category of [name] on this phone's list, in the next colour no listed category has; the same name in any case is the one that exists. */
    fun createCategory(name: String): Choice {
        val listed = com.accountabilibuddy.companion.sync.TaskCategoryColors(app.taskCategories.categories.value, if (app.sync.sharesTaskLists()) app.sync.received.value else emptyList(), java.time.Instant.now()).listed
        listed.firstOrNull { it.name.equals(name.trim(), ignoreCase = true) }?.let { return Choice(it.name, it.color) }
        val added = app.taskCategories.add(name, listed.map { it.color })
        return Choice(added.name, added.color)
    }

    /** Chooses a category's colour; it wins on every device as the newest choice. */
    fun recolorCategory(name: String, color: String) {
        app.taskCategories.recolor(name, color)
    }

    /** A new name for a category: on this phone's list and on the items in it. A name another category has is refused. */
    fun renameCategory(choice: Choice, newName: String) {
        val taken = com.accountabilibuddy.companion.sync.TaskCategoryColors(app.taskCategories.categories.value, if (app.sync.sharesTaskLists()) app.sync.received.value else emptyList(), java.time.Instant.now()).listed
            .any { it.name.equals(newName, ignoreCase = true) && !it.name.equals(choice.name, ignoreCase = true) }
        if (taken) {
            say("There's already a category called $newName.", null)
            return
        }

        app.taskCategories.rename(choice.name, newName, choice.color)
        lists.relabelCategory(choice.name, newName, choice.color)
    }

    fun categoryUseCount(name: String): Int = lists.countInCategory(name)

    /** Takes a category off this phone's list and off the items in it. */
    fun deleteCategory(name: String) {
        app.taskCategories.delete(name)
        lists.relabelCategory(name, null, null)
    }

    // ---- Ticking ----

    /** Ticks a row (or un-ticks a ticked one that can be put back). A running task is ticked on the PC; it shows ticked at once. */
    fun tick(row: TaskRowModel, pc: TasksPc?, legacyPc: String?) {
        val id = row.ref.id
        when (row.ref.kind) {
            TaskKind.Next, TaskKind.DoneNext -> lists.toggleNextTask(id)
            TaskKind.Running -> if (pc != null && !row.sending) app.actions.add(SyncAction.TICK_TASK, pc.pcId, item = id)
            TaskKind.ComeBack -> {
                if (legacyPc == null) {
                    lists.tickParked(id)
                } else {
                    // An older PC owns its come-backs: the tick is an action for it, shown ticked until it answers.
                    deviceIdOf(legacyPc, app.sync.received.value)?.let { target -> if (!row.sending) app.actions.add(SyncAction.TICK_COMEBACK, target, item = id) }
                }
            }
            TaskKind.Later -> {
                lists.tickLater(id)
                say("Marked done. It stays on your devices under Done.") { lists.untickLater(id) }
            }
            TaskKind.DoneLater -> lists.untickLater(id)
            TaskKind.DoneRunning -> Unit
            // ADR 72: settled on every device (the PC whose history holds it ticks it there too).
            TaskKind.Unfinished -> lists.tickPast(id)
            TaskKind.AutoSaved -> Unit
        }
    }

    /** Ticks a step of a running task on the PC. */
    fun tickStep(pc: TasksPc, stepId: String) {
        app.actions.add(SyncAction.TICK_TASK, pc.pcId, item = stepId)
    }

    // ---- Moving between lists ----

    /**
     * "Add to Today": a Later item or a come-back joins today's list. With no session running it becomes the next session's task
     * (a 4th one only with [reason], as "Add anyway" asks); with one running it is sent to the PC's checklist and leaves its list
     * once the PC has taken it.
     *
     * @return False when a reason was needed and it was missing or too short (nothing moved).
     */
    fun addToToday(ref: TaskRef, pc: TasksPc?, reason: String? = null): Boolean {
        if (pc != null) {
            sendToPc(ref, pc)
            return true
        }

        if (needsReason(null) && (reason == null || !app.overLimit.record(reason))) {
            return false
        }

        val moved = when (ref.kind) {
            TaskKind.Later -> lists.moveLaterToNext(ref.id)
            TaskKind.ComeBack -> lists.moveParkedToNext(ref.id)
            else -> null
        }
        if (moved != null) say("Added to today's list.", null)
        return true
    }

    private fun sendToPc(ref: TaskRef, pc: TasksPc) {
        if (!pc.canEdit) {
            say("Update AccountabiliBuddy on ${pc.name} to add tasks to its session from here.", null)
            return
        }

        val taskId = UUID.randomUUID()
        val source: TodayHandoff.Source
        var sentStamp: java.time.Instant? = null
        val waitingFor = when (ref.kind) {
            TaskKind.Later -> TodayHandoff.Source.Later(ref.id)
            TaskKind.ComeBack -> TodayHandoff.Source.ComeBack(ref.id)
            else -> return
        }
        // One send per item: a second tap while the PC has not answered would add the task twice.
        if (app.todayHandoff.isWaiting(waitingFor, app.actions.pending.value)) {
            say("Still waiting for ${pc.name}'s answer for this one.", null)
            return
        }

        // A PC before 0.1.114 adds the task but ignores the note and the estimate, and answers a plain ok: the item would then be taken off
        // Later with those gone for good. So an item that has either stays in Later, and nothing is sent.
        if (ref.kind == TaskKind.Later && !pc.keepsDetails) {
            val item = lists.later.value.firstOrNull { it.id == ref.id && it.doneAt == null }
            if (item != null && (item.note != null || item.estimateMinutes != null)) {
                say("${pc.name} is on an older version that drops a note and an estimate. Update AccountabiliBuddy on it first; this stays in Later.", null)
                return
            }
        }

        // A full checklist parks a 4th task as a bare come-back: a Later item's note, steps, estimate and due date would not go with it.
        if (ref.kind == TaskKind.Later && pc.checklist.tasks.size >= OverLimitStore.MAX_TASKS) {
            say("${pc.name}'s session already has ${OverLimitStore.MAX_TASKS} tasks. It may park this as a bare come-back, so it stays in Later.", null)
            return
        }

        val problem = when (ref.kind) {
            TaskKind.Later -> {
                val item = lists.later.value.firstOrNull { it.id == ref.id && it.doneAt == null } ?: return
                sentStamp = item.editedAt
                source = TodayHandoff.Source.Later(item.id)
                app.pcRemote.addTask(
                    pc.pcId, item.title, category = item.category, categoryColor = item.categoryColor,
                    steps = item.subtasks.orEmpty().filter { !it.isDone }.map { it.title }, taskId = taskId,
                    estimateMinutes = item.estimateMinutes, note = item.note,
                )
            }
            TaskKind.ComeBack -> {
                val item = lists.parked.value.firstOrNull { it.id == ref.id && it.deletedAt == null } ?: return
                source = TodayHandoff.Source.ComeBack(item.id)
                app.pcRemote.addTask(pc.pcId, item.text, category = item.category, categoryColor = item.categoryColor, taskId = taskId)
            }
        }
        if (problem != null) {
            say(problem, null)
            return
        }

        app.todayHandoff.hold(taskId.toString(), source, sentStamp)
        say("Sent to ${pc.name}. It stays here until ${pc.name} has added it as a task (a full list parks it instead, and then it stays).", null)
    }

    /** "Move to Later" for a next-session task or a come-back. */
    fun moveToLater(ref: TaskRef) {
        val moved = when (ref.kind) {
            TaskKind.Next -> lists.moveNextToLater(ref.id)
            TaskKind.ComeBack -> lists.moveParkedToLater(ref.id)
            TaskKind.Unfinished -> lists.movePastToLater(ref.id).let { Unit }
            TaskKind.AutoSaved -> lists.autoSavedToLater(ref.id).let { Unit }
            else -> null
        }
        if (moved != null) say("Moved to Later.", null)
    }

    /** A Later item becomes a come-back. */
    fun moveToComeBack(ref: TaskRef) {
        if (ref.kind == TaskKind.Later && lists.moveLaterToParked(ref.id) != null) say("Moved to Come back to.", null)
    }

    /** Puts a ticked row back where it came from (a next task opens again, a Later item returns to Later). */
    fun putBack(row: TaskRowModel) {
        if (row.ref.kind == TaskKind.DoneNext || row.ref.kind == TaskKind.DoneLater) tick(row, null, null)
    }

    // ---- Deleting ----

    /** Deletes a next-session task, a come-back or a Later item (the Later one offers Undo). A running task is the PC's: no delete from here. */
    fun delete(ref: TaskRef) {
        when (ref.kind) {
            TaskKind.Next, TaskKind.DoneNext -> lists.deleteNextTask(ref.id)
            TaskKind.ComeBack -> lists.deleteParked(ref.id)
            TaskKind.Later, TaskKind.DoneLater -> lists.deleteLater(ref.id)?.let { item -> say("Deleted from Later.") { lists.restoreLater(item) } }
            TaskKind.Running, TaskKind.DoneRunning -> Unit
            TaskKind.Unfinished -> lists.deletePast(ref.id)
            TaskKind.AutoSaved -> lists.deleteAutoSaved(ref.id)
        }
    }

    // ---- Order ----

    /** Moves a row up ([by] -1) or down (+1) among its neighbours in This session (a running task is reordered on the PC). */
    fun move(ref: TaskRef, by: Int, pc: TasksPc?, sessionRows: List<TaskRowModel>) {
        val index = sessionRows.indexOfFirst { it.ref == ref }
        val neighbour = sessionRows.getOrNull(index + by) ?: return
        if (index < 0 || !sameCategory(sessionRows[index], neighbour)) return
        if (pc != null) {
            if (!pc.canEdit) return
            val ids = sessionRows.map { it.ref.id }.toMutableList()
            ids[index] = neighbour.ref.id
            ids[index + by] = ref.id
            app.runningTaskEdits.reorderTasks(pc.pcId, ids)
            say("Sent to ${pc.name}. The new order shows here after its next sync.", null)
            return
        }

        // Among the tasks of its category, counting the ticked ones the list keeps: after (or before) the neighbour.
        val task = lists.nextTasks.value.firstOrNull { it.id == ref.id } ?: return
        val others = lists.nextTasks.value.filter { it.category == task.category && it.id != task.id }.sortedBy { it.position }
        val at = others.indexOfFirst { it.id == neighbour.ref.id }
        if (at >= 0) lists.moveNextTask(task.id, if (by > 0) at + 1 else at)
    }

    private fun sameCategory(first: TaskRowModel, second: TaskRowModel) = first.category.equals(second.category, ignoreCase = true)

    // ---- Adding ----

    /** Adds a task to the next session's list (the caller has asked for a reason when [needsReason]). */
    fun addNext(text: String, category: Choice?) {
        lists.addNextTask(text, category?.name, category?.color)
    }

    /** Parks a text under Come back to. */
    fun park(text: String) {
        lists.addParked(text)
        say("Parked.", null)
    }

    /** Adds an item to the Later list. */
    fun addLater(text: String, category: Choice?) {
        lists.addLater(text, category?.name, category?.color)
    }

    /** Asks the PC to add a task to its running session. */
    fun addRunning(pc: TasksPc, text: String, category: Choice?) {
        if (!pc.canEdit) {
            say("Update AccountabiliBuddy on ${pc.name} to add tasks to its session from here.", null)
            return
        }

        val categoryId = category?.let { chosen -> TaskCategoryChoices.forChecklist(pc.checklist.tasks).firstOrNull { it.name.equals(chosen.name, ignoreCase = true) }?.id }
        app.runningTaskEdits.addTask(pc.pcId, text.trim(), categoryId)
        say("Sent to ${pc.name}. It shows here after its next sync. It keeps ${OverLimitStore.MAX_TASKS} tasks and parks a 4th.", null)
    }

    /** Rewords a come-back. */
    fun editComeback(id: String, text: String) = lists.editParked(id, text)

    // ---- The detail sheet ----

    /**
     * Writes what the detail sheet changed ([before] to [after], field by field) to the task [ref]. Fields it did not change are
     * left alone, so a change another device made meanwhile is not overwritten. A running task takes only a title and steps, and
     * they go to its PC.
     */
    fun commit(ref: TaskRef, before: TaskFields, after: TaskFields, pc: TasksPc?) {
        if (before == after) return
        when (ref.kind) {
            TaskKind.Next, TaskKind.DoneNext -> commitNext(ref.id, before, after)
            TaskKind.Later -> commitLater(ref.id, before, after)
            TaskKind.Running -> commitRunning(ref.id, before, after, pc)
            else -> Unit
        }
    }

    private fun commitNext(id: String, before: TaskFields, after: TaskFields) {
        // One edit, one stamp: the fields and, when the category changed, the move to the end of the new category.
        val category = if (before.category != after.category) after.category to after.categoryColor else null
        lists.updateNextTask(id, category) { task -> applyFields(task, before, after) }
    }

    private fun applyFields(task: SyncNextTask, before: TaskFields, after: TaskFields): SyncNextTask {
        var changed = task
        if (before.title != after.title && after.title.isNotBlank()) changed = changed.copy(title = after.title.trim())
        if (before.steps != after.steps) changed = changed.copy(subtasks = after.steps.filter { it.title.isNotBlank() }.ifEmpty { null })
        if (before.estimateMinutes != after.estimateMinutes) changed = changed.copy(estimateMinutes = TaskFields.cleanEstimate(after.estimateMinutes))
        if (before.note != after.note) changed = changed.copy(note = TaskFields.cleanNote(after.note))
        return changed
    }

    private fun commitLater(id: String, before: TaskFields, after: TaskFields) {
        lists.updateLater(id) { item -> applyFields(item, before, after) }
    }

    private fun applyFields(item: SyncLaterItem, before: TaskFields, after: TaskFields): SyncLaterItem {
        var changed = item
        if (before.title != after.title && after.title.isNotBlank()) changed = changed.copy(title = after.title.trim())
        if (before.steps != after.steps) changed = changed.copy(subtasks = after.steps.filter { it.title.isNotBlank() }.ifEmpty { null })
        if (before.estimateMinutes != after.estimateMinutes) changed = changed.copy(estimateMinutes = TaskFields.cleanEstimate(after.estimateMinutes))
        if (before.note != after.note) changed = changed.copy(note = TaskFields.cleanNote(after.note))
        if (before.category != after.category) changed = changed.copy(category = after.category, categoryColor = after.categoryColor)
        if (before.dueOn != after.dueOn) changed = changed.copy(dueOn = after.dueOn)
        return changed
    }

    private fun commitRunning(id: String, before: TaskFields, after: TaskFields, pc: TasksPc?) {
        if (pc == null) {
            say("That session ended, so this edit wasn't sent.", null)
            return
        }

        if (!pc.canEdit) {
            say("Update AccountabiliBuddy on ${pc.name} to edit its tasks from here.", null)
            return
        }

        // Steps go with their ids, so the PC keeps the ticks of the ones that stay, and a new step keeps the id the phone made for
        // it (the PC takes a wanted id that is free), so a later edit and a tick name the same step. Only what changed is sent.
        val title = after.title.trim().takeIf { it.isNotEmpty() && it != before.title }
        val steps = after.steps.filter { it.title.isNotBlank() }.map { PcFileRemote.TaskStep(it.id, it.title.trim()) }
            .takeIf { before.steps != after.steps }
        if (title == null && steps == null) return
        app.runningTaskEdits.editTask(pc.pcId, id, title, steps)
        say("Sent to ${pc.name}. It shows here after its next sync.", null)
    }
}
