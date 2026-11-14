package com.accountabilibuddy.companion.sync

import android.content.Context
import com.accountabilibuddy.shared.TokenVault
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SharedListMerge
import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.sync.SyncLaterItem
import com.accountabilibuddy.shared.sync.SyncNextTask
import com.accountabilibuddy.shared.sync.SyncParked
import com.accountabilibuddy.shared.sync.SyncAutoSavedItem
import com.accountabilibuddy.shared.sync.PastSettleEdits
import com.accountabilibuddy.shared.sync.SyncPastSettled
import com.accountabilibuddy.shared.sync.SyncTaskHistoryEntry
import com.accountabilibuddy.shared.sync.SyncHistoryTask
import com.accountabilibuddy.shared.sync.SyncHandover
import com.accountabilibuddy.shared.sync.SyncHandoverAck
import com.accountabilibuddy.shared.sync.SyncKeptPast
import com.accountabilibuddy.shared.sync.TaskLabels
import com.accountabilibuddy.shared.tasks.ArchiveReceivedDevice
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.contentOrNull
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.time.Instant
import java.time.temporal.ChronoUnit
import java.util.UUID
import com.accountabilibuddy.companion.tasks.PhoneTaskStore
import com.accountabilibuddy.shared.tasks.OldTaskFields
import com.accountabilibuddy.shared.tasks.OldTaskList
import com.accountabilibuddy.shared.tasks.TaskFacadeLists
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskLocalEdits
import com.accountabilibuddy.shared.tasks.TaskModelStartup

/**
 * The lists the user's devices share (ADR 48, 50): the next session's tasks, the come-backs (the parked thoughts) and the
 * Later list, each item with its edit time and, once deleted, a tombstone kept 30 days. The lists go in this phone's sync file and other
 * devices' lists merge in item by item ([SharedListMerge], the same function the PC runs). Kept in the app's private
 * prefs, sealed with a key in the Android Keystore ([TokenVault]), never logged: task and thought text is only in
 * sealed files and sealed here, and so is a task's note (ADR 50). The running session's checklist is not one of these
 * lists; it belongs to its PC. The phone edits all three lists, the Later list included: an edit changes only the fields it
 * names (the items are copied, never rebuilt, so a field this phone has no screen for, like a planned day part, is carried
 * on as it came), and the newer item it stamps reaches the PC. Items other devices have are never dropped when passing on.
 *
 * A change reads the clock once, and each item it touches is stamped with [SharedListMerge.nextStamp] (that time, or one
 * tick past the item's own newest time), so it always outranks what it changed. A task's position only means something
 * among the tasks of its category (the PC groups its checklist by category).
 *
 * R7.2 (ADR 76): once the one task list is the source ([attach], [PhoneTaskStore.isActive]) the four task lists here (next session,
 * come-backs, Later, Auto-saved) are views of it ([TaskFacadeLists]) and every change to them goes into it as stamped edits
 * ([TaskLocalEdits]); moves between them keep the task ([TaskLocalEdits.move]). Their old stored copies are kept as they were (never
 * written again), as the way back. Other devices' old fields are no longer merged in (Q583: only a file's `tasks` part is). ADR 72's
 * other parts (history, settle records, kept past tasks, receipts) stay here as before.
 */
class SharedListsStore(context: Context, private val now: () -> Instant = { Instant.now().truncatedTo(ChronoUnit.MILLIS) }) {
    private val prefs = context.getSharedPreferences("shared_lists", Context.MODE_PRIVATE)
    private var all = read()

    // The four task lists exactly as stored when the task list took over (written back unchanged, never pruned again).
    private var frozen: Lists? = null
    private var model: PhoneTaskStore? = null
    private val modelActive: Boolean get() = model?.isActive == true

    // R7.2 review item 2: the task list can't be read: the four lists show empty, take no edits and go nowhere (the stored copies,
    // stale since the migration, are not used).
    private val modelClosed: Boolean get() = model?.isClosed == true
    private val _nextTasks = MutableStateFlow(liveNextTasks())
    private val _parked = MutableStateFlow(liveParked())
    private val _later = MutableStateFlow(liveLater())
    private val _autoSaved = MutableStateFlow(liveAutoSaved())
    private val _unfinished = MutableStateFlow(unfinishedNow())
    private val _archive = MutableStateFlow(archiveNow())

    /** ADR 72: the shared Auto-saved list, live items, newest first. */
    val autoSaved: StateFlow<List<SyncAutoSavedItem>> = _autoSaved.asStateFlow()

    /** ADR 72: the PCs' tasks their past sessions left open (last two weeks), not settled, and the ones kept here; newest first. */
    val unfinished: StateFlow<List<PastTaskRow>> = _unfinished.asStateFlow()

    /**
     * AR1c: what the Archive builds its tasks from besides the task list: the other PCs' histories (each as its last file carried it, inside its
     * 90 days), the past tasks kept here and every device's settle records. A new value whenever one of them changes.
     */
    val archive: StateFlow<ArchiveSources> = _archive.asStateFlow()

    /** This phone's id in its sync group (ADR 72's keeper rule); null while it isn't in one. Set by the app. */
    var selfId: () -> String? = { null }

    /** The next session's tasks, in list order. */
    val nextTasks: StateFlow<List<SyncNextTask>> = _nextTasks.asStateFlow()

    /** The come-backs, open and recently ticked, in list order. */
    val parked: StateFlow<List<SyncParked>> = _parked.asStateFlow()

    /** The Later list (ADR 50), live items in list order: the open ones and the ticked-off ones ([SyncLaterItem.doneAt], kept 90 days). */
    val later: StateFlow<List<SyncLaterItem>> = _later.asStateFlow()

    /** Set by the app: the user changed a list here, so it goes to the other devices soon. */
    var onChanged: () -> Unit = {}

    /** Set by the app: the current colour of a category (ADR 62); an item moved between lists takes it, not the one it stored. */
    var colorOf: (name: String?, stored: String?) -> String? = { _, stored -> stored }

    /** How many live items hold category [name] (any case): what a delete would take it off. */
    @Synchronized
    fun countInCategory(name: String): Int {
        fun held(category: String?) = category.equals(name, ignoreCase = true)
        return all.nextTasks.count { it.deletedAt == null && held(it.category) } +
            all.later.count { it.deletedAt == null && held(it.category) } +
            all.parked.count { it.deletedAt == null && held(it.category) }
    }

