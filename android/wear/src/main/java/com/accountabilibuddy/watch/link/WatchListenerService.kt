package com.accountabilibuddy.watch.link

import com.accountabilibuddy.shared.WatchPaths
import com.accountabilibuddy.shared.WatchSnapshotJson
import com.google.android.gms.wearable.MessageEvent
import com.google.android.gms.wearable.WearableListenerService
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject

/**
 * Takes the phone's messages (ADR 21), also when the watch app isn't open: a snapshot is kept for the screens, the
 * Tile and the complication; "not linked" clears what the watch had. Only the phone app with the same id and
 * signature can send these.
 */
class WatchListenerService : WearableListenerService() {
    override fun onMessageReceived(event: MessageEvent) {
        when (event.path) {
            WatchPaths.SNAPSHOT -> {
                val json = runCatching { Json.parseToJsonElement(event.data.decodeToString()) as? JsonObject }.getOrNull() ?: return
                val snapshot = WatchSnapshotJson.read(json) ?: return
                if (snapshot.linked) {
                    SnapshotStore.save(this, snapshot)
                } else {
                    SnapshotStore.clear(this)
                }
            }

            WatchPaths.UNLINK -> SnapshotStore.clear(this)
        }
    }
}
