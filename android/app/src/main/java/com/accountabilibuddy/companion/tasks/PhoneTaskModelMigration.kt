package com.accountabilibuddy.companion.tasks

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.shared.TokenVault
import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskLocalEdits
import com.accountabilibuddy.shared.tasks.TaskMigrationInput
import com.accountabilibuddy.shared.tasks.TaskMigrationPort
import com.accountabilibuddy.shared.tasks.TaskMigrationPreset
import com.accountabilibuddy.shared.tasks.TaskModelStartup
import com.accountabilibuddy.shared.tasks.TaskModelSwitch
import com.accountabilibuddy.shared.tasks.TaskOldStore
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset

/**
 * The startup step that makes the one task list (ADR 76) the phone's source, once (R7.2; the PC's TaskModelMigration). With the marker
 * stored it only reads the task list (unreadable: closed, see [PhoneTaskStore.load]). Otherwise [TaskModelStartup.migrate] runs its
 * steps: every old store that holds something must open and read (else nothing is migrated: never an unreadable store as an empty
 * one), a fresh sealed copy of the old stores is kept in the task model's own prefs (and, once per app version, the phone's usual
 * backups by kind when a folder is chosen), the migration rule runs on what they hold (tombstones and local items included) and the
 * phone switches over only when every item is accounted for, writing the lists and the marker in one commit. The old stores are never
 * written or deleted: they stay one release as the way back. Anything that goes wrong leaves no marker and the phone on its old stores.
 * The log (`tasks-model.log`) holds counts only, never text.
 */
object PhoneTaskModelMigration {
    fun runOnce(app: CompanionApp) {
        val store = app.taskModel
        if (store.hasMarker()) {
            try {
                store.load()
            } catch (e: Exception) {
                store.log("task list could not be read (${e.javaClass.simpleName}); task lists closed this run (the old lists are not used)")
                store.close()
            }
            return
        }

        try {
            TaskModelStartup.migrate(Port(app, store))
        } catch (e: Exception) {
            // Never the message: it could quote a stored value. The type is enough to find it.
            store.log("migration failed (${e.javaClass.simpleName}); staying on the old lists (no marker written)")
        }
    }

    private class Port(private val app: CompanionApp, private val store: PhoneTaskStore) : TaskMigrationPort {
        override fun version(): String = app.appVersion()

        override fun oldStores(): List<TaskOldStore> = OLD_STORES.map { old ->
            val stored = app.getSharedPreferences(old.prefs, android.content.Context.MODE_PRIVATE).getString(old.key, null)
            TaskOldStore("${old.prefs}/${old.key}", stored, stored?.let { TokenVault.open(it, old.alias) }, old.shape)
        }

        override fun keepUnreadable(names: List<String>) {
            for (old in OLD_STORES.filter { "${it.prefs}/${it.key}" in names }) {
                app.getSharedPreferences(old.prefs, android.content.Context.MODE_PRIVATE).getString(old.key, null)?.let { store.keepUnreadableOldStore("${old.prefs}/${old.key}", it) }
            }
        }

        override fun folderBackedUp(version: String): Boolean = store.attempted(version)

        override fun recordBackup(version: String): Boolean = store.recordBackup(version, oldStoresCopy())

        override fun folderBackup() {
            runCatching { if (app.autoBackup.folder != null) app.autoBackup.backUpNow("before the task list update") }
        }

        override fun switchOutcome(): TaskModelSwitch.Outcome {
            val now = DotNetTime.cut(OffsetDateTime.now(ZoneOffset.UTC))
            val numbering = store.numberingDevice()
            val self = store.selfId().lowercase()
            val old = app.sharedLists.storedForMigration()
            val input = TaskMigrationInput(
                app.taskCategories.categories.value,
                old.nextTasks,
                old.later,
                old.parked,
                old.autoSaved,
                app.taskFilterPresets.presets.value.map { TaskMigrationPreset(it.id, it.name, it.category, it.search) },
                now,
                ZoneId.systemDefault(),
                isNumberingDevice = numbering != null && numbering.equals(self, ignoreCase = true),
                firstNumber = 1,
                sessionActions = TaskLocalEdits.SESSION_ACTIONS,
            )
            return TaskModelSwitch.run(input, self, numbering)
        }

        override fun activate(lists: TaskLists, viewSearches: Map<String, String>): Boolean = store.activate(lists, viewSearches)

        override fun log(line: String) = store.log(line)

        // The old stores exactly as kept (still sealed with their own Keystore keys): the whole-store copy the PC's database backup is.
        private fun oldStoresCopy() = buildJsonObject {
            put("at", OffsetDateTime.now(ZoneOffset.UTC).toString())
            for (old in OLD_STORES) {
                app.getSharedPreferences(old.prefs, android.content.Context.MODE_PRIVATE).getString(old.key, null)?.let { put("${old.prefs}/${old.key}", it) }
            }
        }
    }

    private class OldStoreRef(val prefs: String, val key: String, val alias: String, val shape: TaskOldStore.Shape)

    // Each store's prefs, key and Keystore alias as its own class keeps them (SharedListsStore, TaskCategoriesStore, TaskFilterPresetsStore).
    private val OLD_STORES = listOf(
        OldStoreRef("shared_lists", "lists", "accountabilibuddy-shared-lists", TaskOldStore.Shape.OBJECT),
        OldStoreRef("task_categories", "list", "accountabilibuddy-task-categories", TaskOldStore.Shape.ARRAY),
        OldStoreRef("task_filter_presets", "list", "accountabilibuddy-task-filter-presets", TaskOldStore.Shape.ARRAY),
    )
}
