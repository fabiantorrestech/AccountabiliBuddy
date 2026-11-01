package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.companion.data.JsonValues.string
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.put
import java.io.File
import java.time.Instant
import java.util.UUID

/**
 * A device removed from the group (ADR 54). Its files are refused until one opens with the group's new key, which means it
 * was linked again.
 *
 * @property name The name it had when it was removed, for "Pixel 7 (removed)".
 * @property removedAt When this phone learned of the removal.
 * @property dataChoice What to do with the data received from it: [KEEP_DATA] or [ERASE_DATA]; null before the user chose
 * (ADR 55). Set by the remove dialog.
 */
data class RemovedDevice(val deviceId: UUID, val name: String, val removedAt: Instant, val dataChoice: String? = null) {
    companion object {
        const val KEEP_DATA = "keep"
        const val ERASE_DATA = "erase"
    }
}

/**
 * This phone found itself removed from its device group (ADR 54), or cut off by a removal: it has stopped syncing and waits
 * for the user's choice.
 *
 * @property removed True when this phone was named in the rekey; false when it was left out of it (it had no public key to
 * wrap the new code for) and is cut off all the same.
 * @property removedByName The name of the device that removed it, as that device wrote it in the rekey file.
 */
data class UnlinkedState(val at: Instant, val removed: Boolean, val removedByName: String)

/**
 * The devices removed from the group and whether this phone is unlinked (ADR 54), kept in sync-removed.json beside
 * sync.json in the app's private folder. Nothing in it is secret: ids, names and times.
 */
class RemovedDevicesStore(private val file: File) {
    private val _devices = MutableStateFlow<List<RemovedDevice>>(emptyList())
    private val _unlinked = MutableStateFlow<UnlinkedState?>(null)

    /** The removed devices, in the order they were recorded. */
    val devices: StateFlow<List<RemovedDevice>> = _devices.asStateFlow()

    /** Set while this phone is unlinked; null otherwise. */
    val unlinked: StateFlow<UnlinkedState?> = _unlinked.asStateFlow()

    init {
        load()
    }

    /** Called after the removed list changed (a device recorded or cleared); set by the coordinator so the active list follows. */
    var onChanged: (() -> Unit)? = null

    /**
     * Adds the devices not already recorded (a repeated rekey says the same again), removed at [removedAt] (the rekey's time), with
     * the remover's [choices] of keep or erase for their data (ADR 55; none for a device not named there).
     */
    @Synchronized
    fun record(removed: List<Pair<UUID, String>>, removedAt: Instant, choices: Map<UUID, String> = emptyMap()) {
        val known = _devices.value.map { it.deviceId }.toSet()
        _devices.value = _devices.value + removed.filter { it.first !in known }.map { RemovedDevice(it.first, it.second, removedAt, choices[it.first]) }
        save()
        onChanged?.invoke()
    }

    /** A removed device wrote a file that opens with the new key: it was linked again. */
    @Synchronized
    fun clear(deviceId: UUID) {
        if (_devices.value.any { it.deviceId == deviceId }) {
            _devices.value = _devices.value.filter { it.deviceId != deviceId }
            save()
            onChanged?.invoke()
        }
    }

    @Synchronized
    fun setDataChoice(deviceId: UUID, choice: String) {
        _devices.value = _devices.value.map { if (it.deviceId == deviceId) it.copy(dataChoice = choice) else it }
        save()
    }

    @Synchronized
    fun setUnlinked(state: UnlinkedState?) {
        _unlinked.value = state
        save()
    }

    private fun load() {
        val json = runCatching { Json.parseToJsonElement(file.readText()) as JsonObject }.getOrNull() ?: return
        _devices.value = (json["devices"] as? JsonArray).orEmpty().mapNotNull { element ->
            runCatching {
                val device = element as JsonObject
                RemovedDevice(
                    UUID.fromString(device.string("deviceId")!!),
                    device.string("name")!!,
                    Instant.parse(device.string("removedAt")!!),
                    device.string("dataChoice"),
                )
            }.getOrNull()
        }
        _unlinked.value = (json["unlinked"] as? JsonObject)?.let { unlinked ->
            runCatching {
                UnlinkedState(
                    Instant.parse(unlinked.string("at")!!),
                    (unlinked["removed"] as? JsonPrimitive)?.booleanOrNull == true,
                    unlinked.string("removedByName") ?: "another device",
                )
            }.getOrNull()
        }
    }

    private fun save() {
        val json = buildJsonObject {
            put("devices", buildJsonArray {
                _devices.value.forEach { device ->
                    add(buildJsonObject {
                        put("deviceId", device.deviceId.toString())
                        put("name", device.name)
                        put("removedAt", device.removedAt.toString())
                        device.dataChoice?.let { put("dataChoice", it) }
                    })
                }
            })
            _unlinked.value?.let { unlinked ->
                put("unlinked", buildJsonObject {
                    put("at", unlinked.at.toString())
                    put("removed", unlinked.removed)
                    put("removedByName", unlinked.removedByName)
                })
            }
        }
        file.parentFile?.mkdirs()
        val temporary = File(file.parentFile, "${file.name}.tmp")
        temporary.writeText(json.toString())
        if (!temporary.renameTo(file)) {
            file.delete()
            temporary.renameTo(file)
        }
    }
}
