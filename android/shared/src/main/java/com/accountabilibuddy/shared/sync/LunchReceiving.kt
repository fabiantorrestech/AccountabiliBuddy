package com.accountabilibuddy.shared.sync

import java.time.Instant
import java.util.UUID

/**
 * Taking another device's lunch from its file (the phone's twin of the PC's `LunchService.Receive`): pure, so the rule is checked without Android.
 * The files are read again and again, so a record identical to the one last received from the same sender is ignored. Without that, a stamp held
 * to "now" by [LunchListening.clampFuture] would be clamped again to the newer "now" on every read and beat an edit made here in between.
 * Memory only: after a restart each sender's lunch is read once more.
 */
class LunchReceiving {
    private val lastReceived = HashMap<UUID, SyncLunch>()

    /**
     * The lunch to take from [sender]'s file, or null when nothing changes here. [ours] is today's lunch here; [isToday] says whether the received
     * lunch's start is today. A received lunch is held to [LunchListening.MAX_LENGTH] and its stamp to [now].
     */
    @Synchronized
    fun take(sender: UUID, received: SyncLunch, ours: SyncLunch?, now: Instant, isToday: (Instant) -> Boolean): SyncLunch? {
        if (lastReceived[sender] == received) return null
        lastReceived[sender] = received
        if (!isToday(received.start)) return null
        // The stamp held to now, the day's total to the longest lunch, an older app's edit keeping our pieces, the newer edit winning (ADR 88).
        return LunchDay.receive(ours, received, now)
    }
}
