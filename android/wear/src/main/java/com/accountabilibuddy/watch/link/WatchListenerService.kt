package com.accountabilibuddy.watch.link

import com.accountabilibuddy.shared.WatchPaths
import com.accountabilibuddy.shared.WatchSnapshotJson
import com.google.android.gms.wearable.MessageEvent
import com.google.android.gms.wearable.WearableListenerService
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject

/**
 * Takes the phone's messages (ADR 21), also when the watch app isn't open: a snapshot is kept for the screens, the
 * Tile and the complication; "not linked" clears what the watch had. The watch reaches a PC only through the phone
 * (ADR 45). Only the phone app with the same id and signature can send these.
 */
class WatchListenerService : WearableListenerService() {
    override fun onMessageReceived(event: MessageEvent) {
        when (event.path) {
            WatchPaths.SNAPSHOT -> {
                val json = runCatching { Json.parseToJsonElement(event.data.decodeToString()) as? JsonObject }.getOrNull() ?: return
                val snapshot = WatchSnapshotJson.read(json) ?: return
                if (snapshot.linked) {
                    SnapshotStore.save(this, snapshot, wait = true)
                } else {
                    forgetEverything()
                }
            }

            WatchPaths.UNLINK -> forgetEverything()

            // ADR 38: a bedtime reminder to ring, or one dismissed/snoozed elsewhere ("/reminder" is a prefix of both).
            WatchPaths.REMINDER -> com.accountabilibuddy.shared.ReminderMessages.readRing(event.data)?.let { com.accountabilibuddy.watch.alerts.ReminderAlert.show(this, it) }
            WatchPaths.REMINDER_ACTION -> com.accountabilibuddy.shared.ReminderMessages.readAction(event.data)?.let {
                com.accountabilibuddy.watch.alerts.ReminderAlert.stop(this, it.night, it.number)
            }

            // ADR 36: what the phone's noise player is doing.
            WatchPaths.NOISE -> com.accountabilibuddy.shared.NoiseRemote.readState(event.data)?.let(NoiseLink::received)
        }
    }

    /** Not linked (any more): nothing from the phone. */
    private fun forgetEverything() {
        SnapshotStore.clear(this)
    }
}
