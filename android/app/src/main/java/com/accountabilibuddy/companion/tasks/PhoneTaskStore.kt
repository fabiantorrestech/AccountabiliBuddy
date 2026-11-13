package com.accountabilibuddy.companion.tasks

import android.content.Context
import android.util.Log
import com.accountabilibuddy.shared.TokenVault
import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.tasks.TaskFieldStamps
import com.accountabilibuddy.shared.tasks.OldTaskFields
import com.accountabilibuddy.shared.tasks.TaskJson
import com.accountabilibuddy.shared.tasks.TaskListMerge
import com.accountabilibuddy.shared.tasks.TaskListRules
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskNewerEntries
import com.accountabilibuddy.shared.tasks.TaskParts
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.io.File
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset
import java.util.concurrent.CopyOnWriteArrayList

/**
 * The phone's one task list (ADR 76, R7.2): tasks, labels, views, History and the account's Tasks settings, with a newer app's
 * entries kept as written ([TaskNewerEntries]). In the app's private prefs (`task_model`), sealed with a key in the Android Keystore
 * ([TokenVault]) like the other private lists; titles, notes and every other text are never logged. Every change is normalized
 * ([TaskListRules.normalize]: settled into History, numbered, pruned) before it is kept.
 *
 * It is the source only once the one-time migration ([PhoneTaskModelMigration]) wrote the lists and the marker in one commit
 * ([activate]); until then [isActive] is false and the phone keeps its old stores. The old stores are never written or deleted by
 * it: they stay one release as the way back.
 */
