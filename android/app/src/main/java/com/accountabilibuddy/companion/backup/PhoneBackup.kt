package com.accountabilibuddy.companion.backup

import android.content.Context
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull
import kotlinx.serialization.json.put
import java.io.File
import java.time.Instant

/**
 * The phone's settings as one JSON file (Settings → Backup: export, import, auto-backup): the app's own preferences,
 * the phone's settings files and its guard history ([GuardHistoryBackup]: times and kinds only). Secrets never go in it (the device group's link
 * code, the Drive sign-in, which Google Play services keeps, sealed tokens), nor reasons and notes, which never leave the phone, nor what comes
 * back by itself (the PC's data, received sync files, today's state such as a lunch or pending actions). Folder permissions (the noise and sync
 * folders) need choosing again on another phone. Choosing what to restore: [BackupGroup]. New parts are additive, so [FORMAT] stays 1: older files
 * still import, and older apps ignore what they don't know.
 */
object PhoneBackup {
    const val FORMAT = 1
    const val MIME = "application/json"

    /** Preference files backed up: the user's settings. */
    val PREFS = listOf(
        "ui", "emergency", "essentials", "bedtimeEssentials", "headphones", "noise", "pause", "startup", "nudges", "takeover", "smartBedtime", "lunchSettings",
        "timers", "curbox", "blockGuard", "lunchLockLift", "phoneDown",
    )

    /** The preference files whose changes start an auto-backup. Not the ones the app writes on its own while running. */
    val WATCHED_PREFS = PREFS - setOf("phoneDown", "curbox", "blockGuard", "lunchLockLift") + setOf("workUse", "over_limit")

    // Keys left out of those files: today's state, not settings.
    private val SKIPPED_KEYS = mapOf(
        // The After sunset place is private (ADR 49): sealed to this phone's Keystore, never in a backup file.
        "ui" to setOf("place"),
        "takeover" to setOf("lastShown", "snoozedUntil", "startingUntil", "snoozeDay", "snoozes", "dayOffAskedAt", "dayOff", "unanswered"),
        "nudges" to setOf("lastNudge"),
        "emergency" to setOf("uses"),
    )

    // Files where only these keys are settings; the rest is the app's running state or sealed.
    private val ONLY_KEYS = mapOf(
        "curbox" to setOf("hardMode", "focusGroup"),
        "blockGuard" to setOf("on", "known"),
        "lunchLockLift" to setOf("on"),
        "phoneDown" to com.accountabilibuddy.companion.placement.PocketPenalty.entries.map { it.keyOn }.toSet(),
    )

    private fun backedUp(name: String, key: String): Boolean =
        key !in SKIPPED_KEYS[name].orEmpty() && ONLY_KEYS[name]?.let { key in it } != false

    /** Settings files under files/companion backed up (the phone's own; the PC's data and secrets aren't). */
    val FILES = listOf("phone.json", "sleep.json", "sync-settings.json", "watch.json")
    private const val SYNC_SETTINGS = "sync-settings.json"
    private const val HISTORY = "history"
    private const val NOT_A_BACKUP = "That isn't an AccountabiliBuddy phone settings file."

    fun export(context: Context, appVersion: String): String {
        val json = buildJsonObject {
            put("format", FORMAT)
            put("app", "AccountabiliBuddy phone settings")
            put("appVersion", appVersion)
            put("createdAt", Instant.now().toString())
            put("prefs", buildJsonObject {
                PREFS.forEach { name ->
                    val values = context.getSharedPreferences(name, Context.MODE_PRIVATE).all.filterKeys { backedUp(name, it) }
                    if (values.isNotEmpty()) {
                        put(name, buildJsonObject { values.forEach { (key, value) -> encode(value)?.let { put(key, it) } } })
                    }
                }
            })
            put("files", buildJsonObject {
                FILES.forEach { name ->
                    val file = File(context.filesDir, "companion/$name")
                    if (file.exists()) {
                        runCatching { Json.parseToJsonElement(file.readText()) }.getOrNull()?.let { put(name, it) }
                    }
                }
            })
            put(HISTORY, GuardHistoryBackup.export(context))
        }
        return json.toString()
    }

    /** Why [text] can't be restored now (a focus session, not a backup, or a newer format); null when it can. */
    fun importProblem(context: Context, text: String): String? {
        (context.applicationContext as com.accountabilibuddy.companion.CompanionApp).sync.removalLockedReason()?.let { return it }
        val json = runCatching { Json.parseToJsonElement(text).jsonObject }.getOrNull() ?: return NOT_A_BACKUP
        val format = runCatching { json.getValue("format").jsonPrimitive.int }.getOrNull() ?: return NOT_A_BACKUP
        return if (format > FORMAT) "That file is from a newer version of the app. Update the app first." else null
    }

    /** The groups [text] holds something for, in the order they are shown: a file from an older version shows only what it has. */
    fun groupsIn(text: String): List<BackupGroup> {
        val json = runCatching { Json.parseToJsonElement(text).jsonObject }.getOrNull() ?: return emptyList()
        val prefs = (json["prefs"] as? JsonObject)?.keys.orEmpty()
        val files = (json["files"] as? JsonObject)?.keys.orEmpty()
        return BackupGroup.entries.filter { group ->
            group.prefs.any { it in prefs } || group.files.any { it in files } || (group == BackupGroup.GuardHistory && json[HISTORY] is JsonObject)
        }
    }

