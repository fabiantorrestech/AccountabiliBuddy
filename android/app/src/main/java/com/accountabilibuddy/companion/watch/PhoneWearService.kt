package com.accountabilibuddy.companion.watch

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.shared.WatchPaths
import com.google.android.gms.wearable.MessageEvent
import com.google.android.gms.wearable.WearableListenerService
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull

/**
 * Wakes the phone app when the watch app asks for news ("/hello", ADR 21), even when the phone app isn't running, and
 * takes the watch's answer after it tried to pair with the PC ("/pc-paired").
 */
class PhoneWearService : WearableListenerService() {
    override fun onMessageReceived(event: MessageEvent) {
        if (event.path == WatchPaths.PC_PAIRED) {
            val json = runCatching { Json.parseToJsonElement(event.data.decodeToString()) as? JsonObject }.getOrNull() ?: return
            val ok = (json["ok"] as? JsonPrimitive)?.booleanOrNull ?: false
            (application as CompanionApp).watchBridge.notePcPaired(ok, (json["message"] as? JsonPrimitive)?.content.orEmpty())
            return
        }

        // ADR 38: a bedtime reminder dismissed or snoozed on the watch.
        if (event.path == WatchPaths.REMINDER_ACTION) {
            com.accountabilibuddy.shared.ReminderMessages.readAction(event.data)?.let { (application as CompanionApp).reminderRelay.received(it) }
            return
        }

        // ADR 36: the watch controls the noise player.
        if (event.path == WatchPaths.NOISE_COMMAND) {
            com.accountabilibuddy.shared.NoiseRemote.readCommand(event.data)?.let { (application as CompanionApp).noiseWatch.apply(event.sourceNodeId, it) }
            return
        }

        // The watch's Lunch page: start, back, +5 or −5 min.
        if (event.path == WatchPaths.LUNCH) {
            val app = application as CompanionApp
            val action = com.accountabilibuddy.shared.LunchRemote.read(event.data) ?: return
            if (event.sourceNodeId == app.watchBridge.linkSettings.value.linkedNodeId) {
                LunchFromWatch.apply(app.lunch, app.store.state.value.document.dayPlan, action)
            }
            return
        }

        if (event.path != WatchPaths.HELLO) {
            return
        }

        // The watch app opened: what's playing goes along with the snapshot.
        (application as CompanionApp).noiseWatch.sendNow()

        // Called on a background thread; the answer may wait a few seconds for the PC.
        val bridge = (application as CompanionApp).watchBridge
        runBlocking {
            withTimeoutOrNull(ANSWER_LIMIT_MS) { bridge.answerHello(event.sourceNodeId) }
        }
    }

    private companion object {
        const val ANSWER_LIMIT_MS = 15_000L
    }
}
