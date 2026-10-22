package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.companion.data.JsonValues.string
import com.accountabilibuddy.shared.sync.SyncFileJson
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.io.File
import java.time.Instant
import java.util.Base64

/**
 * The other devices' parts this phone received (ADR 22), one merged copy per device ([ReceivedDevice]), kept in
 * sync-received.json in the app's private folder. Received sessions are kept 90 days ([SyncCarry.accept] merges with
 * [com.accountabilibuddy.shared.sync.SyncMerge]); leaving the group clears them.
 */
class ReceivedDevicesStore(private val file: File) {
    private val _devices = MutableStateFlow(load())

    val devices: StateFlow<List<ReceivedDevice>> = _devices.asStateFlow()

    @Synchronized
    fun save(devices: List<ReceivedDevice>) {
        val json = buildJsonObject {
            put("devices", buildJsonArray {
                devices.forEach { device ->
                    add(buildJsonObject {
                        put("file", SyncFileJson.write(device.file))
                        put("receivedAt", device.receivedAt.toString())
                        put("method", device.method)
                        put("sealed", Base64.getEncoder().encodeToString(device.sealed))
                    })
                }
            })
        }
        file.parentFile?.mkdirs()
        val temporary = File(file.parentFile, "${file.name}.tmp")
        temporary.writeText(json.toString())
        if (!temporary.renameTo(file)) {
            file.delete()
            temporary.renameTo(file)
        }

        _devices.value = devices
    }

    @Synchronized
    fun clear() {
        file.delete()
        _devices.value = emptyList()
    }

    // A copy that can't be read is left out (its device sends a whole file again next time).
    private fun load(): List<ReceivedDevice> {
        if (!file.exists()) {
            return emptyList()
        }

        val json = runCatching { Json.parseToJsonElement(file.readText()) as JsonObject }.getOrNull() ?: return emptyList()
        return (json["devices"] as? JsonArray).orEmpty().mapNotNull { element ->
            runCatching {
                val device = element as JsonObject
                ReceivedDevice(
                    file = SyncFileJson.read(device["file"] as JsonObject)!!,
                    receivedAt = Instant.parse(device.string("receivedAt")!!),
                    method = device.string("method")!!,
                    sealed = Base64.getDecoder().decode(device.string("sealed")!!),
                )
            }.getOrNull()
        }
    }
}
