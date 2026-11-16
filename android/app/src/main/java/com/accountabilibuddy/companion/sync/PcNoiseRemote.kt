package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.NoiseRemote
import com.accountabilibuddy.shared.sync.SyncAction
import com.accountabilibuddy.shared.sync.SyncActionResult
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncNoiseOps
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
 * The other devices' noise players, controlled through the sync files: the watch's Noise page for a PC (ADR 42, 36), and the
 * volume popover's "Other devices" rows for any PC or phone (ADR 92, [others], [setPlaying]). Commands are "noise" actions in
 * this phone's file ([ActionStore]). The PC answers in its own file, which also carries its new state, so a command shows a few
 * seconds later (longer when the PC is idle: it polls Drive slowly then). A PC that doesn't answer within 10 minutes is
 * given up on, like a start or end-early request ([PcFileRemote.GIVE_UP_AFTER]).
 */
class PcNoiseRemote(
    private val actions: ActionStore,
    private val received: StateFlow<List<ReceivedDevice>>,
    private val scope: CoroutineScope,
    private val selfId: () -> UUID = { UUID(0, 0) },
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

    /**
     * Every other device whose file carries a noise player (ADR 92), PCs and phones, by name. A device with a play or pause still
     * waiting shows the way it was asked to go, with [NoiseDevice.sending] set until it answers.
     */
    val others: StateFlow<List<NoiseDevice>> = combine(received, actions.pending, notes) { devices, pending, notes ->
        val self = selfId()
        devices
            .filter { it.file.device.deviceId != self && it.file.noise != null }
            .sortedWith(compareBy({ it.file.device.name.lowercase() }, { it.file.device.deviceId }))
            .map { device ->
                val id = device.file.device.deviceId
                val noise = device.file.noise!!
                val waiting = pending.filter { it.kind == SyncAction.NOISE && it.target == id }
                val asked = waiting.lastOrNull { opOf(it) == SyncNoiseOps.PLAY || opOf(it) == SyncNoiseOps.PAUSE }?.let { opOf(it) == SyncNoiseOps.PLAY }
                NoiseDevice(
                    id, device.file.device.name, device.file.device.kind == SyncDeviceCard.KIND_PC, asked ?: noise.playing, noise.name, noise.locked,
                    sending = waiting.isNotEmpty(), seenAt = device.file.device.writtenAt, note = notes[id],
                )
            }
    }.stateIn(scope, SharingStarted.Eagerly, emptyList())

    /**
     * Asks the device [id] to play or pause its noise (ADR 92), replacing a play or pause for it still waiting. A device too old for
     * these says so for a few seconds instead.
     */
    fun setPlaying(id: UUID, play: Boolean) {
        val device = received.value.firstOrNull { it.file.device.deviceId == id && it.file.noise != null } ?: return
        if (!supportsPlayPause(device.file.device)) {
            say(id, updateText(device.file.device.name))
            return
        }

        notes.update { it - id }
        actions.pending.value.filter { it.kind == SyncAction.NOISE && it.target == id && (opOf(it) == SyncNoiseOps.PLAY || opOf(it) == SyncNoiseOps.PAUSE) }
            .forEach { actions.remove(it.id) }
        add(id, request(if (play) SyncNoiseOps.PLAY else SyncNoiseOps.PAUSE))
    }

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

    private fun isVolume(action: SyncAction): Boolean = opOf(action) == SyncNoiseOps.VOLUME

    private fun opOf(action: SyncAction): String? = runCatching {
        ((Json.parseToJsonElement(action.text.orEmpty()) as JsonObject)["op"] as? JsonPrimitive)?.content
    }.getOrNull()

    private fun say(pcId: UUID, text: String) {
        notes.update { it + (pcId to text) }
        scope.launch {
            delay(NOTE_SHOWN_MS)
            notes.update { if (it[pcId] == text) it - pcId else it }
        }
    }

    private fun updateText(name: String): String = "Update AccountabiliBuddy on $name"

    private fun nameOf(pcId: UUID): String = received.value.firstOrNull { it.file.device.deviceId == pcId }?.file?.device?.name ?: "That device"

    companion object {
        private const val VOLUME_PAUSE_MS = 400L
        private const val NOTE_SHOWN_MS = 8_000L

        /** True when the device's app answers play and pause (ADR 92; [NoiseVersions]). */
        fun supportsPlayPause(card: SyncDeviceCard): Boolean = NoiseVersions.supportsPlayPause(card)
    }
}

/**
 * Another device's noise player for the volume popover's "Other devices" rows (ADR 92): [playing] is the way it was last asked to go
 * while [sending], otherwise what its file says; [seenAt] is when that file was written, so an old one can show as last seen.
 */
data class NoiseDevice(
    val id: UUID,
    val name: String,
    val isPc: Boolean,
    val playing: Boolean,
    val title: String?,
    val locked: Boolean,
    val sending: Boolean,
    val seenAt: Instant,
    val note: String?,
)
