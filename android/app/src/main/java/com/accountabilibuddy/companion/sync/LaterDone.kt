package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncLaterItem
import java.time.Instant

/**
 * A Later item the user ticked off on this phone (ADR 50). The Later list has no "done" state to share, so ticking takes the
 * item off the list as a deleted one (the other devices see a tombstone) and the phone keeps the item here, for "Done today"
 * and for un-ticking it. This record is never sent to another device; it is Keystore-sealed with the lists and dropped after two days.
 */
data class LaterDone(val item: SyncLaterItem, val doneAt: Instant)
