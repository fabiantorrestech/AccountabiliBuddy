package com.accountabilibuddy.shared.session

import java.time.Instant

/**
 * An end the user confirmed during the wait (ADR 66 twin, R8): it applies at [readyAt] through an exact alarm and is dropped if the
 * session ends otherwise. The [reason] is private text: sealed on the phone, never synced, never logged ([toString] leaves it out).
 */
data class PhoneArmedEnd(val requestedAt: Instant, val readyAt: Instant, val reason: String) {
    override fun toString(): String = "PhoneArmedEnd(requestedAt=$requestedAt, readyAt=$readyAt)"
}
