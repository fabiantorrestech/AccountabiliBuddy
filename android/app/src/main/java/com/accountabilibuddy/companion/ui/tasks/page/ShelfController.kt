package com.accountabilibuddy.companion.ui.tasks.page

import com.accountabilibuddy.companion.CompanionApp
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
import com.accountabilibuddy.shared.tasks.SyncTaskView
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskBins
import com.accountabilibuddy.shared.tasks.TaskField
import com.accountabilibuddy.shared.tasks.TaskHistoryRules
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskLabelEdits
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskStep
import com.accountabilibuddy.shared.tasks.TaskView
import com.accountabilibuddy.shared.tasks.TaskViewConfig
import java.time.OffsetDateTime
import java.util.UUID

/**
 * What the phone's Tasks page (round 2 R7.3) can do to the task list. Every change goes through the phone's task store
 * ([com.accountabilibuddy.companion.tasks.PhoneTaskStore.change]) as [ShelfEdits] / [TaskLabelEdits] edits, so each field is stamped
 * by this phone and the store normalizes the result; nothing here writes the list directly. A change that can be taken back says so
 * with an Undo ([toast]).
 *
 * A task asked into This session while a PC's session runs is the PC's to take (its cap, its rules): it is sent through the existing
 * "Add to Today" action ([TasksController.addToToday]); without a running session a full This session sends it to Queued.
 */
class ShelfController(private val app: CompanionApp, private val toast: (TasksToast) -> Unit) {
    private val store get() = app.taskModel

    /** Moves tasks to [bin] ("Move to" in the ⋮ sheet, a row swipe). [pc] is the PC with a running session, or null. */
    fun move(ids: List<String>, bin: TaskBin, pc: TasksPc?) {
        if (bin == TaskBin.SESSION && pc != null) {
            sendToPcSession(ids, pc)
            return
        }

        val result = change { lists, self, now -> ShelfEdits.move(lists, ids, bin, self, now) } ?: return
        if (!result.changed) return
        val where = if (result.queuedInstead == result.ids.size) TaskBin.QUEUED else bin
        val words = movedWords(result.ids.size, where)
        val message = if (result.queuedInstead > 0) "This session is full. $words" else words
        toast(TasksToast(message, detail = seeText(result.ids.size, where), undo = undoOf(result, ShelfEdits.MOVE_FIELDS), goTo = goToOf(where)))
    }

    /** The Today strip's drop, and a Shelf row's swipe to Today: into Today › Queued, never This session. */
    fun toQueued(ids: List<String>) {
        val result = change { lists, self, now -> ShelfEdits.stripDrop(lists, ids, self, now) } ?: return
        if (!result.changed) return
        toast(TasksToast(movedWords(result.ids.size, TaskBin.QUEUED), detail = seeText(result.ids.size, TaskBin.QUEUED), undo = undoOf(result, ShelfEdits.MOVE_FIELDS), goTo = TasksToast.TODAY))
    }

    /** A drop on another page (R7.3b's drag; the page picker): a bin, a label (replaces it) or a tag (adds it). */
    fun dropOnPage(ids: List<String>, page: ShelfPage) {
        val result = change { lists, self, now -> ShelfEdits.dropOnPage(lists, ids, page, self, now) } ?: return
        if (!result.changed) return
        val count = result.ids.size
        val message = when {
            page.bin != null -> movedWords(count, page.bin!!)
            page.tag != null -> "${tasksWord(count)} tagged ${page.title}"
            else -> "${tasksWord(count)} moved to ${page.title}"
        }
        toast(TasksToast(message, detail = if (count == 1) "Tap to see it there" else "Tap to see them there", undo = undoOf(result, ShelfEdits.MOVE_FIELDS), goTo = page.key))
    }

    /** Ticks a task (Finished today), with Undo. */
    fun finish(id: String) {
        val result = change { lists, self, now -> ShelfEdits.finish(lists, id, self, now) } ?: return
        if (result.changed) toast(TasksToast("Finished", undo = undoOf(result, listOf(TaskField.BIN))))
    }

    /** Unticks a task finished today: back where it came from. */
    fun reopen(id: String) {
        change { lists, self, now -> ShelfEdits.reopen(lists, id, self, now) }
    }

    fun rename(id: String, title: String) {
        change { lists, self, now -> ShelfEdits.rename(lists, id, title, self, now) }
    }

