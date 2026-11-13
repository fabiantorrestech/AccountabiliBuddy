package com.accountabilibuddy.shared.sync

import java.util.ArrayDeque

/**
 * Spreads requests so no more than [PER_MINUTE] start in any minute (SYNC1c "Re-read everything" on the relay; the PC's `RelayRequestPacer`): the
 * relay allows 120 requests a minute per address, a PC and a phone on one home network share them, and a goal history pass may take up to 40. A
 * sliding window of start times; [await] returns once the next request may start. Pure: the clock (milliseconds) and the wait are injected, so the
 * checks drive it with a fake clock. Not thread-safe (one re-read at a time).
 */
class RelayRequestPacer(private val nowMillis: () -> Long, private val sleepMillis: (Long) -> Unit) {
    private val starts = ArrayDeque<Long>()

    /** Requests started so far. */
    var started: Int = 0
        private set

    /** Waits until one more request may start, then counts it. */
    fun await() {
        while (true) {
            val now = nowMillis()
            while (starts.isNotEmpty() && now - starts.peekFirst() >= MINUTE_MS) starts.pollFirst()
            if (starts.size < PER_MINUTE) {
                starts.addLast(now)
                started++
                return
            }

            sleepMillis(MINUTE_MS - (now - starts.peekFirst()) + 50)
        }
    }

    companion object {
        /** The most requests that start in any 60 seconds. */
        const val PER_MINUTE = 30

        private const val MINUTE_MS = 60_000L
    }
}
