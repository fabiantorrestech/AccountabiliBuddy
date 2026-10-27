package com.accountabilibuddy.companion.sync

import android.content.Context
import com.accountabilibuddy.shared.sync.SyncAction
import com.accountabilibuddy.shared.sync.SyncActionResult
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
 * Since ADR 42 also start, end early and day plan edits, whose answers ([SyncActionResult]) go to [onAnswered].
 */
class ActionStore(context: Context) {
    private val prefs = context.getSharedPreferences("actions", Context.MODE_PRIVATE)
    private val _pending = MutableStateFlow(read())

    /** Waiting for their PC. */
    val pending: StateFlow<List<SyncAction>> = _pending.asStateFlow()

    /** Set by the app: sends the files at once (Drive) and redraws. */
    var onChanged: () -> Unit = {}

    /**
     * Called for each action a PC acknowledged, with its answer from the same file (ADR 42); null from a PC that acks
     * without answering (before 0.1.107). Set by the app.
     */
    var onAnswered: (action: SyncAction, result: SyncActionResult?, pc: ReceivedDevice) -> Unit = { _, _, _ -> }

    /** Adds an action for [target] (a new id each time, also when sent again) and returns it. */
    fun add(kind: String, target: UUID, item: String? = null, text: String? = null): SyncAction {
        val action = SyncAction(UUID.randomUUID().toString(), kind, target, item, text, Instant.now())
        // Locked like prune, so an action added while answers are handled isn't lost.
        synchronized(this) { save(_pending.value + action) }
        onChanged()
        return action
    }

    /** Takes an action out of this phone's file before its PC answered (cancelled or given up on). */
    fun remove(id: String) {
        val removed = synchronized(this) {
            if (_pending.value.none { it.id == id }) {
                false
            } else {
                save(_pending.value.filterNot { it.id == id })
                true
            }
        }
        if (removed) {
            onChanged()
        }
    }

    /** True while this phone has asked [target] to tick [item] and it hasn't answered yet (shown ticked meanwhile). */
    fun isTicking(target: UUID, item: String?): Boolean =
        item != null && _pending.value.any { it.target == target && it.item == item && it.kind != SyncAction.PARK }

    /**
     * Drops what the PCs acknowledged in their files, and anything older than 3 days. Each acknowledged action goes to
     * [onAnswered] with its result first (the PC drops the result once the action leaves this phone's file).
     */
    @Synchronized
    fun prune(received: List<ReceivedDevice>, now: Instant = Instant.now()) {
        val byPc = received.associateBy { it.file.device.deviceId }
        val answered = _pending.value.filter { action -> byPc[action.target]?.file?.acks.orEmpty().contains(action.id) }
        // Answers first, so what they change is in place before the action leaves the file (nothing is sent twice).
        answered.forEach { action ->
            val pc = byPc.getValue(action.target)
            onAnswered(action, pc.file.actionResults?.firstOrNull { it.id == action.id }, pc)
        }

        val answeredIds = answered.map { it.id }.toSet()
        val kept = _pending.value.filter { it.id !in answeredIds && Duration.between(it.at, now) < KEPT }
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

/** The sync device id of the PC [name] in the received files. */
fun deviceIdOf(name: String?, received: List<ReceivedDevice>): UUID? =
    received.firstOrNull { it.file.device.kind == com.accountabilibuddy.shared.sync.SyncDeviceCard.KIND_PC && it.file.device.name == name }
        ?.file?.device?.deviceId
