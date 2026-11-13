package com.accountabilibuddy.shared.sync

import java.time.Duration
import java.time.Instant
import kotlin.coroutines.cancellation.CancellationException

/**
 * "At most once per [every]" for the relay's forced passes (SYNC1c; the PC's `RelayRereadEvery` rule): the relay's quota is shared by every
 * device on one address, so Re-read goal history, Send my files again (its history part) and Try again on a relay file each have one of these.
 * [isOpen] says whether a pass may start now; [mark] records that one did. Pure: the caller brings the time.
 */
class SyncPassGate(private val every: Duration) {
    private var last: Instant = Instant.MIN

    /** True when no pass started within the last [every]. */
    @Synchronized
    fun isOpen(now: Instant): Boolean = last == Instant.MIN || Duration.between(last, now) >= every

    /** A pass started at [now]. */
    @Synchronized
    fun mark(now: Instant) {
        last = now
    }
}

/** What the troubleshooting actions say when they can't do their job now. Words only, never a detail (a name, a path or a message could be private). */
object SyncTroubleshootRules {
    const val FAILED_TEXT = "Couldn't finish \u00b7 try again"
    const val WAIT_TEXT = "Wait a few minutes"

    /**
     * Runs one action to its one result line. Whatever it throws (a closed store, a locked database, a bad answer, a missing activity) becomes
     * [FAILED_TEXT]: the button that asked never crashes the app. A cancelled coroutine is let through.
     */
    suspend fun safely(action: suspend () -> String): String = try {
        action()
    } catch (e: CancellationException) {
        throw e
    } catch (e: Exception) {
        FAILED_TEXT
    }
}
