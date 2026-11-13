package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Today
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.sync.OverLimitStore
import com.accountabilibuddy.companion.ui.tasks.TaskKind
import com.accountabilibuddy.companion.ui.tasks.TaskRef
import com.accountabilibuddy.companion.ui.tasks.TasksController
import com.accountabilibuddy.companion.ui.tasks.TasksPc
import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamps
import com.accountabilibuddy.shared.sync.TaskLabels
import com.accountabilibuddy.shared.tasks.ShelfChange
import com.accountabilibuddy.shared.tasks.ShelfEdits
import com.accountabilibuddy.shared.tasks.ShelfPage
import com.accountabilibuddy.shared.tasks.ShelfReorder
import com.accountabilibuddy.shared.tasks.ShelfSession
import com.accountabilibuddy.shared.tasks.ShelfSteps
import com.accountabilibuddy.shared.tasks.ShelfToastLength
import com.accountabilibuddy.shared.tasks.ShelfToasts
import com.accountabilibuddy.shared.tasks.SyncTaskView
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskBins
import com.accountabilibuddy.shared.tasks.TaskField
import com.accountabilibuddy.shared.tasks.TaskHistoryRules
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskLabelEdits
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskReminder
import com.accountabilibuddy.shared.tasks.TaskReminderEdits
import com.accountabilibuddy.shared.tasks.TaskLocalEdits
import com.accountabilibuddy.shared.tasks.TaskStep
import com.accountabilibuddy.shared.tasks.TaskView
import com.accountabilibuddy.shared.tasks.TaskViewConfig
import java.time.OffsetDateTime
import java.util.UUID

/**
 * What the phone's Tasks page (round 2 R7.3) can do to the task list. Every change goes through the phone's task store
 * ([com.accountabilibuddy.companion.tasks.PhoneTaskStore.change]) as [ShelfEdits] / [ShelfReorder] / [ShelfSession] /
 * [TaskLabelEdits] edits, so each field is stamped by this phone and the store normalizes the result; nothing here writes the list
 * directly, and a closed task list takes no change. A change that can be taken back says so with an Undo ([toast]).
 *
 * A task asked into This session while a PC's session runs is the PC's to take (its cap, its rules): it is sent through the existing
 * "Add to Today" action ([TasksController.addToToday]). Without a running session This session keeps its cap: a task that finds no
 * room is offered a swap or "Add anyway" ([sessionFull], the Tasks page's "This session is full" sheet) and stays where it was
 * until then; where nothing can offer that ([sessionFull] null: Today's task sections) it goes to Queued and the message says so.
 */
class ShelfController(private val app: CompanionApp, private val sessionFull: ((List<String>) -> Unit)? = null, private val toast: (TasksToast) -> Unit) {
    private val store get() = app.taskModel

    /** Moves tasks to [bin] ("Move to" in the ⋮ sheet, a row swipe, select mode's Move). [pc] is the PC with a running session, or null. */
    fun move(ids: List<String>, bin: TaskBin, pc: TasksPc?) {
        if (bin == TaskBin.SESSION && pc != null) {
            sendToPcSession(ids, pc)
            return
        }

        // This session with no room for some of them: those wait for the sheet's swap or "Add anyway"; the rest go in.
        val waiting = if (bin == TaskBin.SESSION && sessionFull != null) ShelfSession.needsRoom(store.lists.value, ids) else emptyList()
        val going = ids.filter { id -> waiting.none { it.equals(id, ignoreCase = true) } }
        val result = change { lists, self, now -> ShelfEdits.move(lists, going, bin, self, now) }
        if (result != null && result.changed) {
            val where = if (result.queuedInstead == result.ids.size) TaskBin.QUEUED else bin
            val words = ShelfToasts.moved(result.ids.size, where)
            toast(movedToast(if (result.queuedInstead > 0) "This session is full. $words" else words, result, where))
        }

        if (waiting.isNotEmpty()) sessionFull?.invoke(waiting)
    }

