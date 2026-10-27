package com.accountabilibuddy.companion.watch

import com.accountabilibuddy.shared.sync.SyncCompanions
import com.accountabilibuddy.shared.sync.SyncCompanion
import android.content.Context
import android.os.SystemClock
import com.accountabilibuddy.companion.data.CompanionStore
import com.accountabilibuddy.companion.sync.PcFileControl
import com.accountabilibuddy.companion.sync.ReceivedDevice
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
import kotlinx.coroutines.flow.flow
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
    private val links: WatchLinkStore,
    private val scope: CoroutineScope,
    /** The other devices' parts on the phone (ADR 22): their focus joins the watch's day, tagged with the device. */
    private val received: StateFlow<List<ReceivedDevice>> = MutableStateFlow(emptyList()),
    /** Today's lunch for the watch's Lunch page, and what changes it (lunch started or changed, the length setting). */
    private val lunch: () -> com.accountabilibuddy.shared.WatchLunch? = { null },
    private val lunchChanges: kotlinx.coroutines.flow.Flow<Any?> = kotlinx.coroutines.flow.flowOf(Unit),
    /** The PC picked in Devices (its sync device id), the one named in the snapshot; null: the newest PC file's. */
    private val pickedPc: () -> String? = { null },
    /** Reads the other devices' files from Drive now (ADR 42): what "/hello" does before it answers. */
    private val syncNow: suspend () -> Unit = {},
) {
    private val messages = Wearable.getMessageClient(context)
    private val capabilities = Wearable.getCapabilityClient(context)
    private val sending = Mutex()
    private val _detected = MutableStateFlow<List<WatchNode>>(emptyList())

    // The last snapshot sent (to skip sending the same again) and when, on the uptime clock.
    private var lastSent: WatchSnapshot? = null
    private var lastSentAt = 0L

    // Task 18: the watches the Data Layer knows (with the app, near or not) and when the linked one was last heard.
    @Volatile private var knownWatchIds: Set<String> = emptySet()
    @Volatile private var lastHeard: Instant? = null

    /** Called after the watch is linked or unlinked here, so the PC's Devices tab learns it at once (Task 18). */
    var onLinkChanged: (() -> Unit)? = null

    /** The linked watch as a companion on this phone's sync file (Task 18): still valid while the Data Layer knows it. */
    fun companions(): List<SyncCompanion> = SyncCompanions.companionsFor(links.settings.value, knownWatchIds, lastHeard)

    /** Asks the Data Layer which watches have the app, near or not; kept as they were when it can't be asked. */
    suspend fun refreshKnownWatches() {
        knownWatchIds = runCatching {
            capabilities.getCapability(WatchPaths.WATCH_CAPABILITY, CapabilityClient.FILTER_ALL).await().nodes.map { it.id }.toSet()
        }.getOrDefault(knownWatchIds)
    }

    val settings: StateFlow<WatchLinkSettings> = links.settings

    /** Watches paired with this phone that have the watch app, found by [findWatches]. */
    val detected: StateFlow<List<WatchNode>> = _detected.asStateFlow()

    /** Sends when what the watch shows changes, while the app runs. */
    fun start() {
        scope.launch {
            // The ticker resends now and then, so the watch's "updated … ago" stays true.
            val ticker = flow {
                while (true) {
                    emit(Unit)
                    delay(RESEND_MS)
                }
            }
            combine(store.state, links.settings, ticker, received, lunchChanges) { _, _, _, _, _ -> Unit }.collectLatest {
                val snapshot = build() ?: return@collectLatest
                val previous = lastSent
                if (previous != null && sameContent(previous, snapshot)) {
                    return@collectLatest
                }

                // A session starting or ending goes at once; anything else at most every 10 seconds (a newer change
                // while waiting replaces this one).
                // Lunch started, changed or ended (on any device, or from the watch) goes at once too.
                val startedOrEnded = previous == null || (previous.running == null) != (snapshot.running == null) ||
                    previous.lunch?.start != snapshot.lunch?.start || previous.lunch?.end != snapshot.lunch?.end
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
            if (_detected.value.isNotEmpty()) {
                knownWatchIds = _detected.value.map { it.id }.toSet()
            }
        }
    }

    fun linkWatch(watch: WatchNode) {
        links.update { it.copy(linkedNodeId = watch.id, linkedName = watch.name) }
        knownWatchIds = knownWatchIds + watch.id
        onLinkChanged?.invoke()
    }

    /** Stops all sends; the watch is told (when a route allows) so it forgets what it has. */
    fun unlinkWatch() {
        val before = links.settings.value
        links.update { it.copy(linkedNodeId = null, linkedName = null) }
        lastHeard = null
        onLinkChanged?.invoke()
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

    /** The watch link's settings (for the noise player on the watch, ADR 36). */
    val linkSettings: StateFlow<WatchLinkSettings> get() = links.settings

    /**
     * The watch app opened and asked for news: the phone reads the PCs' newest files from Drive (ADR 42), then answers. A
     * watch that isn't the linked one is told it isn't linked. Nothing at all happens when no answer may go to that
     * watch (Data Layer off, or the relay off and the watch away), so Drive isn't read for nothing.
     */
    suspend fun answerHello(nodeId: String) {
        val settings = links.settings.value
        if (nodeId == settings.linkedNodeId) {
            lastHeard = Instant.now()
        }

        val watch = reachableWatches().firstOrNull { it.id == nodeId }
        if (!settings.dataLayerOn || watch == null || (!settings.relayOn && !watch.isNearby)) {
            return
        }

        if (nodeId != settings.linkedNodeId) {
            runCatching { messages.sendMessage(nodeId, WatchPaths.UNLINK, ByteArray(0)).await() }
            return
        }

        withTimeoutOrNull(PC_WAIT_MS) { runCatching { syncNow() } }
        build()?.let { send(it) }
    }

    /** The snapshot for the linked watch, or null when none is linked. */
    private fun build(): WatchSnapshot? {
        if (links.settings.value.linkedNodeId == null) {
            return null
        }

        val settings = links.settings.value
        val pcFiles = received.value.map { it.file.device.name to it.file }
        return SnapshotBuilder.build(
            PhoneSnapshot.input(
                store.state.value, linked = true, PcFileControl.target(received.value, pickedPc())?.file?.device?.name, Instant.now(),
                ZoneId.systemDefault(), relayAllowed = settings.relayOn, received = pcFiles,
            ).copy(lunch = lunch()),
        )
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
            lastHeard = Instant.now()
            lastSent = snapshot
            lastSentAt = SystemClock.elapsedRealtime()
        }
    }

    /** Watches with the app the phone can reach now (in Bluetooth range, or through the relay). */
    private suspend fun reachableWatches(): List<WatchNode> = runCatching {
        capabilities.getCapability(WatchPaths.WATCH_CAPABILITY, CapabilityClient.FILTER_REACHABLE).await().nodes
            .map { WatchNode(it.id, it.displayName, it.isNearby) }
    }.getOrDefault(emptyList())

    /** The same apart from when it was made, with data less than a few minutes apart in age. */
    private fun sameContent(a: WatchSnapshot, b: WatchSnapshot): Boolean =
        a.copy(createdAt = b.createdAt, dataAt = b.dataAt, week = b.week) == b && a.week.dropLast(1) == b.week.dropLast(1) &&
            java.time.Duration.between(a.dataAt, b.dataAt).toMillis() < RESEND_MS

    private companion object {
        const val MIN_GAP_MS = 10_000L
        const val RESEND_MS = 5 * 60_000L
        const val PC_WAIT_MS = 8_000L
    }
}
