package com.accountabilibuddy.companion.backup

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.data.BackupFile
import com.accountabilibuddy.companion.data.ImportedData
import com.accountabilibuddy.companion.data.ProfileDocumentParser
import com.accountabilibuddy.companion.data.SettingsJson
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
 */
object PhoneKindBackup {
    enum class Kind(val written: String, val extension: String, val title: String) {
        Synced("synced", ".absettings", "Synced settings"),
        Device("device", ".abdevice", "This phone's settings"),
        Tasks("tasks", ".abtasks", "Task lists"),
    }

    private const val PLATFORM = "phone"

    /** The backup file for [kind]; with [password], encrypted. */
    fun export(app: CompanionApp, kind: Kind, password: String? = null): String = BackupFile.seal(content(app, kind), password)

    /** The content of [kind]'s backup (what goes inside the file). */
    fun content(app: CompanionApp, kind: Kind): JsonObject = buildJsonObject {
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
                if (app.sync.sharesTaskLists()) put("tasks", tasks(app))
            }
            Kind.Device -> put("device", Json.parseToJsonElement(PhoneBackup.export(app, app.appVersion(), forDeviceKind = true)))
            Kind.Tasks -> put("tasks", tasks(app))
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
            Kind.Tasks -> importTasks(app, content["tasks"] as? JsonObject ?: return "The backup has no task lists.", restore)
        }
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
        return notes.joinToString(" ")
    }

    private fun tasks(app: CompanionApp): JsonObject = buildJsonObject {
        put("nextTasks", SyncFileJson.writeNextTasks(app.sharedLists.nextTasksForSync()))
        put("comebacks", SyncFileJson.writeParked(app.sharedLists.parkedForSync()))
        put("later", SyncFileJson.writeLater(app.sharedLists.laterForSync()))
        put("categories", SyncFileJson.writeCategories(app.taskCategories.forSync()))
    }

    private fun importTasks(app: CompanionApp, tasks: JsonObject, restore: Boolean): String {
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

    private fun readFile(app: CompanionApp, name: String): JsonElement? =
        File(app.filesDir, "companion/$name").takeIf { it.exists() }?.let { runCatching { Json.parseToJsonElement(it.readText()) }.getOrNull() }
}
