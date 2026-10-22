package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.companion.data.JsonValues.bool
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.io.File

/**
 * Which ways the phone shares sync files (ADR 22), safest first: QR codes it scans, sync files shared by hand, and the
 * network link to the paired PC. All on by default; each switch is in the Devices tab.
 */
data class PhoneSyncSettings(val qrOn: Boolean = true, val fileOn: Boolean = true, val linkOn: Boolean = true)

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
        return PhoneSyncSettings(json.bool("qrOn", true), json.bool("fileOn", true), json.bool("linkOn", true))
    }
}
