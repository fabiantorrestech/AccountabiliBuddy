package com.accountabilibuddy.companion.backup

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.data.BackupFile
import com.accountabilibuddy.companion.data.ImportedData
import com.accountabilibuddy.companion.data.ProfileDocumentParser
import com.accountabilibuddy.companion.data.SettingsJson
import com.accountabilibuddy.companion.goals.PhoneGoalsBackup
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.SessionNotesBackup
import com.accountabilibuddy.shared.sync.SyncFileJson
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.io.File
import java.time.Instant

/**
 * The phone's backups by kind (ADR 71), in the PC's backup file (BackupFile) with the PC's content names, so either opens the other's:
 * - **Synced settings** (`.absettings`): the profiles, sleep, day bar and mode, plus the task lists while they are shared. Restoring
 *   takes sleep and the mode through their own rules (a looser change waits) and the task lists by merge; the profiles and day bar come
 *   from a PC, so they are taken here as from a PC's backup and the PCs keep theirs.
 * - **This phone's settings** (`.abdevice`): [PhoneBackup]'s file (with both modes' copies), marked as a phone's; refused on a PC.
 * - **Task lists** (`.abtasks`): tasks, Later, come-backs and task categories.
 *
 * Round 2 R6: this phone's goals go with the task lists as a `goals` part ([PhoneGoalsBackup], the PC's shape), in the task-lists kind
 * and in synced settings while the task lists are shared; notes and reasons only in a file with a password. A restore takes a phone's
 * goals only (a PC's backup carries that PC's goals, which stay on the PC until ADR 79).
 */
object PhoneKindBackup {
    enum class Kind(val written: String, val extension: String, val title: String) {
        Synced("synced", ".absettings", "Synced settings"),
        Device("device", ".abdevice", "This phone's settings"),
        Tasks("tasks", ".abtasks", "Task lists"),
    }

    private const val PLATFORM = "phone"

    /** The backup file for [kind]; with [password], encrypted. */
    fun export(app: CompanionApp, kind: Kind, password: String? = null): String =
        BackupFile.seal(content(app, kind, includesNotes = !password.isNullOrEmpty()), password)

    /** The content of [kind]'s backup (what goes inside the file); goal notes and reasons only with [includesNotes] (a password). */
    fun content(app: CompanionApp, kind: Kind, includesNotes: Boolean = false): JsonObject = buildJsonObject {
        put("createdAt", Instant.now().toString())
        put("appVersion", app.appVersion())
        put("includesSecrets", false)
        put("kind", kind.written)
        put("platform", PLATFORM)
        when (kind) {
            Kind.Synced -> {
                readFile(app, "profiles.json")?.let { put("profiles", it.toString()) }
                put("synced", buildJsonObject {
                    put("sleep", SettingsJson.sleepJson(app.store.state.value.sleep))
                    readFile(app, "daybar.json")?.let { put("daybar", it) }
                    put("guidance", app.guidance.state.value.mode.name.lowercase())
                })
                if (app.sync.sharesTaskLists()) {
                    tasks(app, includesNotes)?.let { put("tasks", it) }
                    PhoneGoalsBackup.export(app.goals, includesNotes)?.let { put(PhoneGoalsBackup.PART, it) }
                }
            }
            Kind.Device -> put("device", Json.parseToJsonElement(PhoneBackup.export(app, app.appVersion(), forDeviceKind = true)))
            Kind.Tasks -> {
                tasks(app, includesNotes)?.let { put("tasks", it) }
                PhoneGoalsBackup.export(app.goals, includesNotes)?.let { put(PhoneGoalsBackup.PART, it) }
            }
        }
    }

    /** The kind a backup's content says it is; null for a file from before kinds. */
    fun kindOf(content: JsonObject): Kind? = content["kind"]?.jsonPrimitive?.contentOrNull?.let { written -> Kind.entries.firstOrNull { it.written == written } }

    /** Why [content] can't be restored as [kind] here; null when it can. */
    fun problem(app: CompanionApp, content: JsonObject, kind: Kind): String? {
        app.sync.removalLockedReason()?.let { return it }
        val found = kindOf(content)
        if (found != null && found != kind) return "That's a ${found.title.lowercase()} backup: import it on its own card."
        if (kind == Kind.Device && content["platform"]?.jsonPrimitive?.contentOrNull != PLATFORM) return "That backup is from a PC: a phone's settings can only come from a phone."
        return null
    }

    /**
     * Restores [content] as [kind]; [restore] makes the backup's task-list items win over later edits (otherwise the newest edit of each
     * stays). Returns what happened, for the screen. The device kind restarts the app after (the caller does it).
     */
    fun import(app: CompanionApp, content: JsonObject, kind: Kind, restore: Boolean): String {
        problem(app, content, kind)?.let { return it }
        return when (kind) {
            Kind.Synced -> importSynced(app, content, restore)
            Kind.Device -> PhoneBackup.import(app, (content["device"] as? JsonObject ?: return "The backup has no settings.").toString()) ?: "This phone's settings restored."
            Kind.Tasks -> listOfNotNull(
                importTasks(app, content["tasks"] as? JsonObject ?: return "The backup has no task lists.", restore),
                importGoals(app, content, restore),
            ).joinToString(" ")
        }
    }