    /** The Today strip's drop, and a Shelf row's swipe to Today: into Today › Queued, never This session. */
    fun toQueued(ids: List<String>) {
        val result = change { lists, self, now -> ShelfEdits.stripDrop(lists, ids, self, now) } ?: return
        if (!result.changed) {
            // Tasks of Today were dropped on the strip: they are there already.
            toast(TasksToast("Already in Today", goTo = TasksToast.TODAY, goLabel = "Open Today", icon = Icons.Outlined.Today))
            return
        }

        toast(movedToast(ShelfToasts.moved(result.ids.size, TaskBin.QUEUED), result, TaskBin.QUEUED))
    }

    /** A drop on another page (a zone, the page's list after a page turn, the picker): a bin, a label (replaces it) or a tag (adds it). */
    fun dropOnPage(ids: List<String>, page: ShelfPage) {
        val result = change { lists, self, now -> ShelfEdits.dropOnPage(lists, ids, page, self, now) } ?: return
        if (!result.changed) return
        val count = result.ids.size
        toast(
            TasksToast(
                ShelfToasts.dropped(count, page), detail = ShelfToasts.see(count, inToday = false), undo = undoOf(result, ShelfEdits.MOVE_FIELDS),
                goTo = page.key, flash = result.ids, goLabel = ShelfToasts.show(count, page.title), icon = page.bin?.let { binIcon(it) },
            ),
        )
    }

    /**
     * A drag let go on its own page: the rows [ids] of the page (its rows in order: [shown]) take the place with [insertAt] of the
     * other rows above them, with Undo.
     */
    fun reorder(shown: List<String>, ids: List<String>, insertAt: Int) {
        val result = change { lists, self, now -> ShelfReorder.place(lists, shown, ids, insertAt, self, now) } ?: return
        if (result.changed) toast(TasksToast("Moved", undo = undoOf(result, listOf(TaskField.BIN))))
    }

    /** One row up ([by] -1) or down (1) on its page: the reorder without dragging (a screen reader's action). */
    fun reorderStep(shown: List<String>, id: String, by: Int) {
        val result = change { lists, self, now -> ShelfReorder.step(lists, shown, id, by, self, now) } ?: return
        if (result.changed) toast(TasksToast(if (by < 0) "Moved up" else "Moved down", undo = undoOf(result, listOf(TaskField.BIN))))
    }

    /**
     * Ticks a task (Finished today), with Undo. [onGone] runs when its toast leaves (the page keeps the row, struck through, until
     * then). Returns what takes the tick back (the toast's Undo; also a second tap on the row's box), or null when nothing changed.
     */
    fun finish(id: String, onGone: (() -> Unit)? = null): (() -> Unit)? {
        val result = change { lists, self, now -> ShelfEdits.finish(lists, id, self, now) } ?: return null
        if (!result.changed) return null
        val undo = undoOf(result, listOf(TaskField.BIN))
        toast(TasksToast("Finished", undo = undo, onGone = onGone))
        return undo
    }

    /** Unticks a task finished today: back where it came from. */
    fun reopen(id: String) {
        change { lists, self, now -> ShelfEdits.reopen(lists, id, self, now) }
    }

    /**
     * The swap of "This session is full": [outgoingId] goes to Queued and [incomingId] takes its place. The toast says what
     * happened (7 s) and Undo restores both. False when the swap no longer makes room (the sheet reads the places again).
     */
    fun swap(incomingId: String, outgoingId: String): Boolean {
        val title = store.lists.value.tasks.firstOrNull { it.id.equals(outgoingId, ignoreCase = true) }?.task?.title.orEmpty()
        val result = change { lists, self, now -> ShelfSession.swap(lists, incomingId, outgoingId, self, now) } ?: return false
        if (!result.changed) return false
        toast(
            TasksToast(
                ShelfSession.swappedText(title), undo = undoOf(result, ShelfEdits.MOVE_FIELDS), goTo = TasksToast.TODAY, goLabel = "Open Today",
                icon = Icons.Outlined.Today, length = ShelfToastLength.EXPLANATION,
            ),
        )
        return true
    }