    /** The groups that can't be left out: guard history holding today's uses, which leaving out would reset today's allowance. */
    fun requiredGroups(text: String, today: java.time.LocalDate = java.time.LocalDate.now()): Set<BackupGroup> {
        val json = runCatching { Json.parseToJsonElement(text).jsonObject }.getOrNull() ?: return emptySet()
        return if (GuardHistoryBackup.holdsToday(json[HISTORY] as? JsonObject, today)) setOf(BackupGroup.GuardHistory) else emptySet()
    }

    /** "From app 0.1.70, Nov 3, 1:05 PM" for the restore screen. */
    fun describe(text: String): String {
        val json = runCatching { Json.parseToJsonElement(text).jsonObject }.getOrNull() ?: return ""
        val version = json["appVersion"]?.jsonPrimitive?.content
        val created = json["createdAt"]?.jsonPrimitive?.content?.let { runCatching { Instant.parse(it) }.getOrNull() }
            ?.atZone(java.time.ZoneId.systemDefault())?.format(java.time.format.DateTimeFormatter.ofPattern("MMM d, h:mm a", java.util.Locale.US))
        return listOfNotNull(created, version?.let { "app $it" }).joinToString(" · ").let { if (it.isEmpty()) it else "Backup from $it" }
    }

    /**
     * Writes a backup's chosen parts over this phone's ([groups]; what belongs to no group always comes back). Null when done; otherwise why not.
     * The app restarts after. Locked during a focus session the user started, like every other restore: a file could carry a paused guard or
     * switched-off sharing. Read here, first, so no screen can get around it; exporting and auto-backup are not affected.
     */
    fun import(context: Context, text: String, groups: Set<BackupGroup> = BackupGroup.entries.toSet()): String? {
        importProblem(context, text)?.let { return it }
        val keepSync = keepsSyncSettings(context, text)
        val json = Json.parseToJsonElement(text).jsonObject
        // Enforced here too: no screen can leave out what holds today's uses.
        val chosen = groups + requiredGroups(text)

        json["prefs"]?.jsonObject?.forEach { (name, values) ->
            if (name !in PREFS) return@forEach
            val group = BackupGroup.ofPrefs(name)
            if (group != null && group !in chosen) return@forEach
            val editor = context.getSharedPreferences(name, Context.MODE_PRIVATE).edit()
            values.jsonObject.forEach { (key, value) ->
                // A Phone down penalty only ever comes back on: turning one off waits (PhoneDownStore), and a restore is no way round that.
                val turnsPenaltyOff = name == "phoneDown" && (value as? JsonObject)?.get("bool")?.jsonPrimitive?.booleanOrNull == false
                // Whether first setup was finished belongs to this phone, not to the file.
                val isSetupState = name == "ui" && key == "setupShown"
                if (backedUp(name, key) && !turnsPenaltyOff && !isSetupState) decode(editor, key, value)
            }
            editor.commit()
        }
        json["files"]?.jsonObject?.forEach { (name, value) ->
            val group = BackupGroup.ofFile(name)
            if (group != null && group !in chosen) return@forEach
            // A file that would leave this phone without its way of syncing does not replace the phone's sharing settings (see keepsSyncSettings).
            if (name in FILES && !(keepSync && name == SYNC_SETTINGS)) {
                File(context.filesDir, "companion").mkdirs()
                File(context.filesDir, "companion/$name").writeText(value.toString())
            }
        }
        if (BackupGroup.GuardHistory in chosen) {
            (json[HISTORY] as? JsonObject)?.let { GuardHistoryBackup.merge(context, it) }
        }
        return null
    }

    /**
     * Whether importing [text] leaves this phone's sharing settings (sync-settings.json) as they are: the file would leave none of the ways of
     * syncing that count on, or pointed elsewhere ([com.accountabilibuddy.companion.sync.SyncCoordinator.leavesNoWay]). Importing is not a way
     * round the 10 minutes turning off the last one waits. The rest of the file is imported as usual.
     */
    fun keepsSyncSettings(context: Context, text: String): Boolean {
        val file = runCatching { Json.parseToJsonElement(text).jsonObject["files"]?.jsonObject?.get(SYNC_SETTINGS)?.jsonObject }.getOrNull() ?: return false
        val sync = (context.applicationContext as com.accountabilibuddy.companion.CompanionApp).sync
        return sync.leavesNoWay(com.accountabilibuddy.companion.sync.PhoneSyncSettingsStore.parse(file))
    }

    // A preference value with its type, so it reads back the same.
    private fun encode(value: Any?): JsonElement? = when (value) {
        is Boolean -> buildJsonObject { put("bool", value) }
        is Int -> buildJsonObject { put("int", value) }
        is Long -> buildJsonObject { put("long", value) }
        is Float -> buildJsonObject { put("float", value) }
        is String -> buildJsonObject { put("string", value) }
        is Set<*> -> buildJsonObject { put("set", JsonArray(value.filterIsInstance<String>().map { JsonPrimitive(it) })) }
        else -> null
    }

    private fun decode(editor: android.content.SharedPreferences.Editor, key: String, value: JsonElement) {
        val o = value as? JsonObject ?: return
        o["bool"]?.jsonPrimitive?.booleanOrNull?.let { editor.putBoolean(key, it); return }
        o["int"]?.jsonPrimitive?.longOrNull?.let { editor.putInt(key, it.toInt()); return }
        o["long"]?.jsonPrimitive?.longOrNull?.let { editor.putLong(key, it); return }
        o["float"]?.jsonPrimitive?.doubleOrNull?.let { editor.putFloat(key, it.toFloat()); return }
        o["string"]?.jsonPrimitive?.let { editor.putString(key, it.content); return }
        o["set"]?.jsonArray?.let { set -> editor.putStringSet(key, set.map { it.jsonPrimitive.content }.toSet()) }
    }
}
