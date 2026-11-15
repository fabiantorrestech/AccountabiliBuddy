package com.accountabilibuddy.shared.session

import java.time.Duration
import java.time.Instant

/** A reading of the trusted time: [networkBacked] when it is Android's network time, else the phone's own (movable) wall clock. */
data class PhoneTimeReading(val instant: Instant, val networkBacked: Boolean)

/** What the phone session's clock reads from the device (Android in the app, a fake in the checks). */
interface PhoneClockSource {
    /** Milliseconds since this boot, deep sleep included (`SystemClock.elapsedRealtime`). */
    fun elapsedRealtimeMs(): Long

    /** The phone's boot count (`Settings.Global.BOOT_COUNT`), -1 when it can't be read. */
    fun bootCount(): Int

    /** The trusted time now, with whether it is network-backed. */
    fun trustedReading(): PhoneTimeReading

    /** Network time can still arrive on this phone (Android 13 and later). Below that it never does, and an unchecked clock is all there is. */
    fun trustCanCome(): Boolean
}

/**
 * The phone session's time (R12): an anchor on the trusted time moved on by the elapsed clock, so the phone's wall clock can't move a
 * session. Taken again at a session's start and after each reboot. Never reads `Instant.now()`: every wall reading comes through [source].
 *
 * @param saved The anchor kept with a running session's checkpoint, so a restart of the app in the same boot carries on from it.
 */
class PhoneSessionClock(private val source: PhoneClockSource, saved: PhoneSessionAnchor? = null) {
    /** The anchor in use (kept with the checkpoint). */
    var anchor: PhoneSessionAnchor = saved ?: take()
        private set

    /** The session time now. */
    fun now(): Instant = anchor.at(source.elapsedRealtimeMs())

    /** The anchor's time is network-backed (B3b); false = movable by the phone's clock, so it is never used to close a session or saved as a heartbeat. */
    val isTrusted: Boolean get() = anchor.networkBacked

    /** True when the phone has been restarted since [anchor] was taken (a new boot count, or the elapsed clock went back). */
    fun rebooted(): Boolean = source.bootCount() != anchor.bootCount || source.elapsedRealtimeMs() < anchor.elapsedMs

    /** Takes a new anchor on the trusted time now (at a session's start, and after a reboot). */
    fun reanchor() {
        anchor = take()
    }

    /** Network time can still arrive (see [PhoneClockSource.trustCanCome]). */
    val trustCanCome: Boolean get() = source.trustCanCome()

    /**
     * An anchor that was not network-backed becomes so when the network time arrives: taken again, and the CORRECTION is returned (the trusted time
     * now minus what the old anchor said now was); null when nothing changed. The caller moves the times written on the old anchor by it (B2).
     */
    fun upgradeIfTrusted(): Duration? {
        if (anchor.networkBacked || !source.trustedReading().networkBacked) return null
        val before = now()
        reanchor()
        return Duration.between(before, now())
    }

    private fun take(): PhoneSessionAnchor {
        val elapsed = source.elapsedRealtimeMs()
        val reading = source.trustedReading()
        return PhoneSessionAnchor(reading.instant, elapsed, source.bootCount(), reading.networkBacked)
    }
}