    /**
     * "Add anyway": [id] goes into This session over the cap, with the [reason] kept on this phone (at least
     * [OverLimitStore.MIN_REASON_LENGTH] characters; sealed, never synced, as "Keep it to 3" always did). False when the reason is
     * too short: nothing is kept and nothing moves.
     */
    fun addAnyway(id: String, reason: String): Boolean {
        if (!store.isActive || reason.trim().length < OverLimitStore.MIN_REASON_LENGTH) return false
        if (!app.overLimit.record(reason)) return false
        val result = change { lists, self, now -> ShelfSession.addAnyway(lists, listOf(id), self, now) } ?: return true
        if (result.changed) toast(movedToast(ShelfToasts.moved(result.ids.size, TaskBin.SESSION), result, TaskBin.SESSION))
        return true
    }

    fun rename(id: String, title: String) {
        change { lists, self, now -> ShelfEdits.rename(lists, id, title, self, now) }
    }

    fun setHighlight(id: String, on: Boolean) = edit(id, TaskField.HIGHLIGHT) { it.copy(highlight = on) }

    fun setEstimate(id: String, minutes: Int?) = edit(id, TaskField.ESTIMATE) { it.copy(estimateMinutes = minutes?.takeIf { value -> value > 0 }) }

    fun setTags(id: String, tags: List<String>) = edit(id, TaskField.TAGS) { it.copy(tags = TaskLabels.clean(tags)) }

    /** The task's label: a live label's id, or null for No label. */
    fun setLabel(id: String, labelId: String?) = edit(id, TaskField.LABEL) { it.copy(labelId = labelId) }

    /** "Done by": a date with a time or for the whole day ([time] null); [on] null removes it (R7.4; the PC's SetDue). */
    fun setDue(id: String, on: java.time.LocalDate?, time: java.time.LocalTime?) = edit(id, TaskField.DUE) { TaskReminderEdits.setDue(it, on, time) }

    /** Adds a reminder by one of the relative rules or [TaskReminder.ABSOLUTE] at [at]; false when it was refused (full, repeated, unknown). */
    fun addReminder(id: String, rule: String, at: OffsetDateTime?): Boolean {
        var added = true
        edit(id, TaskField.REMINDERS) { task -> TaskReminderEdits.add(task, UUID.randomUUID().toString(), rule, at) ?: task.also { added = false } }
        return added
    }

    /** Takes one reminder off the task. */
    fun removeReminder(id: String, reminderId: String) = edit(id, TaskField.REMINDERS) { task -> TaskReminderEdits.remove(task, reminderId) ?: task }

    /** Which devices the task syncs to: only this phone (a local task) or everywhere. */
    fun setOnlyThisDevice(id: String, only: Boolean) {
        val self = app.sync.deviceId.toString()
        edit(id, TaskField.DEVICES) { TaskReminderEdits.setDevices(it, only, self) }
    }

    /** Select mode's Label for several tasks: a live label's id, or null for No label; with Undo. */
    fun setLabelAll(ids: List<String>, labelId: String?) = bulk(ids, TaskField.LABEL, "relabelled") { it.copy(labelId = labelId) }

    /** Select mode's Tags for several tasks: [tag] added to each (the others kept); with Undo. */
    fun addTagAll(ids: List<String>, tag: String) {
        val clean = tag.trim().trimStart(TAG_SIGN)
        if (clean.isNotEmpty()) bulk(ids, TaskField.TAGS, "tagged $TAG_SIGN$clean") { it.copy(tags = TaskLabels.with(it.tags, listOf(clean))) }
    }

    /** Select mode's Time limit for several tasks (null: none); with Undo. */
    fun setEstimateAll(ids: List<String>, minutes: Int?) = bulk(ids, TaskField.ESTIMATE, "changed") { it.copy(estimateMinutes = minutes?.takeIf { value -> value > 0 }) }

