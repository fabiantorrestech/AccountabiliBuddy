package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.SharedItem
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * The phone task store's decisions, pure (R7.2 review fixes), so they run on the JVM; `PhoneTaskStore`, `PhoneTaskModelMigration` and
 * `SharedListsStore` only read and write prefs around them.
 *
 * - [unreadableOldStores]: an old store that is stored but won't open or parse is never migrated as empty (item 1).
 * - [migrate]: the one-time switch's steps in order; a backup that isn't kept, an unreadable old store, counts that don't balance or a
 *   commit that fails each leave no marker (items 1, 4 and the stale-backup NIT: the sealed copies are taken fresh on every attempt).
 * - [open]: an unreadable task list closes the store (like the PC) and is set aside once, never overwritten; a set-aside copy that
 *   opens at a later start is merged back in (item 2).
 * - [committed] and [facadeChanges]: what `PhoneTaskStore.commit` keeps and what `SharedListsStore.edit` hands it.
 */
object TaskModelStartup {
    /** The names of the old stores that hold something that can't be opened or read; empty when every one can be migrated. */
    fun unreadableOldStores(stores: List<TaskOldStore>): List<String> = stores.filter { store ->
        if (store.stored == null) return@filter false
        val text = store.opened ?: return@filter true
        !parses(text, store.shape)
    }.map { it.name }

    /**
     * The one-time migration's steps (the store has no marker yet). True when the task list became the source. Nothing here writes the
     * old stores; every refusal leaves no marker, so the phone stays on its old stores and the next start tries again.
     */
    fun migrate(port: TaskMigrationPort): Boolean {
        val unreadable = unreadableOldStores(port.oldStores())
        if (unreadable.isNotEmpty()) {
            port.keepUnreadable(unreadable)
            port.log("old store could not be read (${unreadable.joinToString(", ")}); nothing migrated, old stores untouched (no marker written)")
            return false
        }

        val version = port.version()
        val firstAttempt = !port.folderBackedUp(version)
        if (!port.recordBackup(version)) {
            port.log("backup could not be written; staying on the old lists (no marker written)")
            return false
        }
        if (firstAttempt) port.folderBackup()
        port.log(if (firstAttempt) "backup written (before-model-$version)" else "backup written again (before-model-$version, fresh copy)")

        val outcome = port.switchOutcome()
        port.log(outcome.logLine)
        val lists = outcome.lists ?: return false
        if (!port.activate(lists, outcome.viewSearches)) {
            port.log("the task list could not be written; staying on the old lists (no marker written)")
            return false
        }

        port.log("switched over: ${lists.tasks.count { it.task.isLive }} live tasks, ${lists.log.size} History entries")
        return true
    }

    /**
     * The store's start with the marker set. [stored] is the sealed lists (null: none), [setAside] the copy an earlier start set aside
     * (null: none), [read] opens and reads a sealed value (null: it can't be).
     */
    fun open(stored: String?, setAside: String?, read: (String) -> TaskParts?, self: String, now: OffsetDateTime): TaskStoreOpening {
        val main = stored?.let(read)
        if (main == null) {
            val what = if (stored == null) "task list missing" else "task list could not be read"
            val aside = when {
                stored == null -> "nothing to set aside"
                setAside == null -> "set aside"
                else -> "an earlier copy is already set aside (kept, not overwritten)"
            }
            return TaskStoreOpening.Closed(stored != null && setAside == null, "$what; $aside; task lists closed until it can be read (the old lists are not used)")
        }

        if (setAside == null) return TaskStoreOpening.Open(main, write = false, clearSetAside = false, logLine = "task list read")
        val aside = read(setAside)
            ?: return TaskStoreOpening.Open(main, write = false, clearSetAside = false, logLine = "task list read; the set-aside copy still can't be read (kept)")

        val merged = TaskListMerge.mergeLists(listsOf(main), listsOf(aside), self, now)
        // The set-aside copy is the older one: its newer-app entries stay only where the current list doesn't mention their ids.
        val newer = TaskNewerEntries.merge(aside, main)
        val parts = TaskParts(
            merged.tasks, merged.labels, merged.views, merged.log, merged.account,
            newer.newerTasks, newer.newerLabels, newer.newerViews, newer.newerTaskLog,
        )
        return TaskStoreOpening.Open(parts, write = true, clearSetAside = true, logLine = "task list read; the set-aside copy could be read and was merged back in")
    }

    /** What a change keeps: [changed] normalized as the store keeps every list; null when that is what it [held] already. */
    fun committed(held: TaskLists, changed: TaskLists, self: String, numberingDevice: String?, now: OffsetDateTime, zone: ZoneId?): TaskLists? =
        TaskListRules.normalize(changed, self, numberingDevice, now, zone).takeIf { it != held }

    /** The old lists' items a change on the old screens made or changed (by id, tombstones included): what goes into the task list. */
    fun facadeChanges(before: OldTaskFields, after: OldTaskFields): OldTaskFields = OldTaskFields(
        changedOf(before.nextTasks, after.nextTasks),
        changedOf(before.later, after.later),
        changedOf(before.parked, after.parked),
        emptyList(),
        emptyList(),
        changedOf(before.autoSaved, after.autoSaved),
    )

    /** The lists of stored parts (a missing part is an empty one: the store writes every part). */
    fun listsOf(parts: TaskParts): TaskLists =
        TaskLists(parts.tasks.orEmpty(), parts.labels.orEmpty(), parts.views.orEmpty(), parts.taskLog.orEmpty(), parts.accountSettings)

    private fun <T : SharedItem> changedOf(before: List<T>, after: List<T>): List<T> {
        val held = before.associateBy { it.id.lowercase() }
        return after.filter { held[it.id.lowercase()] != it }
    }

    private fun parses(text: String, shape: TaskOldStore.Shape): Boolean {
        val element = runCatching { Json.parseToJsonElement(text) }.getOrNull() ?: return false
        return when (shape) {
            TaskOldStore.Shape.OBJECT -> element is JsonObject
            TaskOldStore.Shape.ARRAY -> element is JsonArray
        }
    }
}
