package com.accountabilibuddy.shared.sync

import java.time.Instant

/** An open come-back from the PC (ADR 30): a thought parked there, shown read-only on the phone until ticked on the PC. */
data class SyncComeback(val id: String, val text: String, val createdAt: Instant)
