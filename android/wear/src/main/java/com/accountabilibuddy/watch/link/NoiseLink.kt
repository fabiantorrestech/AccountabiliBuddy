package com.accountabilibuddy.watch.link

import android.content.Context
import android.os.SystemClock
import com.accountabilibuddy.shared.NoiseRemote
import com.accountabilibuddy.shared.WatchPaths
import com.google.android.gms.tasks.Task
import com.google.android.gms.wearable.Wearable
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import java.util.concurrent.ConcurrentHashMap
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/**
 * The phone's noise player on the watch (ADR 36): the last state the phone sent ("/noise", in memory), and commands
 * back ("/noise-command"). A command shows at once here; the phone's answer then confirms it. With the relay off
 * (the phone's switch) commands go only to a phone that's nearby.
 *
 * Since ADR 42 the phone also lists the user's PCs with a noise player ([NoiseRemote.State.devices]), and a command can
 * name one ([NoiseRemote.Command.device]): the phone passes it to that PC through the sync files, so the PC's own state
 * comes back after a few seconds. Until then a PC's play/pause and volume show what was asked ([PcOverride]), and the
 * page says it is sending. The device picked on the page is remembered here ([chosen]).
 */
object NoiseLink {
    private val _state = MutableStateFlow<NoiseRemote.State?>(null)
    private val _chosen = MutableStateFlow<String?>(null)
    private val _overrides = MutableStateFlow<Map<String, PcOverride>>(emptyMap())
    private var loaded = false
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val volumeJobs = ConcurrentHashMap<String, Job>()

    /** What's playing on the phone; null until the phone said. */
    val state: StateFlow<NoiseRemote.State?> = _state.asStateFlow()

    /** The PC picked on the Noise page (its sync device id); null: this phone. */
    val chosen: StateFlow<String?> = _chosen.asStateFlow()

    /** What was asked of each PC (by id) and not yet confirmed by the PC's own state. */
    val overrides: StateFlow<Map<String, PcOverride>> = _overrides.asStateFlow()

    /**
     * What the page shows of a PC before the PC confirms a command: [playing] and [volume] as asked (null: as the PC
     * said), made at [at] (elapsed realtime, ms).
     */
    data class PcOverride(val playing: Boolean?, val volume: Int?, val at: Long)

    /** [pc] with what was asked of it, until the phone says it is no longer sending (or a few seconds passed). */
    fun shown(pc: NoiseRemote.PcPlayer, override: PcOverride?): NoiseRemote.PcPlayer =
        if (override == null) pc else pc.copy(playing = override.playing ?: pc.playing, volume = override.volume ?: pc.volume)

    /** Loads the picked device once (a no-op afterwards). */
    fun load(context: Context) {
        if (loaded) return
        loaded = true
        _chosen.value = prefs(context).getString(KEY_CHOSEN, null)
    }

    /** Picks a PC (its id) or this phone (null) and remembers it. */
    fun choose(context: Context, deviceId: String?) {
        prefs(context).edit().putString(KEY_CHOSEN, deviceId).apply()
        _chosen.value = deviceId
    }

    fun received(state: NoiseRemote.State) {
        _state.value = state
        // A PC's own state is the truth once the phone is no longer sending to it (it answered, or the phone gave up).
        val now = SystemClock.elapsedRealtime()
        _overrides.value = _overrides.value.filter { (id, override) ->
            state.devices.any { it.id == id && (it.sending || now - override.at < ASKED_FOR_MS) }
        }
    }

    suspend fun send(context: Context, command: NoiseRemote.Command) {
        val device = command.device
        if (device != null) {
            ask(device, command)
        } else {
            _state.value = _state.value?.let { current ->
                when (command.action) {
                    // Locked without headphones the phone won't play: don't show it playing.
                    NoiseRemote.TOGGLE -> if (current.locked && !current.playing) current else current.copy(playing = !current.playing)
                    NoiseRemote.VOLUME -> current.copy(volume = command.volume ?: current.volume)
                    else -> current
                }
            }
        }
        if (command.action == NoiseRemote.VOLUME) {
            sendVolumeLater(context.applicationContext, command)
        } else {
            transmit(context, command)
        }
    }

    // Turning the crown makes a volume command per step; the screen already shows each, and only the last one (per
    // device) needs to reach the phone, so it goes once the crown has paused for a moment.
    private fun sendVolumeLater(context: Context, command: NoiseRemote.Command) {
        val key = command.device.orEmpty()
        volumeJobs.remove(key)?.cancel()
        volumeJobs[key] = scope.launch {
            delay(VOLUME_PAUSE_MS)
            transmit(context, command)
        }
    }

    private suspend fun transmit(context: Context, command: NoiseRemote.Command) {
        val relayAllowed = SnapshotStore.snapshot(context).value?.relayAllowed ?: true
        runCatching {
            val phones = PhoneNodes.reachable(context, relayAllowed)
            val messages = Wearable.getMessageClient(context)
            val sent = phones.count { phone -> runCatching { messages.sendMessage(phone.id, WatchPaths.NOISE_COMMAND, NoiseRemote.write(command)).await() }.isSuccess }
            if (sent == 0) {
                PhoneNodes.invalidate()
            }
        }
    }

    // Shows a PC's play/pause or volume as asked until the PC confirms it.
    private fun ask(deviceId: String, command: NoiseRemote.Command) {
        val pc = _state.value?.devices?.firstOrNull { it.id == deviceId } ?: return
        val before = _overrides.value[deviceId]
        val playing = before?.playing ?: pc.playing
        val now = SystemClock.elapsedRealtime()
        val asked = when (command.action) {
            // Locked without headphones the PC won't play: don't show it playing.
            NoiseRemote.TOGGLE -> if (pc.locked && !playing) null else PcOverride(!playing, before?.volume, now)
            NoiseRemote.VOLUME -> PcOverride(before?.playing, command.volume ?: before?.volume, now)
            else -> null
        } ?: return
        _overrides.value = _overrides.value + (deviceId to asked)
    }

    private fun prefs(context: Context) = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    // Google Play services tasks, awaited without the coroutines-play-services library (not a dependency).
    private suspend fun <T> Task<T>.await(): T = suspendCancellableCoroutine { continuation ->
        addOnSuccessListener { continuation.resume(it) }
        addOnFailureListener { continuation.resumeWithException(it) }
        addOnCanceledListener { continuation.cancel() }
    }

    private const val PREFS = "noise"
    private const val KEY_CHOSEN = "chosenDevice"

    // The phone says it is sending within a second or two; a command it never got stops showing after this.
    private const val ASKED_FOR_MS = 5_000L

    // How long the crown must rest before its last volume goes to the phone.
    private const val VOLUME_PAUSE_MS = 120L
}
