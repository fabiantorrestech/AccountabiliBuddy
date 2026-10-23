package com.accountabilibuddy.companion.sync

import java.time.Duration

/** How often the phone syncs through Drive (ADR 26): every 30 seconds while a session runs or the timeline is pinned, else 15 minutes. */
object DrivePoll {
    private val BUSY: Duration = Duration.ofSeconds(30)
    private val IDLE: Duration = Duration.ofMinutes(15)

    fun interval(busy: Boolean): Duration = if (busy) BUSY else IDLE
}
