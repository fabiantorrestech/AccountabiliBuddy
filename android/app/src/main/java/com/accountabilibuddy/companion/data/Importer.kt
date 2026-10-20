package com.accountabilibuddy.companion.data

import com.accountabilibuddy.companion.data.JsonValues.obj
import com.accountabilibuddy.companion.data.JsonValues.string
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonObject

/**
 * Turns a file from the PC into [ImportedData]: a backup (*.abbackup, readable or with a password) or a bare
 * profile document (JSON with a "schemaVersion").
 */
object Importer {
    /** Keys of the PC's settings documents the phone uses (they're in backups from Windows 0.1.34 on). */
    const val SLEEP_KEY = "sleep"
    const val DAY_BAR_KEY = "daybar"

    fun needsPassword(text: String): Boolean = BackupFile.isBackup(text) && BackupFile.needsPassword(text)

    /** @throws ImportException with a message for the user. */
    fun read(text: String, password: String? = null): ImportedData {
        if (BackupFile.isBackup(text)) {
            return fromBackup(BackupFile.open(text, password))
        }

        val root = try {
            Json.parseToJsonElement(text) as? JsonObject
        } catch (e: Exception) {
            null
        } ?: throw ImportException("This file isn't an AccountabiliBuddy backup or profile file.")

        if (root["schemaVersion"] == null) {
            throw ImportException("This file isn't an AccountabiliBuddy backup or profile file.")
        }

        return ImportedData(
            document = ProfileDocumentParser.parse(root),
            profilesJson = root,
            sleep = null,
            sleepJson = null,
            dayBar = null,
            dayBarJson = null,
            events = emptyList(),
            eventsJson = null,
            sourceDescription = "Profile file",
        )
    }

    /** The PC's settings sent over the link (ADR 14, L2): a backup's content, without the file around it. */
    fun fromLink(content: JsonObject, pcName: String): ImportedData = fromBackup(content, "Linked PC $pcName")

    private fun fromBackup(content: JsonObject, description: String? = null): ImportedData {
        // The profiles are a JSON document stored as a string inside the backup.
        val profilesText = content.string("profiles")
            ?: throw ImportException("This backup has no profiles. On the PC, export a backup with Profiles ticked.")
        val profilesJson = try {
            Json.parseToJsonElement(profilesText).jsonObject
        } catch (e: Exception) {
            throw ImportException("The backup's profiles are damaged.", e)
        }

        val settings = content.obj("settings")
        val sleepJson = settings?.get(SLEEP_KEY) as? JsonObject
        val dayBarJson = settings?.get(DAY_BAR_KEY) as? JsonObject
        val eventsJson = content["calendarEvents"] as? JsonArray
        val version = content.string("appVersion")
        val created = content.string("createdAt")?.take(10)

        return ImportedData(
            document = ProfileDocumentParser.parse(profilesJson),
            profilesJson = profilesJson,
            sleep = sleepJson?.let(SettingsJson::sleep),
            sleepJson = sleepJson,
            dayBar = dayBarJson?.let(SettingsJson::dayBar),
            dayBarJson = dayBarJson,
            events = eventsJson?.let(SettingsJson::events).orEmpty(),
            eventsJson = eventsJson,
            sourceDescription = description
                ?: listOfNotNull(version?.let { "Windows $it backup" } ?: "PC backup", created?.let { "made $it" }).joinToString(", "),
        )
    }
}
