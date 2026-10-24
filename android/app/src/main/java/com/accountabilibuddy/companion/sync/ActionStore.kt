package com.accountabilibuddy.companion.sync

import android.content.Context
import com.accountabilibuddy.shared.sync.SyncAction
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.time.Duration
import java.time.Instant
import java.util.UUID

/**
 * What the user did on this phone for a PC (ADR 26: tick a task, park a thought, tick a come-back), waiting in this
 * phone's sync file until that PC lists the id in its acks (prefs "actions"). Dropped after 3 days unacknowledged.
 */
class ActionStore(context: Context) {
    private val prefs = context.getSharedPreferences("actions", Context.MODE_PRIVATE)
    private val _pending = MutableStateFlow(read())

    /** Waiting for their PC. */
    val pending: StateFlow<List<SyncAction>> = _pending.asStateFlow()

    /** Set by the app: sends the files at once (link, Drive) and redraws. */
    var onChanged: () -> Unit = {}

    fun add(kind: String, target: UUID, item: String? = null, text: String? = null) {
        val action = SyncAction(UUID.randomUUID().toString(), kind, target, item, text, Instant.now())
        save(_pending.value + action)
        onChanged()
    }

    /** True while this phone has asked [target] to tick [item] and it hasn't answered yet (shown ticked meanwhile). */
    fun isTicking(target: UUID, item: String?): Boolean =
        item != null && _pending.value.any { it.target == target && it.item == item && it.kind != SyncAction.PARK }

    /** Drops what the PCs acknowledged in their files, and anything older than 3 days. */
    fun prune(received: List<ReceivedDevice>, now: Instant = Instant.now()) {
        val acked = received.flatMap { device -> device.file.acks.orEmpty().map { device.file.device.deviceId to it } }.toSet()
        val kept = _pending.value.filter { (it.target to it.id) !in acked && Duration.between(it.at, now) < KEPT }
        if (kept.size != _pending.value.size) {
            save(kept)
        }
    }

    private fun save(list: List<SyncAction>) {
        _pending.value = list
        val json = JsonArray(list.map { action ->
            buildJsonObject {
                put("id", action.id)
                put("kind", action.kind)
                put("target", action.target.toString())
                action.item?.let { put("item", it) }
                action.text?.let { put("text", it) }
                put("at", action.at.toString())
            }
        })
        prefs.edit().putString(KEY, json.toString()).apply()
    }

    private fun read(): List<SyncAction> = runCatching {
        val text = prefs.getString(KEY, null) ?: return emptyList()
        (Json.parseToJsonElement(text) as JsonArray).mapNotNull { element ->
            val item = element as? JsonObject ?: return@mapNotNull null
            runCatching {
                SyncAction(
                    item.getValue("id").jsonPrimitive.content,
                    item.getValue("kind").jsonPrimitive.content,
                    UUID.fromString(item.getValue("target").jsonPrimitive.content),
                    (item["item"] as? JsonPrimitive)?.content,
                    (item["text"] as? JsonPrimitive)?.content,
                    Instant.parse(item.getValue("at").jsonPrimitive.content),
                )
            }.getOrNull()
        }
    }.getOrDefault(emptyList())

    private companion object {
        const val KEY = "pending"
        val KEPT: Duration = Duration.ofDays(3)
    }
}

/** The sync device id of the PC [name] in the received files (the linked PC carries its file over the link too). */
fun deviceIdOf(name: String?, received: List<ReceivedDevice>): UUID? =
    received.firstOrNull { it.file.device.kind == com.accountabilibuddy.shared.sync.SyncDeviceCard.KIND_PC && it.file.device.name == name }
        ?.file?.device?.deviceId