    // Round 2 R6: a phone's goals only; a PC's backup carries that PC's goals, which stay on the PC (device-local until ADR 79).
    private fun importGoals(app: CompanionApp, content: JsonObject, restore: Boolean): String? {
        val part = content[PhoneGoalsBackup.PART] as? JsonObject ?: return null
        if (content["platform"]?.jsonPrimitive?.contentOrNull != PLATFORM) return "The goals in it are a PC's and stay on that PC."
        return PhoneGoalsBackup.import(app.goals, part, restore)
    }

    private fun importSynced(app: CompanionApp, content: JsonObject, restore: Boolean): String {
        val notes = mutableListOf("Synced settings restored; your other devices get them at the next sync.")
        val synced = content["synced"] as? JsonObject
        // The profiles and day bar come from a PC: taken here as from its backup (the PCs keep their own).
        val profilesText = content["profiles"]?.jsonPrimitive?.contentOrNull
        if (profilesText != null) {
            runCatching {
                val profiles = Json.parseToJsonElement(profilesText).jsonObject
                val dayBar = synced?.get("daybar") as? JsonObject
                app.store.import(
                    ImportedData(
                        document = ProfileDocumentParser.parse(profiles), profilesJson = profiles, sleep = null, sleepJson = null,
                        dayBar = dayBar?.let(SettingsJson::dayBar), dayBarJson = dayBar, events = emptyList(), eventsJson = null,
                        sourceDescription = "Synced settings backup",
                    ),
                )
            }.onFailure { notes += "The profiles in it couldn't be read." }
        }
        (synced?.get("sleep") as? JsonObject)?.let { sleep -> app.sleepSync.saveEdit(SettingsJson.sleep(sleep))?.let(notes::add) }
        when (synced?.get("guidance")?.jsonPrimitive?.contentOrNull?.lowercase()) {
            "guarding" -> if (app.guidance.state.value.mode != com.accountabilibuddy.companion.guidance.GuidanceMode.Guarding) app.guidance.chooseGuarding()
            "companion" -> if (app.guidance.state.value.mode != com.accountabilibuddy.companion.guidance.GuidanceMode.Companion) {
                notes += if (app.guidance.requestCompanion()) "ProductaBuddy takes effect after the usual wait."
                else "ProductaBuddy wasn't restored: a focus session is running. Choose it in Settings afterwards."
            }
        }
        (content["tasks"] as? JsonObject)?.let { notes += importTasks(app, it, restore) }
        importGoals(app, content, restore)?.let(notes::add)
        return notes.joinToString(" ")
    }

    // None while the task list is closed (R7.2 review item 2): neither it nor the stale old lists go in a backup.
    private fun tasks(app: CompanionApp, includesNotes: Boolean): JsonObject? = if (app.taskModel.isClosed) null else buildJsonObject {
        // AR1c (ADR 87, owner 2026-11-14): the session notes only inside a password-protected backup; a plain one never holds a note's text.
        SessionNotesBackup.part(app.sessionNotes.forSync(), includesNotes, Instant.now())?.let { put("sessionNotes", it) }
        val parts = app.taskModel.backupParts()
        if (parts != null) {
            // R7.2 (ADR 76): the task list itself (the PC's names: tasks, labels, views, taskLog, accountSettings), with the old lists
            // rebuilt from it (local items with their text) for an older app.
            val lists = app.taskModel.lists.value
            val old = com.accountabilibuddy.shared.tasks.OldTaskFieldsWriter.write(
                lists.tasks, lists.labels, lists.log, java.time.OffsetDateTime.now(java.time.ZoneOffset.UTC), app.taskModel.zone(), stubLocalItems = false,
            )
            put("nextTasks", SyncFileJson.writeNextTasks(old.nextTasks))
            put("comebacks", SyncFileJson.writeParked(old.parked))
            put("later", SyncFileJson.writeLater(old.later))
            put("categories", SyncFileJson.writeCategories(old.categories))
            com.accountabilibuddy.shared.tasks.TaskJson.writeParts(parts).forEach { (name, value) -> put(if (name == "taskAccountSettings") "accountSettings" else name, value) }
            // ADR 89: the device that wrote the backup, so a restore on a reinstalled phone (a new id) can swap it for itself in the label lists and tag scopes.
            app.taskModel.selfId().takeIf { it != FieldStamp.NOBODY }?.let { put("device", it.lowercase()) }
            return@buildJsonObject
        }

        put("nextTasks", SyncFileJson.writeNextTasks(app.sharedLists.nextTasksForSync()))
        put("comebacks", SyncFileJson.writeParked(app.sharedLists.parkedForSync()))
        put("later", SyncFileJson.writeLater(app.sharedLists.laterForSync()))
        put("categories", SyncFileJson.writeCategories(app.taskCategories.forSync()))
    }

