package com.accountabilibuddy.watch.link

import android.content.Context
import com.accountabilibuddy.shared.LunchRemote
import com.accountabilibuddy.shared.WatchPaths
import com.google.android.gms.tasks.Task
import com.google.android.gms.wearable.Wearable
import kotlinx.coroutines.suspendCancellableCoroutine
import java.time.Instant
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/**
 * The Lunch page's buttons (start, back, +5, −5, Count it, Discard it, Start the rest) go to the phone ("/lunch"), which owns the lunch. Once the phone has
 * it, the page shows the change at once; the phone's next snapshot then confirms it. With the relay off (the phone's
 * switch) only to a phone that's nearby.
 */
object LunchLink {
    /** Sends [action]; false when no phone could be reached (nothing changes then). */
    suspend fun send(context: Context, action: String): Boolean {
        val stored = SnapshotStore.snapshot(context).value
        val relayAllowed = stored?.relayAllowed ?: true
        val delivered = runCatching {
            val phones = PhoneNodes.reachable(context, relayAllowed)
            val messages = Wearable.getMessageClient(context)
            phones.count { phone -> runCatching { messages.sendMessage(phone.id, WatchPaths.LUNCH, LunchRemote.write(action)).await() }.isSuccess } > 0
        }.getOrDefault(false)
        if (!delivered) {
            PhoneNodes.invalidate()
        }

        val lunch = stored?.lunch
        if (delivered && lunch != null) {
            SnapshotStore.save(context, stored.copy(lunch = lunch.expected(action, Instant.now())))
        }
        return delivered
    }

    // Google Play services tasks, awaited without the coroutines-play-services library (not a dependency).
    private suspend fun <T> Task<T>.await(): T = suspendCancellableCoroutine { continuation ->
        addOnSuccessListener { continuation.resume(it) }
        addOnFailureListener { continuation.resumeWithException(it) }
        addOnCanceledListener { continuation.cancel() }
    }
}
