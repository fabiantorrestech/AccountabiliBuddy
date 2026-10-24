package com.accountabilibuddy.watch.link

import com.accountabilibuddy.shared.WatchPaths
import com.accountabilibuddy.shared.WatchSnapshot
import com.accountabilibuddy.shared.WatchSnapshotJson
import com.accountabilibuddy.shared.link.PcOffer
import com.google.android.gms.tasks.Tasks
import com.google.android.gms.wearable.MessageEvent
import com.google.android.gms.wearable.Wearable
import com.google.android.gms.wearable.WearableListenerService
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

/**
 * Takes the phone's messages (ADR 21), also when the watch app isn't open: a snapshot is kept for the screens, the
 * Tile and the complication; "not linked" clears what the watch had and forgets the PC; the PC's pairing code (passed
 * on from its QR code) pairs the watch with the PC for Wi-Fi, and the phone hears how it went. Only the phone app with
 * the same id and signature can send these.
 */
class WatchListenerService : WearableListenerService() {
    override fun onMessageReceived(event: MessageEvent) {
        PhoneLink.heardFromPhone()
        when (event.path) {
            WatchPaths.SNAPSHOT -> {
                val json = runCatching { Json.parseToJsonElement(event.data.decodeToString()) as? JsonObject }.getOrNull() ?: return
                val snapshot = WatchSnapshotJson.read(json) ?: return
                if (snapshot.linked) {
                    // Newer data from the PC over Wi-Fi stays; the phone's switches still apply.
                    SnapshotStore.save(this, WatchSnapshot.newerOf(SnapshotStore.snapshot(this).value, snapshot))
                } else {
                    forgetEverything()
                }
            }

            WatchPaths.UNLINK -> forgetEverything()

            // ADR 36: what the phone's noise player is doing.
            WatchPaths.NOISE -> com.accountabilibuddy.shared.NoiseRemote.readState(event.data)?.let(NoiseLink::received)

            WatchPaths.PC_OFFER -> {
                val json = runCatching { Json.parseToJsonElement(event.data.decodeToString()) as? JsonObject }.getOrNull() ?: return
                val offer = PcOffer.read(json) ?: return
                // Called on a background thread; pairing waits for Wi-Fi and the PC.
                // Null from the timeout means it took too long; pair's own null means paired.
                val outcome = runBlocking { withTimeoutOrNull(PAIR_LIMIT_MS) { Result.success(PcLink.pair(this@WatchListenerService, offer)) } }
                val error = if (outcome == null) "It took too long." else outcome.getOrNull()
                val answer = buildJsonObject {
                    put("ok", error == null)
                    error?.let { put("message", it) }
                }
                // With the relay off, the answer goes only while the phone is nearby.
                val relayAllowed = SnapshotStore.snapshot(this).value?.relayAllowed ?: true
                val reachable = relayAllowed || runCatching {
                    Tasks.await(Wearable.getNodeClient(this).connectedNodes).any { it.id == event.sourceNodeId && it.isNearby }
                }.getOrDefault(false)
                if (reachable) {
                    runCatching { Wearable.getMessageClient(this).sendMessage(event.sourceNodeId, WatchPaths.PC_PAIRED, answer.toString().toByteArray()) }
                }
            }
        }
    }

    /** Not linked (any more): nothing from the phone, and no Wi-Fi to the PC either. */
    private fun forgetEverything() {
        SnapshotStore.clear(this)
        PcLink.forget(this)
    }

    private companion object {
        const val PAIR_LIMIT_MS = 45_000L
    }
}