    private fun importTasks(app: CompanionApp, tasks: JsonObject, restore: Boolean): String {
        // Notes merge back like another device's file (a note deleted since stays deleted); they are not part of the task list, so a closed one doesn't block them.
        val noteCount = SessionNotesBackup.read(tasks["sessionNotes"]).also { app.sessionNotes.receive(it) }.count { it.deletedAt == null }
        val notesText = if (noteCount > 0) " $noteCount session note${if (noteCount == 1) "" else "s"} in the backup merged." else ""
        return importTaskLists(app, tasks, restore) + notesText
    }

    private fun importTaskLists(app: CompanionApp, tasks: JsonObject, restore: Boolean): String {
        if (app.taskModel.isClosed) return "The task lists weren't restored: this phone's task list can't be opened right now (it's kept as it was)."
        if (app.taskModel.isActive) return importIntoTaskList(app, tasks, restore)
        val now = Instant.now()
        // Restore: the backup's live items are stamped now, so they win over later edits and deletions; its deletions don't come back.
        val next = SyncFileJson.readNextTasks(tasks["nextTasks"]).orEmpty().let { list -> if (restore) list.filter { it.deletedAt == null }.map { it.copy(editedAt = now) } else list }
        val parked = SyncFileJson.readParked(tasks["comebacks"]).orEmpty().let { list -> if (restore) list.filter { it.deletedAt == null }.map { it.copy(editedAt = now) } else list }
        val later = SyncFileJson.readLater(tasks["later"]).orEmpty().let { list -> if (restore) list.filter { it.deletedAt == null }.map { it.copy(editedAt = now) } else list }
        if (app.sharedLists.receive(next, parked, later)) app.sharedLists.onChanged()
        SyncFileJson.readCategories(tasks["categories"]).orEmpty().forEach { category ->
            val taken = app.taskCategories.categories.value.map { it.color }
            val added = app.taskCategories.add(category.name, taken)
            if (restore && !added.color.equals(category.color, ignoreCase = true)) app.taskCategories.recolor(added.name, category.color)
        }
        val count = next.count { it.deletedAt == null } + parked.count { it.deletedAt == null } + later.count { it.deletedAt == null }
        return if (restore) "Task lists restored ($count items from the backup)." else "Task lists merged ($count items in the backup; the newest edit of each kept)."
    }

    // R7.2: a backup taken into the task list through its merge rules (a backup with the task list) or, an older backup's old lists,
    // as this phone's edits (Restore: every live item as the backup has it; Merge: only items the task list has never held).
    private fun importIntoTaskList(app: CompanionApp, tasks: JsonObject, restore: Boolean): String {
        val store = app.taskModel
        val named = JsonObject(tasks + listOfNotNull(tasks["accountSettings"]?.let { "taskAccountSettings" to it }))
        val parts = com.accountabilibuddy.shared.tasks.TaskJson.readParts(named)
        val count: Int
        if (parts.tasks != null) {
            count = store.import(
                com.accountabilibuddy.shared.tasks.TaskLists(parts.tasks!!, parts.labels.orEmpty(), parts.views.orEmpty(), parts.taskLog.orEmpty(), parts.accountSettings, parts.tagScopes),
                restore,
                (tasks["device"] as? kotlinx.serialization.json.JsonPrimitive)?.contentOrNull,
            )
        } else {
            val known = store.lists.value.tasks.map { it.id.lowercase() }.toSet()
            fun <T : com.accountabilibuddy.shared.sync.SharedItem> wanted(list: List<T>) = list.filter { it.deletedAt == null && (restore || it.id.lowercase() !in known) }
            val changed = com.accountabilibuddy.shared.tasks.OldTaskFields(
                wanted(SyncFileJson.readNextTasks(tasks["nextTasks"]).orEmpty()), wanted(SyncFileJson.readLater(tasks["later"]).orEmpty()),
                wanted(SyncFileJson.readParked(tasks["comebacks"]).orEmpty()), emptyList(), emptyList(), emptyList(),
            )
            count = changed.nextTasks.size + changed.later.size + changed.parked.size
            store.change { lists, self, now -> com.accountabilibuddy.shared.tasks.TaskLocalEdits.apply(lists, changed, self, now, store.zone(), removed = store.removed()) }
        }

        SyncFileJson.readCategories(tasks["categories"]).orEmpty().forEach { category ->
            val taken = app.taskCategories.categories.value.map { it.color }
            val added = app.taskCategories.add(category.name, taken)
            if (restore && !added.color.equals(category.color, ignoreCase = true)) app.taskCategories.recolor(added.name, category.color)
        }
        app.sharedLists.onChanged()
        return if (restore) "Task lists restored ($count tasks from the backup)." else "Task lists merged ($count tasks in the backup; the newest edit of each kept)."
    }

    private fun readFile(app: CompanionApp, name: String): JsonElement? =
        File(app.filesDir, "companion/$name").takeIf { it.exists() }?.let { runCatching { Json.parseToJsonElement(it.readText()) }.getOrNull() }
}
