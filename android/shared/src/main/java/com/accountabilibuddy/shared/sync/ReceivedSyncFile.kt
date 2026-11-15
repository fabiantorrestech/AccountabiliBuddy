package com.accountabilibuddy.shared.sync

import java.time.Instant

/**
 * A sync file received from another device with the receiver's own trusted time of its arrival (ADR 85, PS1a review B2): the twin of
 * Core's `ReceivedSyncFile`. The app maps its `ReceivedDevice` (file + `receivedAt`) to it.
 *
 * @property file The newest file (merged copy) of that device.
 * @property receivedAt When it arrived, on the receiver's trusted clock; never the sender's stamp.
 */
data class ReceivedSyncFile(val file: SyncFile, val receivedAt: Instant)
