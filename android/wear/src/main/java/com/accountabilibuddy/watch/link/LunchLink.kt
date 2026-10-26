package com.accountabilibuddy.watch.link

import android.content.Context
import com.accountabilibuddy.shared.LunchRemote
import com.accountabilibuddy.shared.WatchLunch
import com.accountabilibuddy.shared.WatchPaths
import com.google.android.gms.tasks.Task
import com.google.android.gms.wearable.CapabilityClient
import com.google.android.gms.wearable.Wearable
import kotlinx.coroutines.suspendCancellableCoroutine
import java.time.Duration
import java.time.Instant
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/**
 * The Lunch page's buttons (start, back, +5, −5) go to the phone ("/lunch"), which owns the lunch. Once the phone has
 * it, the page shows the change at once; the phone's next snapshot then confirms it. With the relay off (the phone's
 * switch) only to a phone that's nearby.
 */
object LunchLink {
    private val ADJUST_STEP: Duration = Duration.ofMinutes(5)

    /** Sends [action]; false when no phone could be reached (nothing changes then). */
    suspend fun send(context: Context, action: String): Boolean {
        val stored = SnapshotStore.snapshot(context).value
        val relayAllowed = stored?.relayAllowed ?: true
        val delivered = runCatching {
            val phones = Wearable.getCapabilityClient(context)
                .getCapability(WatchPaths.PHONE_CAPABILITY, CapabilityClient.FILTER_REACHABLE).await().nodes
                .filter { relayAllowed || it.isNearby }
            val messages = Wearable.getMessageClient(context)
            phones.count { phone -> runCatching { messages.sendMessage(phone.id, WatchPaths.LUNCH, LunchRemote.write(action)).await() }.isSuccess } > 0
        }.getOrDefault(false)

        val lunch = stored?.lunch
        if (delivered && lunch != null) {
            SnapshotStore.save(context, stored.copy(lunch = expected(lunch, action, Instant.now())))
        }
        return delivered
    }

    /** What the phone will make of [action], as its LunchStore does. */
    private fun expected(lunch: WatchLunch, action: String, now: Instant): WatchLunch {
        val end = lunch.end
        return when {
            action == LunchRemote.START && lunch.start == null -> lunch.copy(start = now, end = now.plus(Duration.ofMinutes(lunch.lengthMinutes.toLong())))
            !lunch.isRunning(now) || end == null -> lunch
            action == LunchRemote.BACK -> lunch.copy(end = now)
            action == LunchRemote.PLUS -> lunch.copy(end = end.plus(ADJUST_STEP))
            action == LunchRemote.MINUS -> {
                val soonest = now.plus(WatchLunch.SHORTEST_LEFT)
                val shorter = end.minus(ADJUST_STEP)
                lunch.copy(end = if (shorter < soonest) minOf(soonest, end) else shorter)
            }
            else -> lunch
        }
    }

    // Google Play services tasks, awaited without the coroutines-play-services library (not a dependency).
    private suspend fun <T> Task<T>.await(): T = suspendCancellableCoroutine { continuation ->
        addOnSuccessListener { continuation.resume(it) }
        addOnFailureListener { continuation.resumeWithException(it) }
        addOnCanceledListener { continuation.cancel() }
    }
}
