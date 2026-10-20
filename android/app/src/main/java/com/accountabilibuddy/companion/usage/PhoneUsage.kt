package com.accountabilibuddy.companion.usage

import java.time.Duration

/** Today's phone use: the total, the part during planned focus, and each app (most time first). */
data class PhoneUsage(val total: Duration, val inFocus: Duration, val apps: List<AppUsage>) {
    companion object {
        val Empty = PhoneUsage(Duration.ZERO, Duration.ZERO, emptyList())
    }
}
