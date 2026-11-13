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
import com.accountabilibuddy.shared.tasks.TaskModelStartup
import com.accountabilibuddy.shared.tasks.TaskNewerEntries
import com.accountabilibuddy.shared.tasks.TaskParts
import com.accountabilibuddy.shared.tasks.TaskStoreOpening
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
 * it: they stay one release as the way back. With the marker set but the lists unreadable the store is [isClosed] (R7.2 review).
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

    /**
     * The migration ran but the task list can't be read (R7.2 review item 2): the task lists are closed, read-only and empty; nothing
     * is kept, sent or backed up from them, and the old stores (stale since the migration) are not used either.
     */
    @Volatile
    var isClosed: Boolean = false
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

    /**
     * The store's start with the marker set ([TaskModelStartup.open]). Readable lists are the source; a copy set aside at an earlier
     * start that opens now is merged back in and dropped in the same commit. Unreadable lists close the store, like the PC: nothing is
     * shown, kept, sent or backed up from it and the old stores are not used (they stopped at the migration); the sealed value is set
     * aside once (an existing copy is never overwritten) and the next start tries again.
     */
    @Synchronized
    fun load() {
        val stored = prefs.getString(KEY_LISTS, null)
        val setAside = prefs.getString(KEY_UNREADABLE, null)
        when (val opening = TaskModelStartup.open(stored, setAside, ::readSealed, selfId().lowercase(), clock())) {
            is TaskStoreOpening.Closed -> {
                if (opening.setAside && stored != null) prefs.edit().putString(KEY_UNREADABLE, stored).commit()
                log(opening.logLine)
                close()
            }
            is TaskStoreOpening.Open -> {
                val parts = opening.parts
                newer = TaskParts(null, null, null, null, null, parts.newerTasks, parts.newerLabels, parts.newerViews, parts.newerTaskLog)
                val lists = TaskModelStartup.listsOf(parts)
                if (opening.write) {
                    val edit = prefs.edit().putString(KEY_LISTS, TokenVault.seal(encode(lists).toString(), ALIAS))
                    if (opening.clearSetAside) edit.remove(KEY_UNREADABLE)
                    // Not kept: the set-aside copy stays and is merged again at the next start (the merge gives the same lists).
                    if (!edit.commit()) log("the merged task list could not be written yet; the set-aside copy is kept")
                }
                log(opening.logLine)
                searches = readSearches()
                _lists.value = lists
                isClosed = false
                isActive = true
            }
        }
    }

    /** The task list can't be used this run (its start failed): closed, like an unreadable one. */
    @Synchronized
    fun close() {
        isActive = false
        isClosed = true
        _lists.value = TaskLists.EMPTY
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

    /** An old store's unreadable sealed value ([name] = "prefs/key"), kept once: a copy already kept is never overwritten. */
    fun keepUnreadableOldStore(name: String, sealed: String) {
        val key = KEY_UNREADABLE_OLD_PREFIX + name
        if (!prefs.contains(key)) prefs.edit().putString(key, sealed).commit()
    }

    /** Whether this version's migration attempt already wrote its backup (the folder backup: once per version). */
    fun attempted(version: String): Boolean = prefs.getString(KEY_ATTEMPT, null) == version

    /**
     * The backup made before the migration ([content]: the old stores' sealed copies), kept with the version that made it; taken fresh
     * on every attempt (the old stores may have changed since an earlier one). False when it wasn't written: no migration then.
     */
    fun recordBackup(version: String, content: JsonObject): Boolean =
        prefs.edit().putString(KEY_BACKUP_PREFIX + version, content.toString()).putString(KEY_ATTEMPT, version).commit()

    /**
     * A change made here: [change] gets the lists, this device's id and the time; the result is normalized and kept. True when the
     * lists changed.
     */
    fun change(change: (TaskLists, String, OffsetDateTime) -> TaskLists): Boolean = commit { lists, self, now -> change(lists, self, now) }

    /**
     * Another device's parts read in ([TaskListRules.mergeFile]: a file without `tasks` leaves the tasks alone). True when the lists
     * changed. A newer app's entries it brings are kept (written with the lists) and go out in this phone's next file.
     */
    fun mergeFile(parts: TaskParts?): Boolean {
        if (parts == null || !isActive) return false
        val newerChanged = synchronized(this) {
            val merged = TaskNewerEntries.merge(newer, parts)
            (merged != newer).also { changed ->
                if (changed) {
                    newer = merged
                    fileCache = null
                }
            }
        }
        return commit(keepAnyway = newerChanged) { lists, self, now -> TaskListRules.mergeFile(lists, parts, self, now) ?: lists }
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

    // [keepAnyway]: written even when the lists didn't change (a newer app's entries did).
    private fun commit(keepAnyway: Boolean = false, change: (TaskLists, String, OffsetDateTime) -> TaskLists): Boolean {
        val changed = synchronized(this) {
            if (!isActive) return false
            val self = selfId().lowercase()
            val now = clock()
            val held = _lists.value
            val normalized = TaskModelStartup.committed(held, change(held, self, now), self, numberingDevice(), now, zone())
            if (normalized == null) {
                if (keepAnyway) prefs.edit().putString(KEY_LISTS, TokenVault.seal(encode(held).toString(), ALIAS)).apply()
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

    // A sealed value opened and read; null when it can't be (the key is gone or the text is damaged).
    private fun readSealed(sealed: String): TaskParts? =
        TokenVault.open(sealed, ALIAS)?.let { text -> runCatching { TaskJson.readParts(Json.parseToJsonElement(text) as JsonObject) }.getOrNull() }

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
        private const val KEY_UNREADABLE_OLD_PREFIX = "unreadable-old-"
    }
}
