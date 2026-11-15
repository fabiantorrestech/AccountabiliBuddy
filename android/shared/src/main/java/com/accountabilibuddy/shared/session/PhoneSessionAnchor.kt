package com.accountabilibuddy.shared.session

import java.time.Instant

/**
 * R12 of the plan: the phone session's own clock is an anchor taken at the session's start and at each boot. Within one boot the time is
 * `wall + (elapsedNow - elapsed)`, so moving the phone's wall clock changes nothing; after a reboot the elapsed clock starts again, so
 * a new anchor is taken on the trusted time.
 *
 * @property wall The trusted wall time when the anchor was taken.
 * @property elapsedMs The phone's elapsed-realtime clock (milliseconds since this boot, deep sleep included) at that moment.
 * @property bootCount The phone's boot count at that moment (-1 when the phone does not say).
 * @property networkBacked [wall] came from Android's network time (B3b of the plan check). An anchor that is not network-backed never
 * closes a session for a gap with the phone off, and its times are never saved as a heartbeat or as `offAt`.
 */
data class PhoneSessionAnchor(val wall: Instant, val elapsedMs: Long, val bootCount: Int, val networkBacked: Boolean) {
    /** The time at [elapsedNowMs] on the elapsed clock of the same boot. */
    fun at(elapsedNowMs: Long): Instant = wall.plusMillis(elapsedNowMs - elapsedMs)
}