    /**
     * Carries a renamed or deleted category ([newName] null) to the items in it, as ordinary stamped edits, the same as the PC does. A limit,
     * also the PC's: merge works on whole items, so such an edit can win over an older edit made on a device that hasn't synced yet.
     */
    fun relabelCategory(oldName: String, newName: String?, newColor: String?) {
        // R7.2: tasks hold their label's id, so a rename or delete of the label (TaskCategoriesStore) already reaches them.
        if (!modelActive) relabelCategoryOld(oldName, newName, newColor)
    }

    private fun relabelCategoryOld(oldName: String, newName: String?, newColor: String?) = edit { lists, time ->
        fun held(category: String?) = category.equals(oldName, ignoreCase = true)
        lists.copy(
            nextTasks = lists.nextTasks.map { if (it.deletedAt == null && held(it.category)) it.copy(category = newName, categoryColor = newColor, editedAt = SharedListMerge.nextStamp(time, it)) else it },
            later = lists.later.map { if (it.deletedAt == null && held(it.category)) it.copy(category = newName, categoryColor = newColor, editedAt = SharedListMerge.nextStamp(time, it)) else it },
            parked = lists.parked.map { if (it.deletedAt == null && held(it.category)) it.copy(category = newName, categoryColor = newColor, editedAt = SharedListMerge.nextStamp(time, it)) else it },
        )
    }

    /** R7.2: the task list is the source from now on (when it is active): the four lists become its views. */
    fun attach(store: PhoneTaskStore) {
        model = store
        store.addListener { refreshFromModel() }
        refreshFromModel()
    }

    /** The old lists as stored (tombstones and local items included), for the one-time migration. */
    @Synchronized
    fun storedForMigration(): OldTaskFields {
        val held = frozen ?: all
        return OldTaskFields(held.nextTasks, held.later, held.parked, emptyList(), emptyList(), held.autoSaved)
    }

    /** The next session's list for this phone's sync file: its tasks and the tombstones of the last 30 days. */
    @Synchronized
    fun nextTasksForSync(): List<SyncNextTask> {
        model?.fileParts()?.let { return it.first.nextTasks }
        val keptFrom = now().minus(SharedListMerge.TOMBSTONES_KEPT)
        return SharedListMerge.forFile(all.nextTasks.filter { it.deletedAt?.isAfter(keptFrom) ?: true || it.localTo != null })
    }

    /** The Later list for this phone's sync file (ADR 50): its items and the tombstones of the last 30 days. */
    @Synchronized
    fun laterForSync(): List<SyncLaterItem> {
        model?.fileParts()?.let { return it.first.later }
        val keptFrom = now().minus(SharedListMerge.TOMBSTONES_KEPT)
        return SharedListMerge.forFile(all.later.filter { it.deletedAt?.isAfter(keptFrom) ?: true || it.localTo != null })
    }

    /** The come-backs for this phone's sync file: the open and recently ticked ones, and the tombstones of the last 30 days. */
    @Synchronized
    fun parkedForSync(): List<SyncParked> {
        model?.fileParts()?.let { return it.first.parked }
        val keptFrom = now().minus(SharedListMerge.TOMBSTONES_KEPT)
        return SharedListMerge.forFile(all.parked.filter { item -> (item.deletedAt?.isAfter(keptFrom) ?: item.doneAt?.isAfter(keptFrom) ?: true) || item.localTo != null })
    }

    /**
     * Takes other devices' lists in (a list a file doesn't carry is left alone). Item by item the newest edit wins and
     * a newer tombstone removes an item.
     *
     * When [knowsDetails] is false (the file has no `later` list, so its writer predates ADR 50's fields and can't clear
     * them), a task that comes without an estimate, note or planned part takes them from this phone's copy of the same
     * task before merging.
     *
     * @return True when a list changed.
     */
    @Synchronized
    fun receive(nextTasks: List<SyncNextTask>?, parked: List<SyncParked>?, later: List<SyncLaterItem>? = null, knowsDetails: Boolean = true): Boolean {
        // R7.2 (Q583): with the task list the source, other devices' old fields are not read in (their `tasks` part is).
        if (modelActive || modelClosed) return false
        val time = now()
        val incoming = if (knowsDetails) nextTasks else nextTasks?.map { task ->
            val own = all.nextTasks.firstOrNull { it.id == task.id && it.deletedAt == null }
            if (task.deletedAt != null || own == null) {
                task
            } else {
                task.copy(
                    estimateMinutes = task.estimateMinutes ?: own.estimateMinutes,
                    note = task.note ?: own.note,
                    plannedFor = task.plannedFor ?: own.plannedFor,
                )
            }
        }
        val me = selfId()
        val mergedTasks = incoming?.let { SharedListMerge.merge(all.nextTasks, it, time, me) } ?: all.nextTasks
        val mergedParked = parked?.let { SharedListMerge.merge(all.parked, it, time, me) } ?: all.parked
        val mergedLater = later?.let { SharedListMerge.merge(all.later, it, time, me) } ?: all.later
        if (mergedTasks == SharedListMerge.merge(all.nextTasks, emptyList(), time, me) &&
            mergedParked == SharedListMerge.merge(all.parked, emptyList(), time, me) &&
            mergedLater == SharedListMerge.merge(all.later, emptyList(), time, me)
        ) {
            return false
        }

        save(all.copy(nextTasks = mergedTasks, parked = mergedParked, later = mergedLater), time)
        return true
    }

    /** Adds a come-back at the end of the list; a blank line adds nothing. */
    fun addParked(text: String) {
        // ADR 72: "##word" in the typed text is a label.
        val (typed, labels) = TaskLabels.parse(text.trim())
        val trimmed = typed.trim()
        if (trimmed.isEmpty()) {
            return
        }

        edit { lists, time ->
            val last = lists.parked.maxOfOrNull { it.position }
            lists.copy(parked = lists.parked + SyncParked(newId(), trimmed, time, null, time, null, SharedListMerge.positionBetween(last, null), labels = labels))
        }
    }

    /** Ticks a come-back: it leaves the open list, keeping when it was ticked. */
    fun tickParked(id: String) = changeParked(id) { item, time -> item.copy(doneAt = time, editedAt = SharedListMerge.nextStamp(time, item)) }