    fun setHighlight(id: String, on: Boolean) = edit(id, TaskField.HIGHLIGHT) { it.copy(highlight = on) }

    fun setEstimate(id: String, minutes: Int?) = edit(id, TaskField.ESTIMATE) { it.copy(estimateMinutes = minutes?.takeIf { value -> value > 0 }) }

    fun setTags(id: String, tags: List<String>) = edit(id, TaskField.TAGS) { it.copy(tags = TaskLabels.clean(tags)) }

    /** The task's label: a live label's id, or null for No label. */
    fun setLabel(id: String, labelId: String?) = edit(id, TaskField.LABEL) { it.copy(labelId = labelId) }

    /** Ticks or unticks one step. */
    fun tickStep(id: String, stepId: String) = edit(id, TaskField.STEPS) { task ->
        val at = DotNetTime.cut(OffsetDateTime.now(java.time.ZoneOffset.UTC))
        task.copy(steps = task.steps?.map { step: TaskStep -> if (step.id.equals(stepId, ignoreCase = true)) step.copy(doneAt = if (step.isDone) null else at, focus = false) else step })
    }

    /** A new label named [name] (the first unused colour), given to the task [forTask] when there is one. Returns false for a blank name. */
    fun createLabel(name: String, forTask: String?): Boolean {
        if (name.isBlank()) return false
        change { lists, self, now ->
            val added = TaskLabelEdits.add(lists, name, null, now, self)
            val label = TaskLabelEdits.named(added, name)
            val applied = if (forTask != null && label != null) ShelfEdits.edit(added, forTask, TaskField.LABEL, self, now) { it.copy(labelId = label.id) }.lists else added
            ShelfChange(applied, if (applied == lists) emptyList() else listOf(label?.id.orEmpty()), emptyList())
        }
        return true
    }

    /** A new task at the end of [bin] with the label [labelId] and [tags]; true when it was added. */
    fun add(title: String, bin: TaskBin, labelId: String?, tags: List<String>?): Boolean {
        val id = UUID.randomUUID().toString()
        val result = change { lists, self, now -> ShelfEdits.add(lists, id, title, bin, labelId, tags, self, now) } ?: return false
        if (!result.changed) return false
        val where = if (result.queuedInstead > 0) TaskBin.QUEUED else bin
        toast(TasksToast(if (result.queuedInstead > 0) "This session is full. Added to Today · Queued" else "Added to ${TaskBins.place(where)}", goTo = goToOf(where)))
        return true
    }

    /** Deletes tasks into History, with Undo. */
    fun delete(ids: List<String>) {
        val result = change { lists, self, now -> ShelfEdits.delete(lists, ids, self, now) } ?: return
        if (!result.changed) return
        val gone = result.ids
        toast(TasksToast("${tasksWord(gone.size)} deleted", detail = "In History for 30 days", undo = { change { lists, self, now -> ShelfEdits.undoDelete(lists, gone, self, now) } }))
    }

    /** History › Do again: a finished task's copy at the end of Queued. */
    fun doAgain(entryId: String) {
        val done = change { lists, self, now ->
            val result = TaskHistoryRules.doAgain(lists, entryId, now, self) { UUID.randomUUID().toString() }
            ShelfChange(result.lists, result.ids, emptyList())
        }
        if (done?.changed == true) toast(TasksToast("Added to Today · Queued", goTo = TasksToast.TODAY))
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
    fun createView(name: String, config: TaskViewConfig): String? {
        val trimmed = name.trim()
        if (trimmed.isEmpty()) return null
        val id = UUID.randomUUID().toString()
        change { lists, _, now ->
            val position = (lists.views.filter { it.view.isLive }.maxOfOrNull { it.view.position } ?: 0.0) + 1
            ShelfChange(lists.copy(views = lists.views + SyncTaskView(TaskView(id, trimmed, null, config, position, now))), listOf(id), emptyList())
        }
        return id
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

    private fun movedWords(count: Int, bin: TaskBin): String =
        if (TaskBins.isShelf(bin)) "${tasksWord(count)} moved to ${TaskBins.name(bin)}" else "${tasksWord(count)} added to ${TaskBins.place(bin)}"

    private fun seeText(count: Int, bin: TaskBin): String {
        val them = if (count == 1) "it" else "them"
        return if (TaskBins.isShelf(bin)) "Tap to see $them there" else "Tap to see $them in Today"
    }

    private fun tasksWord(count: Int): String = if (count == 1) "1 task" else "$count tasks"
}
