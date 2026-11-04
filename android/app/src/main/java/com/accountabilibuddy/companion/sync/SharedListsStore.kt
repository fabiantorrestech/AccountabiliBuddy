package com.accountabilibuddy.companion.sync

import android.content.Context
import com.accountabilibuddy.shared.TokenVault
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SharedListMerge
import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.sync.SyncLaterItem
import com.accountabilibuddy.shared.sync.SyncNextTask
import com.accountabilibuddy.shared.sync.SyncParked
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
 */
class SharedListsStore(context: Context, private val now: () -> Instant = { Instant.now().truncatedTo(ChronoUnit.MILLIS) }) {
    private val prefs = context.getSharedPreferences("shared_lists", Context.MODE_PRIVATE)
    private var all = read()
    private val _nextTasks = MutableStateFlow(liveNextTasks())
    private val _parked = MutableStateFlow(liveParked())
    private val _later = MutableStateFlow(liveLater())

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
    fun relabelCategory(oldName: String, newName: String?, newColor: String?) = edit { lists, time ->
        fun held(category: String?) = category.equals(oldName, ignoreCase = true)
        lists.copy(
            nextTasks = lists.nextTasks.map { if (it.deletedAt == null && held(it.category)) it.copy(category = newName, categoryColor = newColor, editedAt = SharedListMerge.nextStamp(time, it)) else it },
            later = lists.later.map { if (it.deletedAt == null && held(it.category)) it.copy(category = newName, categoryColor = newColor, editedAt = SharedListMerge.nextStamp(time, it)) else it },
            parked = lists.parked.map { if (it.deletedAt == null && held(it.category)) it.copy(category = newName, categoryColor = newColor, editedAt = SharedListMerge.nextStamp(time, it)) else it },
        )
    }

    /** The next session's list for this phone's sync file: its tasks and the tombstones of the last 30 days. */
    @Synchronized
    fun nextTasksForSync(): List<SyncNextTask> {
        val keptFrom = now().minus(SharedListMerge.TOMBSTONES_KEPT)
        return all.nextTasks.filter { it.deletedAt?.isAfter(keptFrom) ?: true }
    }

    /** The Later list for this phone's sync file (ADR 50): its items and the tombstones of the last 30 days. */
    @Synchronized
    fun laterForSync(): List<SyncLaterItem> {
        val keptFrom = now().minus(SharedListMerge.TOMBSTONES_KEPT)
        return all.later.filter { it.deletedAt?.isAfter(keptFrom) ?: true }
    }

    /** The come-backs for this phone's sync file: the open and recently ticked ones, and the tombstones of the last 30 days. */
    @Synchronized
    fun parkedForSync(): List<SyncParked> {
        val keptFrom = now().minus(SharedListMerge.TOMBSTONES_KEPT)
        return all.parked.filter { item -> item.deletedAt?.isAfter(keptFrom) ?: item.doneAt?.isAfter(keptFrom) ?: true }
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
        val mergedTasks = incoming?.let { SharedListMerge.merge(all.nextTasks, it, time) } ?: all.nextTasks
        val mergedParked = parked?.let { SharedListMerge.merge(all.parked, it, time) } ?: all.parked
        val mergedLater = later?.let { SharedListMerge.merge(all.later, it, time) } ?: all.later
        if (mergedTasks == SharedListMerge.merge(all.nextTasks, emptyList(), time) &&
            mergedParked == SharedListMerge.merge(all.parked, emptyList(), time) &&
            mergedLater == SharedListMerge.merge(all.later, emptyList(), time)
        ) {
            return false
        }

        save(all.copy(nextTasks = mergedTasks, parked = mergedParked, later = mergedLater), time)
        return true
    }

    /** Adds a come-back at the end of the list; a blank line adds nothing. */
    fun addParked(text: String) {
        val trimmed = text.trim()
        if (trimmed.isEmpty()) {
            return
        }

        edit { lists, time ->
            val last = lists.parked.maxOfOrNull { it.position }
            lists.copy(parked = lists.parked + SyncParked(newId(), trimmed, time, null, time, null, SharedListMerge.positionBetween(last, null)))
        }
    }

