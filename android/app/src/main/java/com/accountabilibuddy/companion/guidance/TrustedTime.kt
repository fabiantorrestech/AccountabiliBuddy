package com.accountabilibuddy.companion.guidance

import android.os.Build
import android.os.SystemClock
import java.time.Instant

/**
 * The time ProductaBuddy's waits count with (ADR 70): Android's own network time, which moving the phone's clock doesn't change
 * (Android 13 and later, once the system has had network time), else the phone's clock. Opens no connection of its own.
 */
object TrustedTime {
    fun now(): Instant {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            runCatching { return SystemClock.currentNetworkTimeClock().instant() }
        }
        return Instant.now()
    }
}
