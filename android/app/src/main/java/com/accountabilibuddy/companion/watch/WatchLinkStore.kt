package com.accountabilibuddy.companion.watch

import com.accountabilibuddy.companion.data.JsonValues.bool
import com.accountabilibuddy.companion.data.JsonValues.string
import com.accountabilibuddy.shared.WatchLinkSettings
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.io.File

/** The watch link settings (ADR 21): which watch is linked and which routes may be used. Kept as watch.json. */
class WatchLinkStore(private val file: File) {
    private val _settings = MutableStateFlow(load())

    val settings: StateFlow<WatchLinkSettings> = _settings.asStateFlow()

    fun update(change: (WatchLinkSettings) -> WatchLinkSettings) = synchronized(this) {
        val next = change(_settings.value)
        save(next)
        _settings.value = next
    }

    private fun load(): WatchLinkSettings {
        val json = if (file.exists()) runCatching { Json.parseToJsonElement(file.readText()) as? JsonObject }.getOrNull() else null
        json ?: return WatchLinkSettings()
        val defaults = WatchLinkSettings()
        return WatchLinkSettings(
            linkedNodeId = json.string("linkedNodeId"),
            linkedName = json.string("linkedName"),
            dataLayerOn = json.bool("dataLayerOn", defaults.dataLayerOn),
            relayOn = json.bool("relayOn", defaults.relayOn),
            wifiFallbackOn = json.bool("wifiFallbackOn", defaults.wifiFallbackOn),
        )
    }

    private fun save(settings: WatchLinkSettings) {
        file.parentFile?.mkdirs()
        val temporary = File(file.parentFile, "${file.name}.tmp")
        temporary.writeText(buildJsonObject {
            settings.linkedNodeId?.let { put("linkedNodeId", it) }
            settings.linkedName?.let { put("linkedName", it) }
            put("dataLayerOn", settings.dataLayerOn)
            put("relayOn", settings.relayOn)
            put("wifiFallbackOn", settings.wifiFallbackOn)
        }.toString())
        if (!temporary.renameTo(file)) {
            file.delete()
            temporary.renameTo(file)
        }
    }
}
