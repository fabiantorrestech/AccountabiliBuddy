package com.accountabilibuddy.companion.sync

import android.content.Context
import com.accountabilibuddy.shared.TokenVault
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SharedListMerge
import com.accountabilibuddy.shared.sync.SyncFileJson
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
 * The lists the user's devices share (ADR 48): the next session's tasks and the come-backs (the parked thoughts), each
 * item with its edit time and, once deleted, a tombstone kept 30 days. The lists go in this phone's sync file and other
 * devices' lists merge in item by item ([SharedListMerge], the same function the PC runs). Kept in the app's private
 * prefs, sealed with a key in the Android Keystore ([TokenVault]), never logged: task and thought text is only in
 * sealed files and sealed here. The running session's checklist is not one of these lists; it belongs to its PC.
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

    /** The next session's tasks, in list order. */
    val nextTasks: StateFlow<List<SyncNextTask>> = _nextTasks.asStateFlow()

    /** The come-backs, open and recently ticked, in list order. */
    val parked: StateFlow<List<SyncParked>> = _parked.asStateFlow()

    /** Set by the app: the user changed a list here, so it goes to the other devices soon. */
    var onChanged: () -> Unit = {}

    /** The next session's list for this phone's sync file: its tasks and the tombstones of the last 30 days. */
    @Synchronized
    fun nextTasksForSync(): List<SyncNextTask> {
        val keptFrom = now().minus(SharedListMerge.TOMBSTONES_KEPT)
        return all.nextTasks.filter { it.deletedAt?.isAfter(keptFrom) ?: true }
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
     * @return True when a list changed.
     */
    @Synchronized
    fun receive(nextTasks: List<SyncNextTask>?, parked: List<SyncParked>?): Boolean {
        val time = now()
        val mergedTasks = nextTasks?.let { SharedListMerge.merge(all.nextTasks, it, time) } ?: all.nextTasks
        val mergedParked = parked?.let { SharedListMerge.merge(all.parked, it, time) } ?: all.parked
        if (mergedTasks == SharedListMerge.merge(all.nextTasks, emptyList(), time) && mergedParked == SharedListMerge.merge(all.parked, emptyList(), time)) {
            return false
        }

        save(Lists(mergedTasks, mergedParked), time)
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
        )
        prefs.edit().putString(KEY, TokenVault.seal(write(all).toString(), ALIAS)).apply()
        _nextTasks.value = liveNextTasks()
        _parked.value = liveParked()
    }

    private fun liveNextTasks() = all.nextTasks.filter { it.deletedAt == null }

    private fun liveParked() = all.parked.filter { it.deletedAt == null }

    private fun newId(): String = UUID.randomUUID().toString()

    private fun write(lists: Lists): JsonObject = buildJsonObject {
        put("nextTasks", SyncFileJson.writeNextTasks(lists.nextTasks))
        put("parked", SyncFileJson.writeParked(lists.parked))
    }

    // Empty when nothing is stored or it can't be opened (the key goes with the app's data).
    private fun read(): Lists {
        val json = prefs.getString(KEY, null)?.let { TokenVault.open(it, ALIAS) }
            ?.let { runCatching { Json.parseToJsonElement(it) as JsonObject }.getOrNull() } ?: return Lists(emptyList(), emptyList())
        return Lists(
            SyncFileJson.readNextTasks(json["nextTasks"] as? JsonArray).orEmpty(),
            SyncFileJson.readParked(json["parked"] as? JsonArray).orEmpty(),
        )
    }

    private data class Lists(val nextTasks: List<SyncNextTask>, val parked: List<SyncParked>)

    companion object {
        private const val KEY = "lists"
        private const val ALIAS = "accountabilibuddy-shared-lists"

        // Two positions closer than this are renumbered (halving a gap again and again runs out of precision).
        private const val MIN_POSITION_GAP = 1e-9
    }
}
