package com.accountabilibuddy.watch.link

import android.content.Context
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

/** The watch's side of the phone link (ADR 21): asking the phone app for a fresh snapshot. */
object PhoneLink {
    private val _phoneReachable = MutableStateFlow<Boolean?>(null)

    /** Whether the last ask reached the phone app; null before the first. */
    val phoneReachable: StateFlow<Boolean?> = _phoneReachable.asStateFlow()

    /** Sends "/hello"; the phone answers with a snapshot, or says this watch isn't linked. */
    suspend fun askForNews(context: Context) {
        val reached = runCatching {
            val phones = Wearable.getCapabilityClient(context)
                .getCapability(WatchPaths.PHONE_CAPABILITY, CapabilityClient.FILTER_REACHABLE).await().nodes
            val messages = Wearable.getMessageClient(context)
            phones.count { phone -> runCatching { messages.sendMessage(phone.id, WatchPaths.HELLO, ByteArray(0)).await() }.isSuccess } > 0
        }.getOrDefault(false)
        _phoneReachable.value = reached
    }

    // Google Play services tasks, awaited without the coroutines-play-services library (not a dependency).
    private suspend fun <T> Task<T>.await(): T = suspendCancellableCoroutine { continuation ->
        addOnSuccessListener { continuation.resume(it) }
        addOnFailureListener { continuation.resumeWithException(it) }
        addOnCanceledListener { continuation.cancel() }
    }
}