    /** Opens a ticked come-back again (the Archive's tick box): its done time goes with a newer stamp. */
    fun untickParked(id: String) = changeParked(id) { item, time ->
        if (item.doneAt == null) item else item.copy(doneAt = null, editedAt = SharedListMerge.nextStamp(time, item))
    }

    /** Changes a come-back's text; a blank text changes nothing. */
    fun editParked(id: String, text: String) {
        val trimmed = text.trim()
        if (trimmed.isNotEmpty()) {
            changeParked(id) { item, time -> item.copy(text = trimmed, editedAt = SharedListMerge.nextStamp(time, item)) }
        }
    }

    /** Deletes a come-back: its text goes at once and a tombstone tells the other devices. */
    fun deleteParked(id: String) = changeParked(id) { item, time -> item.deleted(SharedListMerge.nextStamp(time, item)) }

    /** Adds a task to the end of its category's tasks; a blank title adds nothing. The three-tasks prompt is the screen's. */
    fun addNextTask(title: String, category: String? = null, categoryColor: String? = null): String? {
        val (typed, labels) = TaskLabels.parse(title.trim())
        val trimmed = typed.trim()
        if (trimmed.isEmpty()) {
            return null
        }

        val id = newId()
        edit { lists, time ->
            val last = lists.nextTasks.filter { it.deletedAt == null && it.category == category }.maxOfOrNull { it.position }
            lists.copy(nextTasks = lists.nextTasks + SyncNextTask(id, trimmed, null, category, categoryColor, null, time, null, SharedListMerge.positionBetween(last, null), labels = labels))
        }
        return id
    }

    /**
     * Changes a task's title and category, and its steps when [steps] is given (null leaves them; a step with no id is a
     * new one, a blank step is dropped); a blank title changes nothing. A task put in another category goes to the end of
     * that category's tasks.
     */
    fun editNextTask(id: String, title: String, category: String?, categoryColor: String?, steps: List<SessionTaskItem>? = null) {
        val trimmed = title.trim()
        if (trimmed.isEmpty()) {
            return
        }

        edit { lists, time ->
            val task = lists.nextTasks.firstOrNull { it.id == id && it.deletedAt == null } ?: return@edit lists
            val position = if (task.category == category) {
                task.position
            } else {
                SharedListMerge.positionBetween(lists.nextTasks.filter { it.deletedAt == null && it.category == category }.maxOfOrNull { it.position }, null)
            }
            val newSteps = steps?.filter { it.title.isNotBlank() }?.map { step -> step.copy(id = step.id ?: newId(), title = step.title.trim()) }
            lists.replace(task, task.copy(
                title = trimmed, category = category, categoryColor = categoryColor, position = position,
                subtasks = if (newSteps == null) task.subtasks else newSteps.ifEmpty { null },
                editedAt = SharedListMerge.nextStamp(time, task),
            ))
        }
    }

    /** Ticks or unticks a task (its steps follow, as on the PC). */
    fun toggleNextTask(id: String) = changeNextTask(id) { task, time ->
        val doneAt = if (task.isDone) null else time
        task.copy(
            doneAt = doneAt,
            subtasks = task.subtasks?.map { step -> step.copy(doneAt = if (doneAt == null) null else step.doneAt ?: doneAt) },
            editedAt = SharedListMerge.nextStamp(time, task),
        )
    }

    /** Deletes a task: its text goes at once and a tombstone tells the other devices. */
    fun deleteNextTask(id: String) = changeNextTask(id) { task, time -> task.deleted(SharedListMerge.nextStamp(time, task)) }

    /**
     * Moves a task to [index] among its category's tasks (0 = first); only that task is edited, it takes a position
     * between its new neighbours. When two positions get too close the category's tasks are numbered 1, 2, 3 again.
     */
    fun moveNextTask(id: String, index: Int) = edit { lists, time ->
        val moving = lists.nextTasks.firstOrNull { it.id == id && it.deletedAt == null } ?: return@edit lists
        val others = lists.nextTasks.filter { it.deletedAt == null && it.category == moving.category && it.id != id }.sortedBy { it.position }
        val at = index.coerceIn(0, others.size)
        val before = others.getOrNull(at - 1)?.position
        val after = others.getOrNull(at)?.position
        if (before != null && after != null && after - before < MIN_POSITION_GAP) {
            val ordered = others.toMutableList().apply { add(at, moving) }
            val numbered = ordered.mapIndexed { number, task -> task.id to (number + 1).toDouble() }.toMap()
            lists.copy(nextTasks = lists.nextTasks.map { task ->
                val position = numbered[task.id]
                if (position == null || position == task.position) task else task.copy(position = position, editedAt = SharedListMerge.nextStamp(time, task))
            })
        } else {
            lists.replace(moving, moving.copy(position = SharedListMerge.positionBetween(before, after), editedAt = SharedListMerge.nextStamp(time, moving)))
        }
    }

    /** Puts a step on a task. */
    fun addNextSubtask(taskId: String, title: String) {
        val trimmed = title.trim()
        if (trimmed.isNotEmpty()) {
            changeNextTask(taskId) { task, time ->
                task.copy(
                    doneAt = null,
                    subtasks = task.subtasks.orEmpty() + SessionTaskItem(newId(), trimmed, null, emptyList()),
                    editedAt = SharedListMerge.nextStamp(time, task),
                )
            }
        }
    }

    /**
     * Changes a next-session task through [change] (its fields as a whole: estimate, note, steps, title), stamping it once; a blank
     * title or no change does nothing. The change gets the live task and returns the edited copy. [category] (name, colour), when
     * given, moves the task to that category in the same edit.
     */
    fun updateNextTask(id: String, category: Pair<String?, String?>? = null, change: (SyncNextTask) -> SyncNextTask) = edit { lists, time ->
        val task = lists.nextTasks.firstOrNull { it.id == id && it.deletedAt == null } ?: return@edit lists
        var changed = change(task)
        if (category != null && category.first != task.category) {
            // A task put in another category goes to the end of that category's tasks, in the same single edit.
            val last = lists.nextTasks.filter { it.deletedAt == null && it.category == category.first }.maxOfOrNull { it.position }
            changed = changed.copy(category = category.first, categoryColor = category.second, position = SharedListMerge.positionBetween(last, null))
        }

        if (changed.title.isBlank() || changed == task) lists else lists.replace(task, changed.copy(editedAt = SharedListMerge.nextStamp(time, task)))
    }

