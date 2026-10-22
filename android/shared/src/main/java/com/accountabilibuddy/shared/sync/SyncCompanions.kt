package com.accountabilibuddy.shared.sync

import com.accountabilibuddy.shared.WatchLinkSettings
import java.time.Instant

/** The phone's watch as a companion on its sync file's card (ADR 22, Task 18), with no Android in it. */
object SyncCompanions {
    /**
     * The linked watch, when one is linked: linked while the Data Layer still knows its node ([knownWatchIds], every
     * watch with the AccountabiliBuddy app, near or not), with when the phone last heard from it ([lastHeard]).
     */
    fun companionsFor(settings: WatchLinkSettings, knownWatchIds: Set<String>, lastHeard: Instant?): List<SyncCompanion> {
        val nodeId = settings.linkedNodeId ?: return emptyList()
        return listOf(SyncCompanion(settings.linkedName ?: "Pixel Watch", SyncCompanion.KIND_WATCH, nodeId in knownWatchIds, lastHeard))
    }
}
