package com.accountabilibuddy.watch.link

import android.content.Context
import android.os.SystemClock
import com.accountabilibuddy.shared.WatchPaths
import com.google.android.gms.tasks.Task
import com.google.android.gms.wearable.CapabilityClient
import com.google.android.gms.wearable.Node
import com.google.android.gms.wearable.Wearable
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/**
 * The reachable phones, looked up through the Data Layer (a Google Play services call each time) and remembered for a
 * short while, so a burst of messages (the crown on the Noise page) does one lookup, not one per message. A failed
 * send drops the memory, so the next one looks again.
 */
object PhoneNodes {
    private const val KEEP_MS = 30_000L

    private val lock = Any()
    private var nodes: Set<Node> = emptySet()
    private var fetchedAt = 0L

    /** The reachable phones; only nearby ones when [relayAllowed] is false. Throws when the lookup fails. */
    suspend fun reachable(context: Context, relayAllowed: Boolean): List<Node> {
        val cached = synchronized(lock) { nodes.takeIf { fetchedAt != 0L && SystemClock.elapsedRealtime() - fetchedAt < KEEP_MS } }
        val all = cached ?: Wearable.getCapabilityClient(context)
            .getCapability(WatchPaths.PHONE_CAPABILITY, CapabilityClient.FILTER_REACHABLE).await().nodes
            .also { found ->
                // An empty answer isn't kept: a phone that comes into reach should be found at once.
                if (found.isNotEmpty()) {
                    synchronized(lock) {
                        nodes = found
                        fetchedAt = SystemClock.elapsedRealtime()
                    }
                }
            }
        return all.filter { relayAllowed || it.isNearby }
    }

    /** Forget the remembered phones (a send to them failed, so they may have gone). */
    fun invalidate() = synchronized(lock) { fetchedAt = 0L }

    // Google Play services tasks, awaited without the coroutines-play-services library (not a dependency).
    private suspend fun <T> Task<T>.await(): T = suspendCancellableCoroutine { continuation ->
        addOnSuccessListener { continuation.resume(it) }
        addOnFailureListener { continuation.resumeWithException(it) }
        addOnCanceledListener { continuation.cancel() }
    }
}