    /** Ticks or unticks one step. */
    fun tickStep(id: String, stepId: String) = edit(id, TaskField.STEPS) { task ->
        val at = DotNetTime.cut(OffsetDateTime.now(java.time.ZoneOffset.UTC))
        task.copy(steps = task.steps?.map { step: TaskStep -> if (step.id.equals(stepId, ignoreCase = true)) step.copy(doneAt = if (step.isDone) null else at, focus = false) else step })
    }

    /** A new step at the end of the task's steps (the open task's "Add a step"). */
    fun addStep(id: String, title: String) {
        change { lists, self, now -> ShelfSteps.add(lists, id, UUID.randomUUID().toString(), title, self, now) }
    }

    /** The step menu's Delete step, with Undo. */
    fun removeStep(id: String, stepId: String) {
        val result = change { lists, self, now -> ShelfSteps.remove(lists, id, stepId, self, now) } ?: return
        if (result.changed) toast(TasksToast("Step deleted", undo = undoOf(result, listOf(TaskField.STEPS))))
    }

    /** The step menu's Move up / Move down. */
    fun moveStep(id: String, stepId: String, by: Int) {
        change { lists, self, now -> ShelfSteps.move(lists, id, stepId, by, self, now) }
    }

    /**
     * The step menu's "Make it its own task": the toast says so for 7 s, Undo puts the step back (the new task goes to History's
     * deleted tasks, as any undone add on another device would see it), and "Show" goes to the page it is on with the row flashing.
     */
    fun ownTask(id: String, stepId: String, title: String, goTo: String?) {
        val result = change { lists, self, now -> ShelfSteps.ownTask(lists, id, stepId, UUID.randomUUID().toString(), self, now) } ?: return
        if (!result.changed) return
        val made = result.ids.first()
        toast(
            TasksToast(
                ShelfSteps.ownTaskText(title), undo = {
                    change { lists, self, now ->
                        val back = ShelfEdits.restore(lists, result.before, listOf(TaskField.STEPS), self, now)
                        val gone = ShelfEdits.delete(back.lists, listOf(made), self, now)
                        ShelfChange(gone.lists, back.ids + gone.ids, emptyList())
                    }
                },
                goTo = goTo, flash = listOf(made), goLabel = "Show it", length = ShelfToastLength.EXPLANATION,
            ),
        )
    }

    /** A new label named [name] (the first unused colour), given to the tasks [forTasks]. Returns false for a blank name. */
    fun createLabel(name: String, forTasks: List<String>): Boolean {
        if (name.isBlank()) return false
        change { lists, self, now ->
            val added = TaskLabelEdits.add(lists, name, null, now, self)
            val label = TaskLabelEdits.named(added, name)
            val applied = if (forTasks.isNotEmpty() && label != null) ShelfEdits.editAll(added, forTasks, TaskField.LABEL, self, now) { it.copy(labelId = label.id) }.lists else added
            ShelfChange(applied, if (applied == lists) emptyList() else listOf(label?.id.orEmpty()), emptyList())
        }
        return true
    }

    /** A new label named [name], given to the task [forTask] when there is one. */
    fun createLabel(name: String, forTask: String?): Boolean = createLabel(name, listOfNotNull(forTask))

