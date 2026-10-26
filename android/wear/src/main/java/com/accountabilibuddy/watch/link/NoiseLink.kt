package com.accountabilibuddy.watch.link

import android.content.Context
import com.accountabilibuddy.shared.NoiseRemote
import com.accountabilibuddy.shared.WatchPaths
import com.google.android.gms.tasks.Task
import com.google.android.gms.wearable.CapabilityClient
import com.google.android.gms.wearable.Wearable
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/**
 * The phone's noise player on the watch (ADR 36): the last state the phone sent ("/noise", in memory), and commands
 * back ("/noise-command"). A command shows at once here; the phone's answer then confirms it. With the relay off
 * (the phone's switch) commands go only to a phone that's nearby.
 */
object NoiseLink {
    private val _state = MutableStateFlow<NoiseRemote.State?>(null)

    /** What's playing on the phone; null until the phone said. */
    val state: StateFlow<NoiseRemote.State?> = _state.asStateFlow()

    fun received(state: NoiseRemote.State) {
        _state.value = state
    }

    suspend fun send(context: Context, command: NoiseRemote.Command) {
        _state.value = _state.value?.let { current ->
            when (command.action) {
                // Locked without headphones the phone won't play: don't show it playing.
                NoiseRemote.TOGGLE -> if (current.locked && !current.playing) current else current.copy(playing = !current.playing)
                NoiseRemote.VOLUME -> current.copy(volume = command.volume ?: current.volume)
                else -> current
            }
        }
        val relayAllowed = SnapshotStore.snapshot(context).value?.relayAllowed ?: true
        runCatching {
            val phones = Wearable.getCapabilityClient(context)
                .getCapability(WatchPaths.PHONE_CAPABILITY, CapabilityClient.FILTER_REACHABLE).await().nodes
                .filter { relayAllowed || it.isNearby }
            val messages = Wearable.getMessageClient(context)
            phones.forEach { phone -> runCatching { messages.sendMessage(phone.id, WatchPaths.NOISE_COMMAND, NoiseRemote.write(command)).await() } }
        }
    }

    // Google Play services tasks, awaited without the coroutines-play-services library (not a dependency).
    private suspend fun <T> Task<T>.await(): T = suspendCancellableCoroutine { continuation ->
        addOnSuccessListener { continuation.resume(it) }
        addOnFailureListener { continuation.resumeWithException(it) }
        addOnCanceledListener { continuation.cancel() }
    }
}
