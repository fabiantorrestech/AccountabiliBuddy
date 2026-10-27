package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncDocument
import com.accountabilibuddy.shared.sync.SyncFile
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.put

/**
 * The PC's settings from its sync file (ADR 40, 45): its profiles with the day plan, sleep and day bar documents,
 * calendar events and the plan's version, in the shape of a backup's content, so the phone imports them the same way.
 * The PC is the one the settings were last taken from (by name) when its file is here, else the PC whose file is newest.
 */
object PcFileSettings {
    /** The PC file to take settings from, or null when no PC file carries them. */
    fun choose(files: List<SyncFile>, pairedName: String?): SyncFile? {
        val pcs = files.filter { it.device.kind == SyncDeviceCard.KIND_PC && it.documents.any { d -> d.key == SyncDocument.PROFILES_KEY } }
        return pcs.firstOrNull { pairedName != null && it.device.name == pairedName } ?: pcs.maxByOrNull { it.device.writtenAt }
    }

    /** The backup-shaped content of [file], or null when it has no profiles. */
    fun content(file: SyncFile): JsonObject? {
        val profiles = file.documents.firstOrNull { it.key == SyncDocument.PROFILES_KEY } ?: return null
        val profilesJson = runCatching { Json.parseToJsonElement(profiles.json).jsonObject }.getOrNull() ?: return null
        // The plan travels beside the profiles in the file; the import wants it inside, as in a backup.
        val withPlan = file.dayPlan?.let { plan ->
            runCatching { JsonObject(profilesJson + ("dayPlan" to Json.parseToJsonElement(plan.planJson))) }.getOrNull()
        } ?: profilesJson
        fun doc(key: String) = file.documents.firstOrNull { it.key == key }?.let { runCatching { Json.parseToJsonElement(it.json).jsonObject }.getOrNull() }
        return buildJsonObject {
            put("profiles", withPlan.toString())
            put("settings", buildJsonObject {
                doc(SyncDocument.SLEEP_KEY)?.let { put("sleep", it) }
                doc(SyncDocument.DAY_BAR_KEY)?.let { put("daybar", it) }
            })
            file.events?.let { put("calendarEvents", it) }
            file.dayPlan?.let { plan ->
                put("dayPlanVersion", plan.version)
                put("dayPlanEditedAt", plan.editedAt.toString())
            }
        }
    }

    /** A fingerprint of what [content] would import, so an unchanged file isn't imported again. */
    fun fingerprint(content: JsonObject): String = content.toString().hashCode().toString(16) + ":" + content.toString().length
}
