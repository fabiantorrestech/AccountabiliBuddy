package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncRereadResult

/**
 * What the troubleshooting actions (SYNC1c, [SyncTroubleshoot]) ask of one sync method: the shared folder, Google Drive and the own relay each
 * implement it. Display and maintenance only: nothing here changes a setting or flips a method, and everything read still goes through the
 * coordinator's merge rules and their waits.
 */
interface SyncTroubleshootSource {
    /** The method's code ([com.accountabilibuddy.shared.sync.SyncReceiptParts.METHODS]). */
    val methodCode: String

    /** Whether the method is on, set up, and this phone is in a group. */
    val isLive: Boolean

    /**
     * "Re-read everything": forgets the "seen" marks and reads and merges every file again (merging is idempotent). The relay is paced
     * ([com.accountabilibuddy.shared.sync.RelayRequestPacer]) and ended by a 429; the caller keeps it to once per five minutes.
     */
    suspend fun rereadAll(): SyncRereadResult

    /** "Try again" on one file: its "seen" mark goes ([name], null: all) and the method reads now. */
    suspend fun tryAgain(name: String)

    /** "Send my files again now": this phone's file is written again (the write gate is reset). True when it reached the method. */
    suspend fun writeNow(): Boolean

    /** "Remove my stale file": only this phone's own old-group or extra copy (the [name] as the unreadable list holds it). True when it is gone. */
    suspend fun removeOwnStale(name: String): Boolean

    /** One goal history pass now, whatever is due (after a re-read or a send again marked the files). */
    suspend fun historyNow()
}