    /** Moves a come-back to the end of the next session's tasks (keeping its category) and ticks it, as "Add to Today" does on the PC. */
    fun moveParkedToNext(id: String): String? {
        if (modelActive) return moveInModel(id, OldTaskList.NEXT_TASKS)
        var newTaskId: String? = null
        edit { lists, time ->
            val parked = lists.parked.firstOrNull { it.id == id && it.deletedAt == null && it.doneAt == null } ?: return@edit lists
            val last = lists.nextTasks.filter { it.deletedAt == null && it.category == parked.category }.maxOfOrNull { it.position }
            val task = SyncNextTask(
                newId(), parked.text, null, parked.category, colorOf(parked.category, parked.categoryColor), null, time, null, SharedListMerge.positionBetween(last, null),
            )
            newTaskId = task.id
            lists.copy(
                nextTasks = lists.nextTasks + task,
                parked = lists.parked.map { if (it.id == id) it.copy(doneAt = time, editedAt = SharedListMerge.nextStamp(time, it)) else it },
            )
        }
        return newTaskId
    }

    // ---- The Later list (ADR 50) ----

    /** Adds an item at the end of the Later list; a blank title adds nothing. Returns its id. */
    fun addLater(title: String, category: String? = null, categoryColor: String? = null, dueOn: String? = null): String? {
        val (typed, labels) = TaskLabels.parse(title.trim())
        val trimmed = typed.trim()
        if (trimmed.isEmpty()) {
            return null
        }

        val id = newId()
        edit { lists, time ->
            val last = lists.later.filter { it.deletedAt == null }.maxOfOrNull { it.position }
            lists.copy(later = lists.later + SyncLaterItem(
                id, trimmed, null, category, categoryColor, null, null, dueOn, time, null, SharedListMerge.positionBetween(last, null), labels = labels,
            ))
        }
        return id
    }

    /**
     * Changes a Later item through [change] (its fields as a whole), stamping it once so it outranks what it changed on every
     * device; a blank title or no change does nothing. Fields the phone has no screen for are carried on by the copy.
     */
    fun updateLater(id: String, change: (SyncLaterItem) -> SyncLaterItem) = changeLater(id) { item, time ->
        val changed = change(item)
        if (changed.title.isBlank() || changed == item) item else changed.copy(editedAt = SharedListMerge.nextStamp(time, item))
    }

    /** Deletes a Later item: its text goes at once and a tombstone tells the other devices. Returns the item as it was, for Undo. */
    fun deleteLater(id: String): SyncLaterItem? {
        val held = synchronized(this) { all.later.firstOrNull { it.id == id && it.deletedAt == null } } ?: return null
        changeLater(id) { item, time -> item.deleted(SharedListMerge.nextStamp(time, item)) }
        return held
    }

    /**
     * Puts a deleted Later item back (Undo, or un-ticking it): the same id, position and fields as a newer edit that beats its
     * tombstone on every device. Nothing changes when the item is on the list already.
     */
    fun restoreLater(item: SyncLaterItem) = edit { lists, time ->
        val held = lists.later.firstOrNull { it.id == item.id }
        if (held != null && held.deletedAt == null) {
            return@edit lists
        }

        val back = item.copy(editedAt = SharedListMerge.nextStamp(time, held), deletedAt = null)
        lists.copy(later = if (held == null) lists.later + back else lists.later.map { if (it.id == item.id) back else it })
    }

    /**
     * Ticks a Later item off (ADR 50): it keeps its text and gets the done time as a normal stamped edit, so every device shows it as
     * done (no tombstone). The PC records it once in its done tasks.
     */
    fun tickLater(id: String) = changeLater(id) { item, time ->
        if (item.doneAt != null) item else item.copy(doneAt = time, editedAt = SharedListMerge.nextStamp(time, item))
    }

    /** Opens a ticked Later item again (Undo, Put back): its done time goes with a newer stamp. */
    fun untickLater(id: String) = changeLater(id) { item, time ->
        if (item.doneAt == null) item else item.copy(doneAt = null, editedAt = SharedListMerge.nextStamp(time, item))
    }

    /**
     * "Add to Today" for a Later item with no session running: it becomes the next session's task (its open steps, category,
     * estimate and note; its due date and plan go) and leaves the Later list. Returns the new task's id.
     */
    fun moveLaterToNext(id: String): String? {
        if (modelActive) return moveInModel(id, OldTaskList.NEXT_TASKS)
        var newTaskId: String? = null
        edit { lists, time ->
            val item = lists.later.firstOrNull { it.id == id && it.deletedAt == null && it.doneAt == null } ?: return@edit lists
            val last = lists.nextTasks.filter { it.deletedAt == null && it.category == item.category }.maxOfOrNull { it.position }
            val task = SyncNextTask(
                newId(), item.title, null, item.category, colorOf(item.category, item.categoryColor), TaskSteps.reopened(item.subtasks).ifEmpty { null }, time, null,
                SharedListMerge.positionBetween(last, null), item.estimateMinutes, item.note,
            )
            newTaskId = task.id
            lists.copy(
                nextTasks = lists.nextTasks + task,
                later = lists.later.map { if (it.id == id) item.deleted(SharedListMerge.nextStamp(time, item)) else it },
            )
        }
        return newTaskId
    }

    /** "Move to Later" for an open next-session task: it joins the Later list with its open steps, category, estimate and note. */
    fun moveNextToLater(id: String): String? {
        if (modelActive) return moveInModel(id, OldTaskList.LATER)
        var newItemId: String? = null
        edit { lists, time ->
            val task = lists.nextTasks.firstOrNull { it.id == id && it.deletedAt == null && !it.isDone } ?: return@edit lists
            val last = lists.later.filter { it.deletedAt == null }.maxOfOrNull { it.position }
            val item = SyncLaterItem(
                newId(), task.title, TaskSteps.open(task.subtasks).ifEmpty { null }, task.category, colorOf(task.category, task.categoryColor), task.estimateMinutes,
                task.note, null, time, null, SharedListMerge.positionBetween(last, null), task.plannedFor,
            )
            newItemId = item.id
            lists.copy(
                later = lists.later + item,
                nextTasks = lists.nextTasks.map { if (it.id == id) task.deleted(SharedListMerge.nextStamp(time, task)) else it },
            )
        }
        return newItemId
    }