    /**
     * A new task at the end of [bin] with the label [labelId], [tags] and the Add sheet's extras (a time limit, a note, steps);
     * true when it was added. Asked into This session with no room, it is added to Today · Queued and offered a swap or "Add anyway"
     * ([sessionFull]): a task typed in the Add sheet never lands in To sort that way.
     */
    fun add(
        title: String, bin: TaskBin, labelId: String?, tags: List<String>?, estimateMinutes: Int? = null, note: String? = null, steps: List<String>? = null,
        dueOn: java.time.LocalDate? = null, dueAt: java.time.LocalTime? = null, reminders: List<String>? = null, pc: TasksPc? = null,
    ): Boolean {
        val id = UUID.randomUUID().toString()
        // This session while a PC's session runs is that PC's to take: the task is made in Next and asked of the PC from there.
        val viaPc = bin == TaskBin.SESSION && pc != null
        val full = !viaPc && bin == TaskBin.SESSION && sessionFull != null && ShelfEdits.free(store.lists.value.tasks, TaskLocalEdits.SESSION_ACTIONS) < 1
        val target = if (viaPc) TaskBin.NEXT else if (full) TaskBin.QUEUED else bin
        val result = change { lists, self, now ->
            ShelfEdits.add(
                lists, id, title, target, labelId, tags, self, now, estimateMinutes = estimateMinutes, note = note, steps = steps, stepId = { UUID.randomUUID().toString() },
                dueOn = dueOn, dueAt = dueAt, reminders = reminders, reminderId = { UUID.randomUUID().toString() },
            )
        } ?: return false
        if (!result.changed) return false
        if (viaPc) {
            sendToPcSession(listOf(id), pc)
            return true
        }

        if (full) {
            sessionFull.invoke(listOf(id))
            return true
        }

        val where = if (result.queuedInstead > 0) TaskBin.QUEUED else target
        val goes = goToOf(where)
        toast(
            TasksToast(
                if (result.queuedInstead > 0) "This session is full. Added to Today · Queued" else "Added to ${TaskBins.place(where)}",
                goTo = goes, flash = result.ids, goLabel = if (goes == TasksToast.TODAY) "Open Today" else ShelfToasts.show(1, TaskBins.name(where)),
                icon = if (goes == TasksToast.TODAY) Icons.Outlined.Today else binIcon(where),
            ),
        )
        return true
    }

    /** Deletes tasks into History, with Undo. */
    fun delete(ids: List<String>) {
        val result = change { lists, self, now -> ShelfEdits.delete(lists, ids, self, now) } ?: return
        if (!result.changed) return
        val gone = result.ids
        toast(TasksToast("${ShelfToasts.tasksWord(gone.size)} deleted", detail = "In History for 30 days", undo = { change { lists, self, now -> ShelfEdits.undoDelete(lists, gone, self, now) } }))
    }

    /** History › Do again: a finished task's copy at the end of Queued. */
    fun doAgain(entryId: String) {
        val done = change { lists, self, now ->
            val result = TaskHistoryRules.doAgain(lists, entryId, now, self) { UUID.randomUUID().toString() }
            ShelfChange(result.lists, result.ids, emptyList())
        }
        if (done?.changed == true) toast(TasksToast("Added to Today · Queued", goTo = TasksToast.TODAY, goLabel = "Open Today", icon = Icons.Outlined.Today))
    }

    /** History › Bring back: a deleted or archived task back in its bin. */
    fun bringBack(entryId: String) {
        val back = change { lists, self, now ->
            val result = TaskHistoryRules.bringBack(lists, entryId, now, self)
            ShelfChange(result.lists, result.ids, emptyList())
        }
        if (back?.changed == true) toast(TasksToast("Brought back"))
    }

    /** Saves [config] as a new view named [name] (synced with the other views); returns its id, or null for a blank name. */
    fun createView(name: String, config: TaskViewConfig, icon: String? = null): String? {
        val trimmed = name.trim()
        if (trimmed.isEmpty()) return null
        val id = UUID.randomUUID().toString()
        change { lists, _, now ->
            val position = (lists.views.filter { it.view.isLive }.maxOfOrNull { it.view.position } ?: 0.0) + 1
            ShelfChange(lists.copy(views = lists.views + SyncTaskView(TaskView(id, trimmed, icon, config, position, now))), listOf(id), emptyList())
        }
        return id
    }

