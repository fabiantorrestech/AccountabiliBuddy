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
 * The phone's settings as one JSON file (Settings → Backup: export, import, auto-backup): the app's own preferences and
 * the phone's settings files. Secrets never go in it (the device group's link code, the Drive
 * sign-in, which Google Play services keeps), nor what comes back by itself (the PC's data, received sync files,
 * today's state such as a lunch, pending actions or snoozes). Folder permissions (the noise and sync folders) need
 * choosing again on another phone.
 */
object PhoneBackup {
    const val FORMAT = 1
    const val MIME = "application/json"

    /** Preference files backed up: the user's settings. */
    val PREFS = listOf("ui", "emergency", "essentials", "bedtimeEssentials", "headphones", "noise", "pause", "startup", "nudges", "takeover", "smartBedtime", "lunchSettings")

    // Keys left out of those files: today's state, not settings.
    private val SKIPPED_KEYS = mapOf(
        // The After sunset place is private (ADR 49): sealed to this phone's Keystore, never in a backup file.
        "ui" to setOf("place"),
        "takeover" to setOf("lastShown", "snoozedUntil", "startingUntil", "snoozeDay", "snoozes", "dayOffAskedAt", "dayOff"),
        "nudges" to setOf("lastNudge"),
        "emergency" to setOf("uses"),
    )

    /** Settings files under files/companion backed up (the phone's own; the PC's data and secrets aren't). */
    val FILES = listOf("phone.json", "sleep.json", "sync-settings.json", "watch.json")
    private const val SYNC_SETTINGS = "sync-settings.json"

    fun export(context: Context, appVersion: String): String {
        val json = buildJsonObject {
            put("format", FORMAT)
            put("app", "AccountabiliBuddy phone settings")
            put("appVersion", appVersion)
            put("createdAt", Instant.now().toString())
            put("prefs", buildJsonObject {
                PREFS.forEach { name ->
                    val skipped = SKIPPED_KEYS[name].orEmpty()
                    val values = context.getSharedPreferences(name, Context.MODE_PRIVATE).all.filterKeys { it !in skipped }
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
        }
        return json.toString()
    }

    /**
     * Writes a backup's settings over this phone's. Null when done; otherwise why not. The app restarts after. Locked during a focus session
     * the user started, like every other restore: a file could carry a paused guard or switched-off sharing. Read here, first, so no screen
     * can get around it; exporting and auto-backup are not affected.
     */
    fun import(context: Context, text: String): String? {
        (context.applicationContext as com.accountabilibuddy.companion.CompanionApp).sync.removalLockedReason()?.let { return it }
        val keepSync = keepsSyncSettings(context, text)
        val json = runCatching { Json.parseToJsonElement(text).jsonObject }.getOrNull() ?: return "That isn't an AccountabiliBuddy phone settings file."
        val format = runCatching { json.getValue("format").jsonPrimitive.int }.getOrNull() ?: return "That isn't an AccountabiliBuddy phone settings file."
        if (format > FORMAT) {
            return "That file is from a newer version of the app. Update the app first."
        }

        json["prefs"]?.jsonObject?.forEach { (name, values) ->
            if (name !in PREFS) return@forEach
            val skipped = SKIPPED_KEYS[name].orEmpty()
            val editor = context.getSharedPreferences(name, Context.MODE_PRIVATE).edit()
            values.jsonObject.forEach { (key, value) -> if (key !in skipped) decode(editor, key, value) }
            editor.commit()
        }
        json["files"]?.jsonObject?.forEach { (name, value) ->
            // A file that would leave this phone without its way of syncing does not replace the phone's sharing settings (see keepsSyncSettings).
            if (name in FILES && !(keepSync && name == SYNC_SETTINGS)) {
                File(context.filesDir, "companion").mkdirs()
                File(context.filesDir, "companion/$name").writeText(value.toString())
            }
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
