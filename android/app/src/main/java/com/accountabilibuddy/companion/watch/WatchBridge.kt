package com.accountabilibuddy.companion.watch

import android.content.Context
import android.os.SystemClock
import com.accountabilibuddy.companion.data.CompanionStore
import com.accountabilibuddy.companion.link.LinkClient
import com.accountabilibuddy.companion.link.LinkStatus
import com.accountabilibuddy.shared.SnapshotBuilder
import com.accountabilibuddy.shared.WatchLinkSettings
import com.accountabilibuddy.shared.WatchNode
import com.accountabilibuddy.shared.WatchPaths
import com.accountabilibuddy.shared.WatchRoute
import com.accountabilibuddy.shared.WatchSnapshot
import com.accountabilibuddy.shared.WatchSnapshotJson
import com.google.android.gms.wearable.CapabilityClient
import com.google.android.gms.wearable.Wearable
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeoutOrNull
import java.time.Instant
import java.time.ZoneId

/**
 * The phone's side of the watch link (ADR 21): finds watches that have the app, keeps the user's link settings, and
 * sends the linked watch a snapshot over the Wearable Data Layer when what it shows changes (at most every 10 s, at
 * once when a session starts or ends) and when the watch asks. Messages only, never stored Data Layer items, and only
 * by the routes [WatchRoute] allows: nothing before a watch is linked; with the relay off, only while it's nearby.
 */
class WatchBridge(
    context: Context,
    private val store: CompanionStore,
    private val link: LinkClient,
    private val links: WatchLinkStore,
    private val scope: CoroutineScope,
    /** True while the phone app is on screen (its link to the PC is then the screen's, not the watch's). */
    private val appOnScreen: () -> Boolean,
) {
    private val messages = Wearable.getMessageClient(context)
    private val capabilities = Wearable.getCapabilityClient(context)
    private val sending = Mutex()
    private val _detected = MutableStateFlow<List<WatchNode>>(emptyList())

    // The last snapshot sent (to skip sending the same again) and when, on the uptime clock.
    private var lastSent: WatchSnapshot? = null
    private var lastSentAt = 0L

    val settings: StateFlow<WatchLinkSettings> = links.settings

    /** Watches paired with this phone that have the watch app, found by [findWatches]. */
    val detected: StateFlow<List<WatchNode>> = _detected.asStateFlow()

    /** Sends when what the watch shows changes, while the app runs. */
    fun start() {
        scope.launch {
            combine(store.state, link.status, links.settings) { _, _, _ -> Unit }.collectLatest {
                val snapshot = build() ?: return@collectLatest
                val previous = lastSent
                if (previous != null && sameContent(previous, snapshot)) {
                    return@collectLatest
                }

                // A session starting or ending goes at once; anything else at most every 10 seconds (a newer change
                // while waiting replaces this one).
                val startedOrEnded = previous == null || (previous.running == null) != (snapshot.running == null)
                if (!startedOrEnded) {
                    delay((lastSentAt + MIN_GAP_MS - SystemClock.elapsedRealtime()).coerceAtLeast(0))
                }

                send(build() ?: return@collectLatest)
            }
        }
    }

    fun findWatches() {
        scope.launch {
            _detected.value = runCatching {
                capabilities.getCapability(WatchPaths.WATCH_CAPABILITY, CapabilityClient.FILTER_ALL).await().nodes
                    .map { WatchNode(it.id, it.displayName, it.isNearby) }
                    .sortedBy { it.name }
            }.getOrDefault(emptyList())
        }
    }

    fun linkWatch(watch: WatchNode) {
        links.update { it.copy(linkedNodeId = watch.id, linkedName = watch.name) }
    }

    /** Stops all sends; the watch is told (when a route allows) so it forgets what it has. */
    fun unlinkWatch() {
        val before = links.settings.value
        links.update { it.copy(linkedNodeId = null, linkedName = null) }
        scope.launch {
            sending.withLock {
                lastSent = null
                WatchRoute.choose(before, reachableWatches()).forEach { watch ->
                    runCatching { messages.sendMessage(watch.id, WatchPaths.UNLINK, ByteArray(0)).await() }
                }
            }
        }
    }

    fun setDataLayer(on: Boolean) = links.update { it.copy(dataLayerOn = on) }

    fun setRelay(on: Boolean) = links.update { it.copy(relayOn = on) }

    fun setWifiFallback(on: Boolean) = links.update { it.copy(wifiFallbackOn = on) }

    /**
     * The watch app opened and asked for news. When the PC is paired but the phone isn't connected (its app isn't on
     * screen), the phone connects for a moment to get the PC's current session and focus day, then answers. A watch
     * that isn't the linked one is told it isn't linked.
     */
    suspend fun answerHello(nodeId: String) {
        val settings = links.settings.value
        if (nodeId != settings.linkedNodeId) {
            if (settings.dataLayerOn) {
                runCatching { messages.sendMessage(nodeId, WatchPaths.UNLINK, ByteArray(0)).await() }
            }
            return
        }

        val connectedHere = link.pairedPc.value != null && link.status.value !is LinkStatus.Connected
        if (connectedHere) {
            val asked = Instant.now()
            link.connect()
            withTimeoutOrNull(PC_WAIT_MS) {
                link.status.first { it is LinkStatus.Connected && it.state != null }
                // The focus day follows the state and settings.
                withTimeoutOrNull(FOCUS_DAY_WAIT_MS) { store.state.first { state -> state.focusDayAt?.isAfter(asked) == true } }
            }
        }

        build()?.let { send(it) }
        if (connectedHere && !appOnScreen()) {
            link.disconnect()
        }
    }

    /** The snapshot for the linked watch, or null when none is linked. */
    private fun build(): WatchSnapshot? {
        if (links.settings.value.linkedNodeId == null) {
            return null
        }

        val pcName = link.pairedPc.value?.pcName
        return SnapshotBuilder.build(PhoneSnapshot.input(store.state.value, link.status.value, linked = true, pcName, Instant.now(), ZoneId.systemDefault()))
    }

    private suspend fun send(snapshot: WatchSnapshot) = sending.withLock {
        val targets = WatchRoute.choose(links.settings.value, reachableWatches())
        if (targets.isEmpty()) {
            // Not sent (away with the relay off, or Data Layer off): tried again on the next change.
            return@withLock
        }

        val bytes = WatchSnapshotJson.write(snapshot).toString().toByteArray()
        val delivered = targets.any { watch -> runCatching { messages.sendMessage(watch.id, WatchPaths.SNAPSHOT, bytes).await() }.isSuccess }
        if (delivered) {
            lastSent = snapshot
            lastSentAt = SystemClock.elapsedRealtime()
        }
    }

    /** Watches with the app the phone can reach now (in Bluetooth range, or through the relay). */
    private suspend fun reachableWatches(): List<WatchNode> = runCatching {
        capabilities.getCapability(WatchPaths.WATCH_CAPABILITY, CapabilityClient.FILTER_REACHABLE).await().nodes
            .map { WatchNode(it.id, it.displayName, it.isNearby) }
    }.getOrDefault(emptyList())

    /** The same apart from when it was made. */
    private fun sameContent(a: WatchSnapshot, b: WatchSnapshot): Boolean = a.copy(createdAt = b.createdAt, week = b.week) == b && a.week.dropLast(1) == b.week.dropLast(1)

    private companion object {
        const val MIN_GAP_MS = 10_000L
        const val PC_WAIT_MS = 8_000L
        const val FOCUS_DAY_WAIT_MS = 3_000L
    }
}
