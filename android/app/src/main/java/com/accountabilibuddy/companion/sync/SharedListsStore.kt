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

        save(Lists(mergedTasks, mergedParked))
        return true
    }

    /** Adds a come-back at the end of the list; a blank line adds nothing. */
    fun addParked(text: String) {
        val trimmed = text.trim()
        if (trimmed.isEmpty()) {
            return
        }

        edit { lists ->
            val time = now()
            val last = lists.parked.maxOfOrNull { it.position }
            lists.copy(parked = lists.parked + SyncParked(newId(), trimmed, time, null, time, null, SharedListMerge.positionBetween(last, null)))
        }
    }

    /** Ticks a come-back: it leaves the open list, keeping when it was ticked. */
    fun tickParked(id: String) = changeParked(id) { it.copy(doneAt = now(), editedAt = now()) }

    /** Changes a come-back's text; a blank text changes nothing. */
    fun editParked(id: String, text: String) {
        val trimmed = text.trim()
        if (trimmed.isNotEmpty()) {
            changeParked(id) { it.copy(text = trimmed, editedAt = now()) }
        }
    }

    /** Deletes a come-back: its text goes at once and a tombstone tells the other devices. */
    fun deleteParked(id: String) = changeParked(id) { it.deleted(now()) }

    /** Adds a task to the end of the next session's list; a blank title adds nothing. The three-tasks prompt is the screen's. */
    fun addNextTask(title: String, category: String? = null, categoryColor: String? = null): String? {
        val trimmed = title.trim()
        if (trimmed.isEmpty()) {
            return null
        }

        val id = newId()
        edit { lists ->
            val time = now()
            val last = lists.nextTasks.maxOfOrNull { it.position }
            lists.copy(nextTasks = lists.nextTasks + SyncNextTask(id, trimmed, null, category, categoryColor, null, time, null, SharedListMerge.positionBetween(last, null)))
        }
        return id
    }

    /** Changes a task's title and category; a blank title changes nothing. */
    fun editNextTask(id: String, title: String, category: String?, categoryColor: String?) {
        val trimmed = title.trim()
        if (trimmed.isNotEmpty()) {
            changeNextTask(id) { it.copy(title = trimmed, category = category, categoryColor = categoryColor, editedAt = now()) }
        }
    }

    /** Ticks or unticks a task (its steps follow, as on the PC). */
    fun toggleNextTask(id: String) = changeNextTask(id) { task ->
        val doneAt = if (task.isDone) null else now()
        task.copy(
            doneAt = doneAt,
            subtasks = task.subtasks?.map { step -> step.copy(doneAt = if (doneAt == null) null else step.doneAt ?: doneAt) },
            editedAt = now(),
        )
    }

    /** Deletes a task: its text goes at once and a tombstone tells the other devices. */
    fun deleteNextTask(id: String) = changeNextTask(id) { it.deleted(now()) }

    /** Moves a task to [index] among the tasks (0 = first); only that task is edited, it takes a position between its new neighbours. */
    fun moveNextTask(id: String, index: Int) = edit { lists ->
        val live = lists.nextTasks.filter { it.deletedAt == null }.sortedBy { it.position }
        val moving = live.firstOrNull { it.id == id } ?: return@edit lists
        val others = live.filter { it.id != id }
        val at = index.coerceIn(0, others.size)
        val position = SharedListMerge.positionBetween(others.getOrNull(at - 1)?.position, others.getOrNull(at)?.position)
        lists.copy(nextTasks = lists.nextTasks.map { if (it.id == id) moving.copy(position = position, editedAt = now()) else it })
    }

    /** Puts a step on a task. */
    fun addNextSubtask(taskId: String, title: String) {
        val trimmed = title.trim()
        if (trimmed.isNotEmpty()) {
            changeNextTask(taskId) { task ->
                task.copy(
                    doneAt = null,
                    subtasks = task.subtasks.orEmpty() + SessionTaskItem(newId(), trimmed, null, emptyList()),
                    editedAt = now(),
                )
            }
        }
    }

    private fun changeParked(id: String, change: (SyncParked) -> SyncParked) = edit { lists ->
        lists.copy(parked = lists.parked.map { if (it.id == id && it.deletedAt == null) change(it) else it })
    }

    private fun changeNextTask(id: String, change: (SyncNextTask) -> SyncNextTask) = edit { lists ->
        lists.copy(nextTasks = lists.nextTasks.map { if (it.id == id && it.deletedAt == null) change(it) else it })
    }

    private fun edit(change: (Lists) -> Lists) {
        val changed = synchronized(this) {
            val updated = change(all)
            if (updated == all) {
                false
            } else {
                save(updated)
                true
            }
        }
        if (changed) {
            onChanged()
        }
    }

    // Kept: the open and the ticked ones of the last 90 days (as on the PC), and tombstones of the last 30.
    private fun save(lists: Lists) {
        val time = now()
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
    }
}