class PhoneTaskStore(context: Context, private val clock: () -> OffsetDateTime = { DotNetTime.cut(OffsetDateTime.now(ZoneOffset.UTC)) }) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
    private val logFile = File(context.filesDir, "companion/$LOG_NAME")
    private val listeners = CopyOnWriteArrayList<() -> Unit>()
    private var newer = TaskParts(null, null, null, null, null)
    private var searches: Map<String, String> = emptyMap()
    private var fileCache: Triple<TaskLists, Long, Pair<OldTaskFields, TaskParts>>? = null
    private val _lists = MutableStateFlow(TaskLists.EMPTY)

    /** The lists as kept (tombstones included); empty while not [isActive]. */
    val lists: StateFlow<TaskLists> = _lists.asStateFlow()

    /** The task list is the source (the migration ran and its lists were read). */
    @Volatile
    var isActive: Boolean = false
        private set

    /** This phone's sync device id (stamps name it). Set by the app. */
    var selfId: () -> String = { FieldStamp.NOBODY }

    /** The group's numbering device (ADR 76, Q3); null while there is none (temporary numbers here). Set by the app. */
    var numberingDevice: () -> String? = { null }

    /** Called on any thread after the lists changed (here or from another device). */
    fun addListener(listener: () -> Unit) {
        listeners += listener
    }

    fun hasMarker(): Boolean = prefs.getInt(KEY_MARKER, 0) >= MODEL_VERSION

    /** Reads the stored lists (marker set). An unreadable copy is set aside as it was (never overwritten) and the list starts empty. */
    @Synchronized
    fun load() {
        val sealed = prefs.getString(KEY_LISTS, null)
        val parts = sealed?.let { TokenVault.open(it, ALIAS) }?.let { runCatching { TaskJson.readParts(Json.parseToJsonElement(it) as JsonObject) }.getOrNull() }
        if (parts == null) {
            if (sealed != null) prefs.edit().putString(KEY_UNREADABLE, sealed).commit()
            log("task list could not be read; set aside, starting empty (the other devices' lists come back at the next sync)")
        }

        newer = parts?.let { TaskParts(null, null, null, null, null, it.newerTasks, it.newerLabels, it.newerViews, it.newerTaskLog) } ?: newer
        searches = readSearches()
        _lists.value = parts?.let { TaskLists(it.tasks.orEmpty(), it.labels.orEmpty(), it.views.orEmpty(), it.taskLog.orEmpty(), it.accountSettings) } ?: TaskLists.EMPTY
        isActive = true
    }

    /** The migration's lists and the marker, written in one commit; true when kept (the store is the source from now on). */
    @Synchronized
    fun activate(lists: TaskLists, viewSearches: Map<String, String>): Boolean {
        val written = prefs.edit()
            .putString(KEY_LISTS, TokenVault.seal(encode(lists).toString(), ALIAS))
            .putString(KEY_SEARCHES, TokenVault.seal(JsonObject(viewSearches.mapValues { JsonPrimitive(it.value) }).toString(), ALIAS))
            .putInt(KEY_MARKER, MODEL_VERSION)
            .commit()
        if (!written) return false
        searches = viewSearches
        _lists.value = lists
        isActive = true
        tell()
        return true
    }

    /** Whether this version's migration attempt already wrote its backup (one backup per version). */
    fun attempted(version: String): Boolean = prefs.getString(KEY_ATTEMPT, null) == version

    /** The backup made before the migration ([content]: the old stores' sealed copies), kept with the version that made it. */
    fun recordBackup(version: String, content: JsonObject) {
        prefs.edit().putString(KEY_BACKUP_PREFIX + version, content.toString()).putString(KEY_ATTEMPT, version).commit()
    }

    /**
     * A change made here: [change] gets the lists, this device's id and the time; the result is normalized and kept. True when the
     * lists changed.
     */
    fun change(change: (TaskLists, String, OffsetDateTime) -> TaskLists): Boolean = commit { lists, self, now -> change(lists, self, now) }

    /** Another device's parts read in ([TaskListRules.mergeFile]: a file without `tasks` leaves the tasks alone). True when the lists changed. */
    fun mergeFile(parts: TaskParts?): Boolean {
        if (parts == null || !isActive) return false
        synchronized(this) { newer = TaskNewerEntries.merge(newer, parts) }
        return commit { lists, self, now -> TaskListRules.mergeFile(lists, parts, self, now) ?: lists }
    }

    /**
     * A backup's task list taken in (ADR 71), as another device's would be. With [restore], its live tasks, labels and views are stamped
     * now first, so they come back over later edits and deletions. Returns how many live tasks it held.
     */
    fun import(backup: TaskLists, restore: Boolean): Int {
        if (!isActive) return 0
        commit { lists, self, now ->
            val incoming = if (!restore) backup else TaskLists(
                backup.tasks.filter { it.task.isLive }.map { com.accountabilibuddy.shared.tasks.SyncTaskItem(it.task.copy(editedAt = now, touchedAt = now), TaskFieldStamps.allAt(FieldStamp(now, self))) },
                backup.labels.filter { it.label.isLive }.map { it.copy(label = it.label.copy(editedAt = now)) },
                backup.views.filter { it.view.isLive }.map { it.copy(view = it.view.copy(editedAt = now)) },
                backup.log,
                backup.account,
            )
            TaskListMerge.mergeLists(lists, incoming, self, now)
        }
        return backup.tasks.count { it.task.isLive }
    }

    /** The old fields and the new parts for this phone's file (rebuilt at most once a minute for the same lists); null while not active. */
    @Synchronized
    fun fileParts(): Pair<OldTaskFields, TaskParts>? {
        if (!isActive) return null
        val now = clock()
        val minute = now.toEpochSecond() / 60
        val lists = _lists.value
        fileCache?.let { (held, at, parts) -> if (held === lists && at == minute) return parts }
        val parts = TaskListRules.fileParts(lists, now, ZoneId.systemDefault(), newer)
        fileCache = Triple(lists, minute, parts)
        return parts
    }

    /** The lists for a backup: the new parts as kept (local items with their text, a newer app's entries passed on). */
    fun backupParts(): TaskParts? {
        if (!isActive) return null
        val lists = _lists.value
        return TaskParts(lists.tasks, lists.labels, lists.views, lists.log, lists.account?.takeIf { !it.isEmpty }, newer.newerTasks, newer.newerLabels, newer.newerViews, newer.newerTaskLog)
    }

    /** The time zone the store settles days in. */
    fun zone(): ZoneId = ZoneId.systemDefault()

    /** A line in the task model log (counts and states only, never text). */
    fun log(line: String) {
        Log.i(TAG, line)
        runCatching {
            logFile.parentFile?.mkdirs()
            logFile.appendText(clock().toString() + "  " + line + "\n")
        }
    }

    private fun commit(change: (TaskLists, String, OffsetDateTime) -> TaskLists): Boolean {
        val changed = synchronized(this) {
            if (!isActive) return false
            val self = selfId().lowercase()
            val now = clock()
            val held = _lists.value
            val normalized = TaskListRules.normalize(change(held, self, now), self, numberingDevice(), now, zone())
            if (normalized == held) {
                false
            } else {
                prefs.edit().putString(KEY_LISTS, TokenVault.seal(encode(normalized).toString(), ALIAS)).apply()
                _lists.value = normalized
                true
            }
        }
        if (changed) tell()
        return changed
    }

    private fun tell() = listeners.forEach { runCatching { it() } }

    private fun encode(lists: TaskLists): JsonObject = TaskJson.writeParts(
        TaskParts(lists.tasks, lists.labels, lists.views, lists.log, lists.account, newer.newerTasks, newer.newerLabels, newer.newerViews, newer.newerTaskLog),
    ).let { parts ->
        // Every part is written, even empty, so a store read back never takes a missing part for one it doesn't have.
        buildJsonObject {
            SyncFileJson.TASK_PART_NAMES.filter { it != "taskAccountSettings" }.forEach { name -> put(name, parts[name] ?: JsonArray(emptyList())) }
            parts["taskAccountSettings"]?.let { put("taskAccountSettings", it) }
        }
    }

    private fun readSearches(): Map<String, String> {
        val text = prefs.getString(KEY_SEARCHES, null)?.let { TokenVault.open(it, ALIAS) } ?: return emptyMap()
        val json = runCatching { Json.parseToJsonElement(text) as? JsonObject }.getOrNull() ?: return emptyMap()
        return json.mapNotNull { (key, value) -> (value as? JsonPrimitive)?.content?.let { key to it } }.toMap()
    }

    companion object {
        const val MODEL_VERSION = 1
        const val LOG_NAME = "tasks-model.log"
        private const val TAG = "TaskModel"
        private const val PREFS = "task_model"
        private const val ALIAS = "accountabilibuddy-task-model"
        private const val KEY_MARKER = "model"
        private const val KEY_LISTS = "lists"
        private const val KEY_UNREADABLE = "lists.unreadable"
        private const val KEY_SEARCHES = "viewSearches"
        private const val KEY_ATTEMPT = "attempt"
        private const val KEY_BACKUP_PREFIX = "before-model-"
    }
}
