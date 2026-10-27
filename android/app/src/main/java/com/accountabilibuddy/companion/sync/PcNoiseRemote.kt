package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.NoiseRemote
import com.accountabilibuddy.shared.sync.SyncAction
import com.accountabilibuddy.shared.sync.SyncActionResult
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.time.Duration
import java.time.Instant
import java.util.UUID

/**
 * The watch's Noise page controlling a PC's noise player through the sync files (ADR 42, 36): the PCs whose files carry
 * a noise player, as the watch lists them, and the watch's commands for one of them as "noise" actions in this phone's
 * file ([ActionStore]). The PC answers in its own file, which also carries its new state, so a command shows a few
 * seconds later (longer when the PC is idle: it polls Drive slowly then). A PC that doesn't answer within 10 minutes is
 * given up on, like a start or end-early request ([PcFileRemote.GIVE_UP_AFTER]).
 */
class PcNoiseRemote(
    private val actions: ActionStore,
    private val received: StateFlow<List<ReceivedDevice>>,
    private val scope: CoroutineScope,
) {
    // The last refusal or answer to show per PC, for a few seconds.
    private val notes = MutableStateFlow<Map<UUID, String>>(emptyMap())
    private val volumeJobs = mutableMapOf<UUID, Job>()

    /** The PCs with a noise player, in name order (so the watch's switcher cycles the same way each time). */
    val players: StateFlow<List<NoiseRemote.PcPlayer>> = combine(received, actions.pending, notes) { devices, pending, notes ->
        devices
            .filter { it.file.device.kind == SyncDeviceCard.KIND_PC && it.file.noise != null }
            .sortedWith(compareBy({ it.file.device.name.lowercase() }, { it.file.device.deviceId }))
            .map { pc ->
                val id = pc.file.device.deviceId
                val noise = pc.file.noise!!
                NoiseRemote.PcPlayer(
                    id.toString(), pc.file.device.name, noise.playing, noise.name, Math.round(noise.volume * 100).toInt().coerceIn(0, 100), noise.locked,
                    sending = pending.any { it.kind == SyncAction.NOISE && it.target == id }, note = notes[id],
                )
            }
    }.stateIn(scope, SharingStarted.Eagerly, emptyList())

    /** Passes a watch command for the PC [pcId] (its sync device id) on to it. */
    fun send(pcId: String, command: NoiseRemote.Command) {
        val id = runCatching { UUID.fromString(pcId) }.getOrNull() ?: return
        val pc = received.value.firstOrNull { it.file.device.deviceId == id && it.file.noise != null } ?: return
        val name = pc.file.device.name
        if (!PcFileControl.supportsNoise(pc.file.device)) {
            say(id, updateText(name))
            return
        }

        notes.update { it - id }
        when (command.action) {
            NoiseRemote.TOGGLE -> add(id, request("toggle"))
            NoiseRemote.NEXT -> add(id, request("next"))
            NoiseRemote.PREVIOUS -> add(id, request("prev"))
            NoiseRemote.VOLUME -> command.volume?.let { sendVolume(id, it) }
        }
    }

    /** A PC acknowledged one of this phone's actions ([ActionStore.onAnswered]); a refusal is shown on the watch for a moment. */
    fun answered(action: SyncAction, result: SyncActionResult?, pc: ReceivedDevice) {
        if (action.kind != SyncAction.NOISE) {
            return
        }

        val name = pc.file.device.name
        when {
            result == null || result.status == SyncActionResult.STATUS_UNSUPPORTED -> say(action.target, updateText(name))
            !result.ok -> say(action.target, result.message ?: "$name didn't do that")
        }
    }

    /** Gives up on noise commands no PC answered in 10 minutes (the PC may be off), so one is never applied much later. */
    fun expire(now: Instant = Instant.now()) {
        val gone = actions.pending.value.filter { it.kind == SyncAction.NOISE && Duration.between(it.at, now) > PcFileRemote.GIVE_UP_AFTER }
        gone.forEach { actions.remove(it.id) }
        gone.map { it.target }.distinct().forEach { id ->
            say(id, "${nameOf(id)} didn't answer. Is it on and syncing?")
        }
    }

    // The crown sends many volumes in a row: only the last one goes, after a short pause, and replaces one still waiting.
    private fun sendVolume(pcId: UUID, percent: Int) {
        volumeJobs[pcId]?.cancel()
        volumeJobs[pcId] = scope.launch {
            delay(VOLUME_PAUSE_MS)
            actions.pending.value.filter { it.kind == SyncAction.NOISE && it.target == pcId && isVolume(it) }.forEach { actions.remove(it.id) }
            add(pcId, buildJsonObject {
                put("op", "volume")
                put("value", percent.coerceIn(0, 100) / 100.0)
            })
        }
    }

    private fun add(pcId: UUID, request: JsonObject) {
        actions.add(SyncAction.NOISE, pcId, text = request.toString())
    }

    private fun request(op: String): JsonObject = buildJsonObject { put("op", op) }

    private fun isVolume(action: SyncAction): Boolean = runCatching {
        ((Json.parseToJsonElement(action.text.orEmpty()) as JsonObject)["op"] as? JsonPrimitive)?.content == "volume"
    }.getOrDefault(false)

    private fun say(pcId: UUID, text: String) {
        notes.update { it + (pcId to text) }
        scope.launch {
            delay(NOTE_SHOWN_MS)
            notes.update { if (it[pcId] == text) it - pcId else it }
        }
    }

    private fun updateText(name: String): String = "Update AccountabiliBuddy on $name"

    private fun nameOf(pcId: UUID): String = received.value.firstOrNull { it.file.device.deviceId == pcId }?.file?.device?.name ?: "The PC"

    private companion object {
        const val VOLUME_PAUSE_MS = 400L
        const val NOTE_SHOWN_MS = 8_000L
    }
}