    /** Ticks a come-back: it leaves the open list, keeping when it was ticked. */
    fun tickParked(id: String) = changeParked(id) { item, time -> item.copy(doneAt = time, editedAt = SharedListMerge.nextStamp(time, item)) }

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
        val trimmed = title.trim()
        if (trimmed.isEmpty()) {
            return null
        }

        val id = newId()
        edit { lists, time ->
            val last = lists.nextTasks.filter { it.deletedAt == null && it.category == category }.maxOfOrNull { it.position }
            lists.copy(nextTasks = lists.nextTasks + SyncNextTask(id, trimmed, null, category, categoryColor, null, time, null, SharedListMerge.positionBetween(last, null)))
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
        val trimmed = title.trim()
        if (trimmed.isEmpty()) {
            return null
        }

        val id = newId()
        edit { lists, time ->
            val last = lists.later.filter { it.deletedAt == null }.maxOfOrNull { it.position }
            lists.copy(later = lists.later + SyncLaterItem(
                id, trimmed, null, category, categoryColor, null, null, dueOn, time, null, SharedListMerge.positionBetween(last, null),
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

    // The clock is read once per change, so everything one change touches is stamped from the same moment.
    private fun edit(change: (Lists, Instant) -> Lists) {
        val changed = synchronized(this) {
            val time = now()
            val updated = change(all, time)
            if (updated == all) {
                false
            } else {
                save(updated, time)
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
            lists.nextTasks.filter { it.deletedAt?.isAfter(tombstonesFrom) ?: true }.sortedBy { it.position },
            lists.parked.filter { item -> item.deletedAt?.isAfter(tombstonesFrom) ?: item.doneAt?.isAfter(doneFrom) ?: true }.sortedBy { it.position },
            // A ticked Later item older than the PC's records of done tasks goes the normal way: a tombstone with no text.
            lists.later
                .map { item -> if (item.deletedAt == null && item.doneAt?.isBefore(doneFrom) == true) item.deleted(SharedListMerge.nextStamp(time, item)) else item }
                .filter { it.deletedAt?.isAfter(tombstonesFrom) ?: true }.sortedBy { it.position },
        )
        prefs.edit().putString(KEY, TokenVault.seal(write(all).toString(), ALIAS)).apply()
        _nextTasks.value = liveNextTasks()
        _parked.value = liveParked()
        _later.value = liveLater()
    }

    private fun liveNextTasks() = all.nextTasks.filter { it.deletedAt == null }

    private fun liveParked() = all.parked.filter { it.deletedAt == null }

    private fun liveLater() = all.later.filter { it.deletedAt == null }

    private fun newId(): String = UUID.randomUUID().toString()

    private fun write(lists: Lists): JsonObject = buildJsonObject {
        put("nextTasks", SyncFileJson.writeNextTasks(lists.nextTasks))
        put("parked", SyncFileJson.writeParked(lists.parked))
        put("later", SyncFileJson.writeLater(lists.later))
    }

    // Empty when nothing is stored or it can't be opened (the key goes with the app's data).
    private fun read(): Lists {
        val json = prefs.getString(KEY, null)?.let { TokenVault.open(it, ALIAS) }
            ?.let { runCatching { Json.parseToJsonElement(it) as JsonObject }.getOrNull() } ?: return Lists(emptyList(), emptyList(), emptyList())
        return Lists(
            SyncFileJson.readNextTasks(json["nextTasks"] as? JsonArray).orEmpty(),
            SyncFileJson.readParked(json["parked"] as? JsonArray).orEmpty(),
            SyncFileJson.readLater(json["later"] as? JsonArray).orEmpty(),
        )
    }

    private data class Lists(val nextTasks: List<SyncNextTask>, val parked: List<SyncParked>, val later: List<SyncLaterItem>)

    companion object {
        private const val KEY = "lists"
        private const val ALIAS = "accountabilibuddy-shared-lists"

        // Two positions closer than this are renumbered (halving a gap again and again runs out of precision).
        private const val MIN_POSITION_GAP = 1e-9
    }
}