    /** Gives a saved view (never a built-in one) another icon (a name from TaskViewIcons); synced with the other devices. */
    fun setViewIcon(id: String, icon: String) {
        change { lists, _, now ->
            val views = lists.views.map { item ->
                val view = item.view
                if (!view.id.equals(id, ignoreCase = true) || view.builtIn || !view.isLive || view.icon == icon) {
                    item
                } else {
                    item.copy(view = view.copy(icon = icon, editedAt = FieldStamps.next(now, view.editedAt)))
                }
            }
            ShelfChange(lists.copy(views = views), if (views == lists.views) emptyList() else listOf(id), emptyList())
        }
    }

    /** Deletes a saved view (never a built-in one). */
    fun deleteView(id: String) {
        change { lists, _, now ->
            val views = lists.views.map { item ->
                val view = item.view
                if (!view.id.equals(id, ignoreCase = true) || view.builtIn || !view.isLive) {
                    item
                } else {
                    val at = FieldStamps.next(now, view.editedAt)
                    item.copy(view = view.copy(name = "", editedAt = at, deletedAt = at))
                }
            }
            ShelfChange(lists.copy(views = views), if (views == lists.views) emptyList() else listOf(id), emptyList())
        }
    }

    private fun edit(id: String, field: TaskField, change: (TaskItem) -> TaskItem) {
        change { lists, self, now -> ShelfEdits.edit(lists, id, field, self, now, change) }
    }

    private fun bulk(ids: List<String>, field: TaskField, did: String, change: (TaskItem) -> TaskItem) {
        val result = change { lists, self, now -> ShelfEdits.editAll(lists, ids, field, self, now, change) } ?: return
        if (result.changed) toast(TasksToast("${ShelfToasts.tasksWord(result.ids.size)} $did", undo = undoOf(result, listOf(field))))
    }

    // The store applies the change (normalized, stamped by this phone) and the sync is told; null when the list isn't in use yet.
    private fun change(block: (TaskLists, String, OffsetDateTime) -> ShelfChange): ShelfChange? {
        if (!store.isActive) return null
        var result: ShelfChange? = null
        val changed = store.change { lists, self, now -> block(lists, self, now).also { result = it }.lists }
        if (changed) app.sharedLists.onChanged()
        return result
    }

    private fun undoOf(result: ShelfChange, fields: List<TaskField>): () -> Unit = {
        change { lists, self, now -> ShelfEdits.restore(lists, result.before, fields, self, now) }
    }

    // The toast of a move to a bin: the board's two lines, Undo, and the tap that goes there (a Shelf bin's page, or Today).
    private fun movedToast(message: String, result: ShelfChange, where: TaskBin): TasksToast {
        val count = result.ids.size
        val today = !TaskBins.isShelf(where)
        return TasksToast(
            message, detail = ShelfToasts.see(count, inToday = today), undo = undoOf(result, ShelfEdits.MOVE_FIELDS), goTo = goToOf(where), flash = result.ids,
            goLabel = if (today) "Open Today" else ShelfToasts.show(count, TaskBins.name(where)), icon = if (today) Icons.Outlined.Today else binIcon(where),
        )
    }

    // This session during a PC's session: asked of the PC, one task at a time, as "Add to Today" always did.
    private fun sendToPcSession(ids: List<String>, pc: TasksPc) {
        val old = TasksController(app) { message, _ -> toast(TasksToast(message)) }
        val tasks = store.lists.value.tasks.map { it.task }.filter { task -> task.isLive && ids.any { it.equals(task.id, ignoreCase = true) } }
        for (task in tasks) {
            when (task.bin) {
                TaskBin.SORT -> old.addToToday(TaskRef(TaskKind.ComeBack, task.id), pc)
                TaskBin.NEXT, TaskBin.LATER -> old.addToToday(TaskRef(TaskKind.Later, task.id), pc)
                else -> toast(TasksToast("${pc.name}'s session is running: move this one there."))
            }
        }
    }

    private fun goToOf(bin: TaskBin): String = if (TaskBins.isShelf(bin)) "bin:" + bin.name.lowercase() else TasksToast.TODAY

    private companion object {
        const val TAG_SIGN = '#'
    }
}
