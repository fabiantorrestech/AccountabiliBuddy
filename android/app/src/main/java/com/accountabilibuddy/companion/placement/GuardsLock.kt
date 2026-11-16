package com.accountabilibuddy.companion.placement

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.shared.session.PhoneLockArea
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * The guard settings rest while a focus session runs (view-only, like the PC's Settings): the phone's Guards, Essentials, emergency
 * time, placement and Curbox settings can be looked at and not changed. [until] is when the session's phase ends, or null when
 * it has no set end. The live ways out (Emergency, "Need it for work", Essentials as a screen, the Phone down options, calls) are
 * not settings and are not touched by this.
 */
data class GuardsLock(val until: Instant?, val test: Boolean = false) {
    /** "Settings rest until 2:20 PM." (12-hour, whatever the phone's clock setting), or until the session ends when it has no set end. */
    fun sentence(zone: ZoneId = ZoneId.systemDefault()): String = "Settings rest until ${untilText(zone) ?: "your session ends"}."

    /** "Plans rest until 2:20 PM." while the day plan is held (the PC's Plan page is locked the same way during a session). */
    fun planSentence(zone: ZoneId = ZoneId.systemDefault()): String = "Plans rest until ${untilText(zone) ?: "your session ends"}."

    /** The end time as "2:20 PM", or null when the session has no set end. */
    fun untilText(zone: ZoneId = ZoneId.systemDefault()): String? = until?.let { CLOCK.format(it.atZone(zone)) }

    companion object {
        private val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

        /**
         * The lock at [now] for [area]: set while a session that locks that area runs ([PlacementSession.locking], the check the removal lock uses); it does not
         * follow ADR 60's 10-minute release. A session on another device or the test session locks every area; the phone's own session locks its scope by mode
         * (AccountabiliBuddy everything, ProductaBuddy only the enforcement its profile turns on), so a ProductaBuddy phone session leaves the plain settings open.
         * The default area stands for "the Settings pages": what a lock written before phone sessions meant.
         */
        fun at(app: CompanionApp, now: Instant, area: PhoneLockArea = PhoneLockArea.OTHER_SETTINGS): GuardsLock? =
            PlacementSession.locking(app, now, area)?.let { GuardsLock(it.endsAt, test = it.test) }

        /** Every area locked at [now] (for the guard sections whose rows lock by area: LockMyPhone and Essentials, Curbox). */
        fun areasAt(app: CompanionApp, now: Instant): Set<PhoneLockArea> = PlacementSession.lockedAreas(app, now)
    }
}