    /** A come-back becomes a Later item (its text, category and colour) and leaves the come-backs. */
    fun moveParkedToLater(id: String): String? {
        if (modelActive) return moveInModel(id, OldTaskList.LATER)
        var newItemId: String? = null
        edit { lists, time ->
            val parked = lists.parked.firstOrNull { it.id == id && it.deletedAt == null } ?: return@edit lists
            val last = lists.later.filter { it.deletedAt == null }.maxOfOrNull { it.position }
            val item = SyncLaterItem(
                newId(), parked.text, null, parked.category, colorOf(parked.category, parked.categoryColor), null, null, null, time, null, SharedListMerge.positionBetween(last, null),
            )
            newItemId = item.id
            lists.copy(
                later = lists.later + item,
                parked = lists.parked.map { if (it.id == id) parked.deleted(SharedListMerge.nextStamp(time, parked)) else it },
            )
        }
        return newItemId
    }

    /** A Later item becomes a come-back (its title, with " (+N steps)", category and colour) and leaves the Later list. */
    fun moveLaterToParked(id: String): String? {
        if (modelActive) return moveInModel(id, OldTaskList.PARKED)
        var newParkedId: String? = null
        edit { lists, time ->
            val item = lists.later.firstOrNull { it.id == id && it.deletedAt == null && it.doneAt == null } ?: return@edit lists
            val steps = TaskSteps.open(item.subtasks).size
            val text = if (steps > 0) "${item.title} (+$steps ${if (steps == 1) "step" else "steps"})" else item.title
            val last = lists.parked.maxOfOrNull { it.position }
            val parked = SyncParked(newId(), text, time, null, time, null, SharedListMerge.positionBetween(last, null), item.category, colorOf(item.category, item.categoryColor))
            newParkedId = parked.id
            lists.copy(
                parked = lists.parked + parked,
                later = lists.later.map { if (it.id == id) item.deleted(SharedListMerge.nextStamp(time, item)) else it },
            )
        }
        return newParkedId
    }

    private fun changeLater(id: String, change: (SyncLaterItem, Instant) -> SyncLaterItem) = edit { lists, time ->
        lists.copy(later = lists.later.map { if (it.id == id && it.deletedAt == null) change(it, time) else it })
    }

    private fun changeParked(id: String, change: (SyncParked, Instant) -> SyncParked) = edit { lists, time ->
        lists.copy(parked = lists.parked.map { if (it.id == id && it.deletedAt == null) change(it, time) else it })
    }

    private fun changeNextTask(id: String, change: (SyncNextTask, Instant) -> SyncNextTask) = edit { lists, time ->
        lists.copy(nextTasks = lists.nextTasks.map { if (it.id == id && it.deletedAt == null) change(it, time) else it })
    }

    private fun Lists.replace(old: SyncNextTask, new: SyncNextTask) = copy(nextTasks = nextTasks.map { if (it === old) new else it })

    // ---- ADR 72: every task list syncs ----

    /**
     * Takes ADR 72's parts in from another device's file: a PC's task history (replacing its older copy), the settle records,
     * the Auto-saved list, and the items handed to this phone to keep (their receipts go out in this phone's file).
     * @return True when something changed.
     */
    @Synchronized
    fun receiveExtras(
        deviceId: String, name: String, isPc: Boolean, history: List<SyncTaskHistoryEntry>?, settled: List<SyncPastSettled>?,
        autoSaved: List<SyncAutoSavedItem>?, handovers: List<SyncHandover>?,
    ): Boolean {
        val time = now()
        val me = selfId()
        var lists = all
        if (isPc && history != null) {
            // The PC's rule on receipt (ReceivedTaskHistoryStore): an entry with no time, or older than the 90 days history is kept, is not kept.
            val inside = SyncTaskHistoryEntry.cut(history, time)
            lists = lists.copy(history = lists.history.filterNot { it.deviceId.equals(deviceId, ignoreCase = true) } + DeviceHistory(deviceId, name, inside))
        }

        settled?.let { lists = lists.copy(pastSettled = SharedListMerge.merge(lists.pastSettled, it, time)) }
        // R7.2: Auto-saved is part of the task list (its `tasks` part carries it).
        if (!modelActive && !modelClosed) autoSaved?.let { lists = lists.copy(autoSaved = SharedListMerge.merge(lists.autoSaved, it, time, me)) }
        var adopted = false
        if (me != null) {
            for (handover in handovers.orEmpty().filter { it.to.equals(me, ignoreCase = true) }) {
                if (lists.receipts.any { it.id.equals(handover.id, ignoreCase = true) && !it.editedAt.isBefore(handover.editedAt) }) continue
                // A closed task list can't take it: no receipt, so the sender keeps offering it until the list opens again.
                if (modelClosed && handover.list != SyncHandover.HISTORY) continue
                lists = if (modelActive && handover.list != SyncHandover.HISTORY) {
                    adopted = adoptInModel(handover, me) || adopted
                    lists
                } else {
                    adopt(lists, handover, me)
                }
                lists = lists.copy(receipts = lists.receipts.filterNot { it.id.equals(handover.id, ignoreCase = true) } + SyncHandoverAck(handover.id, handover.editedAt))
            }
        }

        if (lists == all) {
            return adopted
        }

        save(lists, time)
        return true
    }

    /** The settle records for this phone's file. */
    @Synchronized
    fun pastSettledForSync(): List<SyncPastSettled> = all.pastSettled

    /** The Auto-saved list for this phone's file: tombstones included, local items as stubs. */
    @Synchronized
    fun autoSavedForSync(): List<SyncAutoSavedItem> = model?.fileParts()?.first?.autoSaved ?: SharedListMerge.forFile(all.autoSaved)

    /** This phone's receipts for the handovers it took in (30 days). */
    @Synchronized
    fun receiptsForSync(): List<SyncHandoverAck> = all.receipts

