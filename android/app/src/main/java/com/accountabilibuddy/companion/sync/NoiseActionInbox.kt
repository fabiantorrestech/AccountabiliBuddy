package com.accountabilibuddy.companion.sync

import android.content.Context
import com.accountabilibuddy.companion.noise.NoisePlayer
import com.accountabilibuddy.shared.sync.SyncAction
import com.accountabilibuddy.shared.sync.SyncActionResult
import com.accountabilibuddy.shared.sync.SyncNoiseOps
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.time.Duration
import java.time.Instant
import java.util.UUID

/**
 * The noise commands the other devices send this phone (ADR 92): `noise` actions in their sync files whose target is this
 * phone, applied once each by the player's own rules (pausing always; playing not while the headphone lock blocks it, nor
 * without sounds). Fresh for 2 minutes from when this phone first saw one, and never one made more than a day ago, as on the
 * PC (ADR 42). What it did goes back in this phone's file: the ids as `acks`, kept 14 days, and an answer for each while the
 * sender still lists the action (prefs "noiseInbox").
 */
class NoiseActionInbox(
    context: Context,
    private val received: StateFlow<List<ReceivedDevice>>,
    private val selfId: () -> UUID,
    private val selfName: () -> String,
    private val player: () -> NoisePlayer?,
    private val onApplied: () -> Unit,
    private val scope: CoroutineScope,
) {
    private val prefs = context.getSharedPreferences("noiseInbox", Context.MODE_PRIVATE)
    private val lock = Mutex()
    private val firstSeen = mutableMapOf<String, Instant>()

    @Volatile
    private var applied: List<Applied> = read()

    @Volatile
    private var lastCommandAt: Instant? = null

    /** Applies new commands whenever another device's file arrives. */
    fun start() {
        scope.launch { received.collect { devices -> runCatching { check(devices) } } }
    }

    /** The command ids applied in the last 14 days, for this phone's file. */
    fun acks(): List<String> = applied.map { it.id }

    /** The answers for the commands their senders still list (the sender stops listing one once it sees the ack). */
    fun results(): List<SyncActionResult> {
        val listed = received.value.flatMap { it.file.actions.orEmpty() }.map { it.id }.toSet()
        return applied.filter { it.id in listed }.map { SyncActionResult(it.id, it.ok, it.status, it.message) }
    }

    /** True for 10 minutes after a command arrived, so this phone keeps syncing at its busy pace for the next one. */
    fun recentlyCommanded(now: Instant = Instant.now()): Boolean = lastCommandAt?.let { Duration.between(it, now) < FAST_AFTER } == true

    private suspend fun check(devices: List<ReceivedDevice>) = lock.withLock {
        val self = selfId()
        val now = Instant.now()
        val done = applied.map { it.id }.toSet()
        val waiting = devices
            .filter { it.file.device.deviceId != self }
            .flatMap { it.file.actions.orEmpty() }
            .filter { it.target == self && it.kind == SyncAction.NOISE && it.id !in done }
        if (waiting.isEmpty()) return@withLock

        val answers = waiting.map { action -> answer(action, now) }
        applied = (applied + answers).filter { Duration.between(it.at, now) < KEPT_FOR }
        save(applied)
        lastCommandAt = now
        onApplied()
    }

    private suspend fun answer(action: SyncAction, now: Instant): Applied {
        val seen = firstSeen.getOrPut(action.id) { now }
        if (Duration.between(seen, now) > FRESH_FOR || Duration.between(action.at, now) > MADE_TOO_LONG_AGO) {
            return Applied(action.id, now, false, SyncActionResult.STATUS_LATE, "That came too late, so nothing was changed.")
        }

        val request = runCatching { Json.parseToJsonElement(action.text.orEmpty()).jsonObject }.getOrNull()
        val op = (request?.get("op") as? JsonPrimitive)?.content
        val value = (request?.get("value") as? JsonPrimitive)?.doubleOrNull
        val valid = when (op) {
            SyncNoiseOps.TOGGLE, SyncNoiseOps.NEXT, SyncNoiseOps.PREV, SyncNoiseOps.PLAY, SyncNoiseOps.PAUSE -> true
            SyncNoiseOps.VOLUME -> value != null && value in 0.0..1.0
            else -> false
        }
        if (!valid) {
            return Applied(action.id, now, false, SyncActionResult.STATUS_INVALID, "${selfName()} couldn't read this request.")
        }

        // The player belongs to the main thread.
        val error = withContext(Dispatchers.Main) { apply(op!!, value) }
        return if (error == null) {
            Applied(action.id, now, true, SyncActionResult.STATUS_APPLIED, null)
        } else {
            Applied(action.id, now, false, SyncActionResult.STATUS_REFUSED, error)
        }
    }

    // By the player's own buttons' rules; null when done, otherwise why not.
    private fun apply(op: String, value: Double?): String? {
        val noise = player() ?: return "${selfName()} isn't ready yet."
        val state = noise.state.value
        if (!state.hasFolder || state.fileCount == 0) {
            return "${selfName()} has no noise sounds to play."
        }

        val wantsPlay = op == SyncNoiseOps.PLAY || (op == SyncNoiseOps.TOGGLE && !state.playing)
        if (wantsPlay && !state.playing && noise.lockedOut()) {
            return "Connect headphones on ${selfName()}"
        }

        when (op) {
            SyncNoiseOps.TOGGLE -> noise.toggle()
            SyncNoiseOps.PLAY -> if (!state.playing) noise.play()
            SyncNoiseOps.PAUSE -> if (state.playing) noise.pause()
            SyncNoiseOps.NEXT -> noise.next()
            SyncNoiseOps.PREV -> noise.previous()
            SyncNoiseOps.VOLUME -> noise.setVolume(Math.round(value!! * 100).toInt().coerceIn(0, 100))
        }

        return null
    }

    private fun save(list: List<Applied>) {
        val json = buildJsonArray {
            list.forEach { item ->
                add(buildJsonObject {
                    put("id", item.id)
                    put("at", item.at.toEpochMilli())
                    put("ok", item.ok)
                    put("status", item.status)
                    item.message?.let { put("message", it) }
                })
            }
        }
        prefs.edit().putString("applied", json.toString()).apply()
    }

    private fun read(): List<Applied> = runCatching {
        Json.parseToJsonElement(prefs.getString("applied", null) ?: return emptyList()).jsonArray.map { element ->
            val item = element.jsonObject
            Applied(
                item.getValue("id").jsonPrimitive.content,
                Instant.ofEpochMilli(item.getValue("at").jsonPrimitive.content.toLong()),
                item.getValue("ok").jsonPrimitive.content.toBoolean(),
                item.getValue("status").jsonPrimitive.content,
                (item["message"] as? JsonPrimitive)?.content,
            )
        }
    }.getOrDefault(emptyList())

    private data class Applied(val id: String, val at: Instant, val ok: Boolean, val status: String, val message: String?)

    private companion object {
        val FRESH_FOR: Duration = Duration.ofMinutes(2)
        val MADE_TOO_LONG_AGO: Duration = Duration.ofDays(1)
        val KEPT_FOR: Duration = Duration.ofDays(14)
        val FAST_AFTER: Duration = Duration.ofMinutes(10)
    }
}
