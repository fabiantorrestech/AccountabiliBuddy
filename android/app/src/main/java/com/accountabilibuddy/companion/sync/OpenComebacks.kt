package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncParked

/**
 * The open come-backs on Today (ADR 30, 48): the user's parked thoughts from the list every device shares, newest
 * merge of all of them. Ticking one or parking a new one changes that list here and reaches the other devices
 * with the next sync (it is no longer a read-only copy of one PC's list).
 */
data class OpenComebacks(val items: List<SyncParked>) {
    companion object {
        /** The not yet ticked ones of [parked] (live items only), in list order. */
        fun of(parked: List<SyncParked>): OpenComebacks = OpenComebacks(parked.filter { it.deletedAt == null && it.doneAt == null })
    }
}