    /** Ticks a past task (another PC's, settled on every device; one kept here, here only). */
    fun tickPast(id: String) = edit { lists, time ->
        val kept = lists.kept.firstOrNull { it.task.id.equals(id, ignoreCase = true) }
        if (kept != null) {
            lists.copy(kept = lists.kept.map { if (it === kept) it.copy(task = it.task.copy(doneAt = time), editedAt = time) else it })
        } else {
            settle(lists, id, SyncPastSettled.DONE, time)
        }
    }

    /**
     * Opens a past task again (the Archive's tick box): another PC's, through a settle record (Open), so it is open on every device; one kept
     * here, here only. Never a raw edit of the owner's history.
     */
    fun untickPast(id: String) = edit { lists, time ->
        val kept = lists.kept.firstOrNull { it.task.id.equals(id, ignoreCase = true) }
        if (kept != null) {
            lists.copy(kept = lists.kept.map { if (it === kept) it.copy(task = it.task.copy(doneAt = null), editedAt = time) else it })
        } else {
            settle(lists, id, SyncPastSettled.OPEN, time)
        }
    }

    /**
     * Restores a past task deleted anywhere (Recently deleted › Restore): a settle record of [state] (Done or Open, what [ArchiveBuilder.restoreStateOf] says)
     * stamped newer than the delete, so the owning PC puts it back and every device lists it again. Never a raw restore; nothing but those two states is written.
     */
    fun restorePast(id: String, state: String) = edit { lists, time -> lists.copy(pastSettled = PastSettleEdits.restore(lists.pastSettled, id, state, time)) }

    /** Moves a past task to Later (its open steps, category, estimate, note and labels): it leaves Unfinished on every device. */
    fun movePastToLater(id: String) = edit { lists, time ->
        val row = unfinishedNow().firstOrNull { it.task.id.equals(id, ignoreCase = true) } ?: return@edit lists
        val task = row.task
        val last = lists.later.maxOfOrNull { it.position }
        val item = SyncLaterItem(
            newId(), task.title, task.subtasks.filter { !it.isDone }.ifEmpty { null }, task.category, task.categoryColor, task.estimateMinutes, task.note, null,
            time, null, SharedListMerge.positionBetween(last, null), labels = task.labels,
        )
        val withItem = lists.copy(later = lists.later + item)
        if (row.keptHere) withItem.copy(kept = withItem.kept.filterNot { it.task.id.equals(id, ignoreCase = true) }) else settle(withItem, id, SyncPastSettled.MOVED, time)
    }

    /** Deletes a past task (another PC's: on every device, into its PC's Recently deleted; one kept here: here only). */
    fun deletePast(id: String) = edit { lists, time ->
        if (lists.kept.any { it.task.id.equals(id, ignoreCase = true) }) lists.copy(kept = lists.kept.filterNot { it.task.id.equals(id, ignoreCase = true) })
        else settle(lists, id, SyncPastSettled.DELETED, time)
    }

    /** Moves an Auto-saved item to Later; it leaves Auto-saved on every device. */
    fun autoSavedToLater(id: String) {
        if (modelActive) {
            moveInModel(id, OldTaskList.LATER)
            return
        }

        autoSavedToLaterOld(id)
    }

    private fun autoSavedToLaterOld(id: String) = edit { lists, time ->
        val held = lists.autoSaved.firstOrNull { it.id == id && it.deletedAt == null } ?: return@edit lists
        val last = lists.later.maxOfOrNull { it.position }
        val item = SyncLaterItem(newId(), held.title, null, held.category, held.categoryColor, null, null, null, time, null, SharedListMerge.positionBetween(last, null), labels = held.labels)
        lists.copy(later = lists.later + item, autoSaved = lists.autoSaved.map { if (it === held) it.deleted(SharedListMerge.nextStamp(time, it)) else it })
    }

    /** Deletes an Auto-saved item on every device. */
    fun deleteAutoSaved(id: String) = edit { lists, time ->
        lists.copy(autoSaved = lists.autoSaved.map { if (it.id == id && it.deletedAt == null) it.deleted(SharedListMerge.nextStamp(time, it)) else it })
    }

    /** The title of an Auto-saved item, for Add to Today (the caller adds the task, then deletes the item). */
    @Synchronized
    fun autoSavedTitle(id: String): String? = all.autoSaved.firstOrNull { it.id == id && it.deletedAt == null }?.title

    /** Every label on this phone's lists (ADR 72). */
    @Synchronized
    fun allLabels(): List<String> = TaskLabels.allOf(
        all.nextTasks.filter { it.deletedAt == null }.map { it.labels } + all.later.filter { it.deletedAt == null }.map { it.labels } +
            all.parked.filter { it.deletedAt == null }.map { it.labels } + all.autoSaved.filter { it.deletedAt == null }.map { it.labels },
    )

    private fun settle(lists: Lists, id: String, state: String, time: Instant): Lists =
        lists.copy(pastSettled = PastSettleEdits.settle(lists.pastSettled, id, state, time))

    // An item handed to this phone to keep: it replaces any stub or older copy; a past task joins the kept ones.
    private fun adopt(lists: Lists, handover: SyncHandover, me: String): Lists = when (handover.list) {
        SyncHandover.NEXT_TASKS -> handover.nextTask?.let { item -> lists.copy(nextTasks = lists.nextTasks.filterNot { it.id.equals(item.id, ignoreCase = true) } + item.copy(deletedAt = null, localTo = me)) } ?: lists
        SyncHandover.LATER -> handover.laterItem?.let { item -> lists.copy(later = lists.later.filterNot { it.id.equals(item.id, ignoreCase = true) } + item.copy(deletedAt = null, localTo = me)) } ?: lists
        SyncHandover.PARKED -> handover.parkedItem?.let { item -> lists.copy(parked = lists.parked.filterNot { it.id.equals(item.id, ignoreCase = true) } + item.copy(deletedAt = null, localTo = me)) } ?: lists
        SyncHandover.AUTO_SAVED -> handover.autoSavedItem?.let { item -> lists.copy(autoSaved = lists.autoSaved.filterNot { it.id.equals(item.id, ignoreCase = true) } + item.copy(deletedAt = null, localTo = me)) } ?: lists
        SyncHandover.HISTORY -> handover.pastTask?.let { past -> lists.copy(kept = lists.kept.filterNot { it.task.id.equals(past.task.id, ignoreCase = true) } + past.copy(task = past.task.copy(localTo = me))) } ?: lists
        else -> lists
    }

