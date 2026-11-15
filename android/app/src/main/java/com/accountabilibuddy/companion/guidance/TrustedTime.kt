package com.accountabilibuddy.companion.guidance

import android.os.Build
import android.os.SystemClock
import java.time.Instant

/**
 * The time ProductaBuddy's waits count with (ADR 70): Android's own network time, which moving the phone's clock doesn't change
 * (Android 13 and later, once the system has had network time), else the phone's clock. Opens no connection of its own.
 */
object TrustedTime {
    /** A time and whether it is Android's network time (not movable by the phone's clock), PS2a (B3b of the PS plan check). */
    data class Reading(val instant: Instant, val networkBacked: Boolean)

    fun now(): Instant = reading().instant

    /** [now] with the signal that tells the two answers apart: network time, or the phone's own clock (Android below 13, or no network time yet). */
    fun reading(): Reading {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            runCatching { return Reading(SystemClock.currentNetworkTimeClock().instant(), true) }
        }
        return Reading(Instant.now(), false)
    }

    /** The time is Android's network time right now. */
    fun isNetworkBacked(): Boolean = reading().networkBacked
}
