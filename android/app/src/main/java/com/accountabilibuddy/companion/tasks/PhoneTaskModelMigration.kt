package com.accountabilibuddy.companion.tasks

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.tasks.TaskLocalEdits
import com.accountabilibuddy.shared.tasks.TaskMigrationInput
import com.accountabilibuddy.shared.tasks.TaskMigrationPreset
import com.accountabilibuddy.shared.tasks.TaskModelSwitch
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset

/**
 * The startup step that makes the one task list (ADR 76) the phone's source, once (R7.2; the PC's TaskModelMigration). With the marker
 * stored it only reads the task list. Otherwise it first backs up the old stores (their sealed copies, kept in the task model's own
 * prefs, once per app version; and, when a backup folder is chosen, the phone's usual backups by kind), runs the migration rule on what
 * they hold (tombstones and local items included) and switches over only when every item is accounted for, writing the lists and the
 * marker in one commit. The old stores are never written or deleted: they stay one release as the way back. Anything that goes wrong
 * leaves no marker and the phone on its old stores. The log (`tasks-model.log`) holds counts only, never text.
 */
object PhoneTaskModelMigration {
    fun runOnce(app: CompanionApp) {
        val store = app.taskModel
        if (store.hasMarker()) {
            try {
                store.load()
            } catch (e: Exception) {
                store.log("task list could not be read (${e.javaClass.simpleName})")
            }
            return
        }

        try {
            val version = app.appVersion()
            if (store.attempted(version)) {
                store.log("backup of this version's attempt already written")
            } else {
                store.recordBackup(version, oldStoresCopy(app))
                runCatching { if (app.autoBackup.folder != null) app.autoBackup.backUpNow("before the task list update") }
                store.log("backup written (before-model-$version)")
            }

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
            val outcome = TaskModelSwitch.run(input, self, numbering)
            store.log(outcome.logLine)
            val lists = outcome.lists ?: return
            if (!store.activate(lists, outcome.viewSearches)) {
                store.log("the task list could not be written; staying on the old lists (no marker written)")
                return
            }

            store.log("switched over: ${lists.tasks.count { it.task.isLive }} live tasks, ${lists.log.size} History entries")
        } catch (e: Exception) {
            // Never the message: it could quote a stored value. The type is enough to find it.
            store.log("migration failed (${e.javaClass.simpleName}); staying on the old lists (no marker written)")
        }
    }

    // The old stores exactly as kept (still sealed with their own Keystore keys): the whole-store copy the PC's database backup is.
    private fun oldStoresCopy(app: CompanionApp) = buildJsonObject {
        put("at", OffsetDateTime.now(ZoneOffset.UTC).toString())
        for ((prefs, key) in OLD_STORES) {
            app.getSharedPreferences(prefs, android.content.Context.MODE_PRIVATE).getString(key, null)?.let { put("$prefs/$key", it) }
        }
    }

    private val OLD_STORES = listOf("shared_lists" to "lists", "task_categories" to "list", "task_filter_presets" to "list")
}
