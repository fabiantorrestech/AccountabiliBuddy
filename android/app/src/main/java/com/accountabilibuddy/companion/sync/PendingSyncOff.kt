package com.accountabilibuddy.companion.sync

import java.time.Instant

/** A held turn-off of the last way of syncing: which [method], and when its wait is over ([at]). */
data class PendingSyncOff(val method: SyncMethod, val at: Instant)
