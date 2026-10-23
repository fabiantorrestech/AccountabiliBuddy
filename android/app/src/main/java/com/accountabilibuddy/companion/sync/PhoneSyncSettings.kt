package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.companion.data.JsonValues.bool
import com.accountabilibuddy.companion.data.JsonValues.int
import com.accountabilibuddy.companion.data.JsonValues.string
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.io.File

/**
 * Which ways the phone shares sync files (ADR 22), safest first: QR codes it scans, sync files shared by hand, the
 * network link to the paired PC, and a sync folder another app keeps in step with Google Drive. All but the folder
 * are on by default; each switch is in the Devices tab.
 */
data class PhoneSyncSettings(
    val qrOn: Boolean = true,
    val fileOn: Boolean = true,
    val linkOn: Boolean = true,
    val folderOn: Boolean = false,
    /** The folder chosen with Android's folder picker (a tree uri), or null. */
    val folderUri: String? = null,
    /** 1, 5 or 15: faster than 15 only while the pinned timeline runs. */
    val folderMinutes: Int = 15,
    /** The phone's next alarm is its wake time and goes to the PCs in its sync file (ADR 23); on by default. */
    val alarmOn: Boolean = true,
    /** The app whose alarms are the wake-up alarm, pinned by the user; null: learned ([WakeAlarm]). */
    val alarmApp: String? = null,
    /** Sync through the app's Google Drive folder (ADR 26); on once signed in. */
    val driveOn: Boolean = false,
)

/** [PhoneSyncSettings] in sync-settings.json in the app's private folder. */
class PhoneSyncSettingsStore(private val file: File) {
    private val _settings = MutableStateFlow(load())

    val settings: StateFlow<PhoneSyncSettings> = _settings.asStateFlow()

    @Synchronized
    fun save(settings: PhoneSyncSettings) {
        val json = buildJsonObject {
            put("qrOn", settings.qrOn)
            put("fileOn", settings.fileOn)
            put("linkOn", settings.linkOn)
            put("folderOn", settings.folderOn)
            settings.folderUri?.let { put("folderUri", it) }
            put("folderMinutes", settings.folderMinutes)
            put("alarmOn", settings.alarmOn)
            settings.alarmApp?.let { put("alarmApp", it) }
            put("driveOn", settings.driveOn)
        }
        file.parentFile?.mkdirs()
        val temporary = File(file.parentFile, "${file.name}.tmp")
        temporary.writeText(json.toString())
        if (!temporary.renameTo(file)) {
            file.delete()
            temporary.renameTo(file)
        }

        _settings.value = settings
    }

    private fun load(): PhoneSyncSettings {
        val json = runCatching { Json.parseToJsonElement(file.readText()) as JsonObject }.getOrNull() ?: return PhoneSyncSettings()
        return PhoneSyncSettings(
            json.bool("qrOn", true), json.bool("fileOn", true), json.bool("linkOn", true), json.bool("folderOn", false),
            json.string("folderUri"), json.int("folderMinutes") ?: 15, json.bool("alarmOn", true), json.string("alarmApp"),
            json.bool("driveOn", false),
        )
    }
}
