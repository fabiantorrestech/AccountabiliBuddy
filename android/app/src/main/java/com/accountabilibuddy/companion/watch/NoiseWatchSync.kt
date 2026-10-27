package com.accountabilibuddy.companion.watch

import android.content.Context
import android.os.Handler
import android.os.Looper
import com.accountabilibuddy.companion.noise.NoisePlayer
import com.accountabilibuddy.companion.sync.PcNoiseRemote
import com.accountabilibuddy.shared.NoiseRemote
import com.accountabilibuddy.shared.WatchLinkSettings
import com.accountabilibuddy.shared.WatchNode
import com.accountabilibuddy.shared.WatchPaths
import com.accountabilibuddy.shared.WatchRoute
import com.google.android.gms.tasks.Task
import com.google.android.gms.wearable.CapabilityClient
import com.google.android.gms.wearable.Wearable
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/**
 * The noise player on the linked watch (ADR 36): what's playing goes to the watch when it changes (and when the watch
 * says hello), by the same route as the snapshot; the watch's play/pause, skip and volume come back here. Only the
 * linked watch is heard. Since ADR 42 the PCs with a noise player are listed too, and a command naming one goes to it
 * through the sync files ([PcNoiseRemote]).
 */
class NoiseWatchSync(
    private val context: Context,
    private val noise: NoisePlayer,
    private val links: StateFlow<WatchLinkSettings>,
    private val scope: CoroutineScope,
    private val pcNoise: PcNoiseRemote,
) {
    private val messages = Wearable.getMessageClient(context)
    private val main = Handler(Looper.getMainLooper())

    fun start() {
        scope.launch {
            combine(noise.state, noise.settings, links, pcNoise.players) { state, settings, _, pcs -> state(state, settings, pcs) }
                .collectLatest { state ->
                    // Volume turned on the crown sends many changes: the last one after a short pause.
                    delay(250)
                    send(state)
                }
        }
    }

    /** Sends what's playing now (after the watch said hello). */
    fun sendNow() {
        scope.launch { send(state(noise.state.value, noise.settings.value, pcNoise.players.value)) }
    }

    /** A command from [nodeId]: applied when it's the linked watch; for a PC ([NoiseRemote.Command.device]) it goes on to that PC. */
    fun apply(nodeId: String, command: NoiseRemote.Command) {
        if (nodeId != links.value.linkedNodeId) return
        val pcId = command.device
        if (pcId != null) {
            main.post { pcNoise.send(pcId, command) }
            return
        }

        // The player belongs to the main thread.
        main.post {
            when (command.action) {
                NoiseRemote.TOGGLE -> noise.toggle()
                NoiseRemote.NEXT -> noise.next()
                NoiseRemote.PREVIOUS -> noise.previous()
                NoiseRemote.VOLUME -> command.volume?.let(noise::setVolume)
            }
        }
    }

    // Locked = the lock is on and no headphones are connected (the watch shows a small lock).
    private fun state(
        state: com.accountabilibuddy.companion.noise.NoiseState,
        settings: com.accountabilibuddy.companion.noise.NoiseSettings,
        pcs: List<NoiseRemote.PcPlayer>,
    ) = NoiseRemote.State(state.playing, state.title, settings.volume, state.hasFolder, settings.headphoneLock && !state.headphonesConnected, pcs)

    private suspend fun send(state: NoiseRemote.State) {
        val settings = links.value
        if (settings.linkedNodeId == null) return
        val targets = WatchRoute.choose(settings, reachableWatches())
        val bytes = NoiseRemote.write(state)
        targets.forEach { watch -> runCatching { messages.sendMessage(watch.id, WatchPaths.NOISE, bytes).await() } }
    }

    private suspend fun reachableWatches(): List<WatchNode> = runCatching {
        Wearable.getCapabilityClient(context).getCapability(WatchPaths.WATCH_CAPABILITY, CapabilityClient.FILTER_REACHABLE).await().nodes
            .map { WatchNode(it.id, it.displayName, it.isNearby) }
    }.getOrDefault(emptyList())

    // Google Play services tasks, awaited without the coroutines-play-services library (not a dependency).
    private suspend fun <T> Task<T>.await(): T = suspendCancellableCoroutine { continuation ->
        addOnSuccessListener { continuation.resume(it) }
        addOnFailureListener { continuation.resumeWithException(it) }
        addOnCanceledListener { continuation.cancel() }
    }
}
