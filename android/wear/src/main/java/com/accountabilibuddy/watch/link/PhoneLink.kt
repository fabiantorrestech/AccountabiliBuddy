package com.accountabilibuddy.watch.link

import android.content.Context
import android.os.SystemClock
import com.accountabilibuddy.shared.WatchPaths
import com.google.android.gms.tasks.Task
import com.google.android.gms.wearable.Wearable
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/** The watch's side of the phone link (ADR 21): asking the phone app for a fresh snapshot, and whether it was reached. */
object PhoneLink {
    private val _phoneReachable = MutableStateFlow<Boolean?>(null)

    // When the watch last asked, on the uptime clock; 0 = not yet.
    @Volatile private var lastAsked = 0L

    // Asking wakes the phone (and through it the PC), so not more often than this unless the user asks.
    private const val ASK_GAP_MS = 2 * 60_000L

    /** Whether the last ask reached the phone app; null before the first. */
    val phoneReachable: StateFlow<Boolean?> = _phoneReachable.asStateFlow()

    /**
     * Sends "/hello"; the phone answers with a snapshot, or says this watch isn't linked. With the relay off (the phone's
     * switch, from the last snapshot) only to a phone that's nearby. Skipped when asked under 2 minutes ago, unless
     * [force]d; false then.
     */
    suspend fun askForNews(context: Context, relayAllowed: Boolean, force: Boolean = false): Boolean {
        val now = SystemClock.elapsedRealtime()
        if (!force && lastAsked != 0L && now - lastAsked < ASK_GAP_MS) {
            return false
        }

        lastAsked = now
        val reached = runCatching {
            val phones = PhoneNodes.reachable(context, relayAllowed)
            val messages = Wearable.getMessageClient(context)
            phones.count { phone -> runCatching { messages.sendMessage(phone.id, WatchPaths.HELLO, ByteArray(0)).await() }.isSuccess } > 0
        }.getOrDefault(false)
        if (!reached) {
            PhoneNodes.invalidate()
        }
        _phoneReachable.value = reached
        return true
    }

    // Google Play services tasks, awaited without the coroutines-play-services library (not a dependency).
    private suspend fun <T> Task<T>.await(): T = suspendCancellableCoroutine { continuation ->
        addOnSuccessListener { continuation.resume(it) }
        addOnFailureListener { continuation.resumeWithException(it) }
        addOnCanceledListener { continuation.cancel() }
    }
}
