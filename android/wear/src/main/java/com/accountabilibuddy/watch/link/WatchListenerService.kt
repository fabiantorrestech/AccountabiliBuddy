package com.accountabilibuddy.watch.link

import com.accountabilibuddy.shared.WatchPaths
import com.accountabilibuddy.shared.WatchSnapshotJson
import com.google.android.gms.wearable.MessageEvent
import com.google.android.gms.wearable.WearableListenerService
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject

/**
 * Takes the phone's messages (ADR 21), also when the watch app isn't open: a snapshot is kept for the screens, the
 * Tile and the complication; "not linked" makes the watch ask whether to forget what it had (ADR 54). The watch reaches a PC only through the phone
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
                    // The phone has this watch linked: an earlier unlink no longer applies.
                    PhoneNotice.linkedSnapshot(this)
                } else {
                    unlinkedByPhone()
                }
            }

            WatchPaths.UNLINK -> unlinkedByPhone()

            // ADR 54: the phone was removed from the user's synced devices (1), or that ended (0). The snapshot stays either way.
            WatchPaths.PHONE_STATE -> PhoneNotice.phoneUnlinked(this, event.data.firstOrNull() == 1.toByte())

            // ADR 38: a bedtime reminder to ring, or one dismissed/snoozed elsewhere ("/reminder" is a prefix of both).
            WatchPaths.REMINDER -> com.accountabilibuddy.shared.ReminderMessages.readRing(event.data)?.let { com.accountabilibuddy.watch.alerts.ReminderAlert.show(this, it) }
            WatchPaths.REMINDER_ACTION -> com.accountabilibuddy.shared.ReminderMessages.readAction(event.data)?.let {
                com.accountabilibuddy.watch.alerts.ReminderAlert.stop(this, it.night, it.number)
            }

            // ADR 36: what the phone's noise player is doing.
            WatchPaths.NOISE -> com.accountabilibuddy.shared.NoiseRemote.readState(event.data)?.let(NoiseLink::received)
        }
    }

    /**
     * The phone says this watch isn't linked (any more). The watch no longer wipes at once: it shows "Forget data" / "Keep for now"
     * (W4, ADR 54) and the user picks. With no snapshot held there is nothing to ask about.
     */
    private fun unlinkedByPhone() {
        if (SnapshotStore.snapshot(this).value == null) {
            return
        }

        PhoneNotice.unlinkedByPhone(this)
    }
}