    // The clock is read once per change, so everything one change touches is stamped from the same moment.
    private fun edit(change: (Lists, Instant) -> Lists) {
        val changed = synchronized(this) {
            val time = now()
            val before = all
            val changedLists = change(all, time)
            // A closed task list takes no edits (R7.2 review item 2): only ADR 72's own parts change.
            val updated = if (modelClosed) changedLists.copy(nextTasks = before.nextTasks, parked = before.parked, later = before.later, autoSaved = before.autoSaved) else changedLists
            if (updated == all) {
                false
            } else {
                val store = model
                if (store != null && store.isActive) {
                    // R7.2: the changed items go into the task list as this phone's stamped edits; ADR 72's own parts are kept here.
                    val changedItems = TaskModelStartup.facadeChanges(before.oldFields(), updated.oldFields())
                    store.change { lists, self, at -> TaskLocalEdits.apply(lists, changedItems, self, at, store.zone(), removed = store.removed()) }
                    save(updated, time)
                    refreshFromModelLocked()
                } else {
                    save(updated, time)
                }
                true
            }
        }
        if (changed) {
            onChanged()
        }
    }

    // Kept: the open and the ticked ones of the last 90 days (as on the PC), and tombstones of the last 30.
    private fun save(lists: Lists, time: Instant) {
        val tombstonesFrom = time.minus(SharedListMerge.TOMBSTONES_KEPT)
        val doneFrom = time.minus(java.time.Duration.ofDays(90))
        all = Lists(
            lists.nextTasks.filter { it.deletedAt?.isAfter(tombstonesFrom) ?: true || it.localTo != null }.sortedBy { it.position },
            lists.parked.filter { item -> (item.deletedAt?.isAfter(tombstonesFrom) ?: item.doneAt?.isAfter(doneFrom) ?: true) || item.localTo != null }.sortedBy { it.position },
            // A ticked Later item older than the PC's records of done tasks goes the normal way: a tombstone with no text.
            lists.later
                .map { item -> if (item.deletedAt == null && item.doneAt?.isBefore(doneFrom) == true) item.deleted(SharedListMerge.nextStamp(time, item)) else item }
                .filter { it.deletedAt?.isAfter(tombstonesFrom) ?: true || it.localTo != null }.sortedBy { it.position },
            lists.autoSaved.filter { item -> if (item.deletedAt != null) item.deletedAt!!.isAfter(tombstonesFrom) || item.localTo != null else !item.isExpired(time) }.sortedBy { it.position },
            lists.pastSettled.filter { it.editedAt.isAfter(time.minus(SyncPastSettled.KEPT)) },
            lists.history.map { device -> device.copy(entries = device.entries.filter { entry -> entry.at?.isAfter(time.minus(SyncPastSettled.KEPT)) ?: false }) },
            lists.kept.filter { (it.sessionStart ?: it.editedAt).isAfter(time.minus(SyncPastSettled.KEPT)) },
            lists.receipts.filter { it.editedAt.isAfter(time.minus(SyncHandover.KEPT)) },
        )
        // R7.2: with the task list the source, the four lists' stored copies stay exactly as they were (the way back).
        val stored = frozen?.let { all.copy(nextTasks = it.nextTasks, parked = it.parked, later = it.later, autoSaved = it.autoSaved) } ?: all
        prefs.edit().putString(KEY, TokenVault.seal(write(stored).toString(), ALIAS)).apply()
        _nextTasks.value = liveNextTasks()
        _parked.value = liveParked()
        _later.value = liveLater()
        _autoSaved.value = liveAutoSaved()
        _unfinished.value = unfinishedNow()
        _archive.value = archiveNow()
        // R7.2: the four lists shown are always the task list's views.
        if (modelActive || modelClosed) refreshFromModelLocked()
    }

    private fun archiveNow() = ArchiveSources(
        all.history.map { ArchiveReceivedDevice(it.deviceId, it.name, it.entries) },
        all.kept, all.pastSettled,
    )

    private fun liveNextTasks() = all.nextTasks.filter { it.deletedAt == null }

    private fun liveParked() = all.parked.filter { it.deletedAt == null }

    private fun liveLater() = all.later.filter { it.deletedAt == null }

    private fun liveAutoSaved() = all.autoSaved.filter { it.deletedAt == null && !it.isExpired(now()) }.sortedByDescending { it.savedAt }

    private fun unfinishedNow(): List<PastTaskRow> {
        val since = now().minus(java.time.Duration.ofDays(14))
        // R7.2 (Q170, Q174): a past session's open task that is live in the task list again ("End and mark the rest unfinished" sent it
        // back to Queued) is used by that list: it isn't offered from past sessions too.
        val settled = all.pastSettled.filter { it.deletedAt == null && it.hidesTask }.map { it.id.lowercase() }.toSet() + liveModelIds()
        val fromPcs = all.history.flatMap { device ->
            device.entries.filter { !it.outside && it.sessionStart?.isAfter(since) == true }.flatMap { entry ->
                entry.tasks.filter { !it.isDone && it.title.isNotEmpty() && it.localTo == null && it.id.lowercase() !in settled }
                    .map { PastTaskRow(it, entry.sessionStart!!, device.name, keptHere = false) }
            }
        }
        val kept = all.kept.filter { !it.task.isDone && it.sessionStart != null }.map { PastTaskRow(it.task, it.sessionStart!!, it.sourceName, keptHere = true) }
        return (fromPcs + kept).sortedByDescending { it.sessionStart }
    }

    private fun newId(): String = UUID.randomUUID().toString()

    private fun liveModelIds(): Set<String> =
        model?.takeIf { it.isActive }?.lists?.value?.tasks?.filter { it.task.isLive }?.map { it.id.lowercase() }?.toSet() ?: emptySet()

    private fun Lists.oldFields() = OldTaskFields(nextTasks, later, parked, emptyList(), emptyList(), autoSaved)

    // A move between the lists that keeps the task (R7.2, Q52); returns its id (the same).
    private fun moveInModel(id: String, to: OldTaskList): String? {
        val store = model ?: return null
        val changed = store.change { lists, self, at -> TaskLocalEdits.move(lists, id, to, self, at, store.zone()) }
        if (changed) {
            refreshFromModel()
            onChanged()
        }
        return id.takeIf { changed }
    }

    // A task handed to this phone to keep (ADR 72), into the task list: it comes back over its stub, kept here.
    private fun adoptInModel(handover: SyncHandover, me: String): Boolean {
        val store = model ?: return false
        val none = OldTaskFields(emptyList(), emptyList(), emptyList(), emptyList(), emptyList(), emptyList())
        val changed = when (handover.list) {
            SyncHandover.NEXT_TASKS -> handover.nextTask?.let { none.copy(nextTasks = listOf(it.copy(deletedAt = null, localTo = me))) }
            SyncHandover.LATER -> handover.laterItem?.let { none.copy(later = listOf(it.copy(deletedAt = null, localTo = me))) }
            SyncHandover.PARKED -> handover.parkedItem?.let { none.copy(parked = listOf(it.copy(deletedAt = null, localTo = me))) }
            SyncHandover.AUTO_SAVED -> handover.autoSavedItem?.let { none.copy(autoSaved = listOf(it.copy(deletedAt = null, localTo = me))) }
            else -> null
        } ?: return false
        return store.change { lists, self, at -> TaskLocalEdits.apply(lists, changed, self, at, store.zone(), removed = store.removed()) }
    }

    private fun refreshFromModel() = synchronized(this) { refreshFromModelLocked() }

    // The four lists as the task list's views (only while it is the source); the stored copies are kept aside, unchanged.
    private fun refreshFromModelLocked() {
        val store = model ?: return
        if (!store.isActive && !store.isClosed) return
        if (frozen == null) frozen = all
        val lists = if (store.isClosed) TaskLists.EMPTY else store.lists.value
        val at = java.time.OffsetDateTime.ofInstant(now(), java.time.ZoneOffset.UTC)
        all = all.copy(
            nextTasks = TaskFacadeLists.draft(lists, at),
            parked = TaskFacadeLists.parked(lists, at),
            later = TaskFacadeLists.later(lists, at),
            autoSaved = TaskFacadeLists.autoSaved(lists, at),
        )
        _nextTasks.value = liveNextTasks()
        _parked.value = liveParked()
        _later.value = liveLater()
        _autoSaved.value = liveAutoSaved()
        _unfinished.value = unfinishedNow()
    }

    private fun write(lists: Lists): JsonObject = buildJsonObject {
        put("nextTasks", SyncFileJson.writeNextTasks(lists.nextTasks))
        put("parked", SyncFileJson.writeParked(lists.parked))
        put("later", SyncFileJson.writeLater(lists.later))
        put("autoSaved", SyncFileJson.writeAutoSaved(lists.autoSaved))
        put("pastSettled", SyncFileJson.writePastSettled(lists.pastSettled))
        put("history", JsonArray(lists.history.map { device -> buildJsonObject { put("deviceId", device.deviceId); put("name", device.name); put("entries", SyncFileJson.writeTaskHistory(device.entries)) } }))
        put("kept", SyncFileJson.writeHandovers(lists.kept.map { SyncHandover("00000000-0000-0000-0000-000000000000", it.task.id, SyncHandover.HISTORY, it.editedAt, it.editedAt, pastTask = it) }))
        put("receipts", JsonArray(lists.receipts.map { buildJsonObject { put("id", it.id); put("editedAt", it.editedAt.toString()) } }))
    }

    // Empty when nothing is stored or it can't be opened (the key goes with the app's data).
    private fun read(): Lists {
        val json = prefs.getString(KEY, null)?.let { TokenVault.open(it, ALIAS) }
            ?.let { runCatching { Json.parseToJsonElement(it) as JsonObject }.getOrNull() } ?: return Lists(emptyList(), emptyList(), emptyList())
        return Lists(
            SyncFileJson.readNextTasks(json["nextTasks"] as? JsonArray).orEmpty(),
            SyncFileJson.readParked(json["parked"] as? JsonArray).orEmpty(),
            SyncFileJson.readLater(json["later"] as? JsonArray).orEmpty(),
            SyncFileJson.readAutoSaved(json["autoSaved"] as? JsonArray).orEmpty(),
            SyncFileJson.readPastSettled(json["pastSettled"] as? JsonArray).orEmpty(),
            (json["history"] as? JsonArray).orEmpty().mapNotNull { element ->
                val device = element as? JsonObject ?: return@mapNotNull null
                val id = device["deviceId"]?.jsonPrimitive?.contentOrNull ?: return@mapNotNull null
                DeviceHistory(id, device["name"]?.jsonPrimitive?.contentOrNull ?: "", SyncFileJson.readTaskHistory(device["entries"]).orEmpty())
            },
            SyncFileJson.readHandovers(json["kept"]).orEmpty().mapNotNull { it.pastTask },
            (json["receipts"] as? JsonArray).orEmpty().mapNotNull { element ->
                val receipt = element as? JsonObject ?: return@mapNotNull null
                runCatching { SyncHandoverAck(receipt["id"]!!.jsonPrimitive.content, Instant.parse(receipt["editedAt"]!!.jsonPrimitive.content)) }.getOrNull()
            },
        )
    }

    private data class Lists(
        val nextTasks: List<SyncNextTask>,
        val parked: List<SyncParked>,
        val later: List<SyncLaterItem>,
        val autoSaved: List<SyncAutoSavedItem> = emptyList(),
        val pastSettled: List<SyncPastSettled> = emptyList(),
        val history: List<DeviceHistory> = emptyList(),
        val kept: List<SyncKeptPast> = emptyList(),
        val receipts: List<SyncHandoverAck> = emptyList(),
    )

    /** One PC's task history as its last file carried it (ADR 72). */
    private data class DeviceHistory(val deviceId: String, val name: String, val entries: List<SyncTaskHistoryEntry>)

    companion object {
        private const val KEY = "lists"
        private const val ALIAS = "accountabilibuddy-shared-lists"

        // Two positions closer than this are renumbered (halving a gap again and again runs out of precision).
        private const val MIN_POSITION_GAP = 1e-9
    }
}

/** What the Archive builds its tasks from besides the task list (AR1c): other PCs' histories, the past tasks kept on this phone and the settle records. */
data class ArchiveSources(
    val received: List<ArchiveReceivedDevice>,
    val kept: List<com.accountabilibuddy.shared.sync.SyncKeptPast>,
    val settled: List<SyncPastSettled>,
)
