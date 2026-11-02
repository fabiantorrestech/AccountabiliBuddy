package com.accountabilibuddy.companion.placement

import com.accountabilibuddy.companion.CompanionApp
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

    /** The end time as "2:20 PM", or null when the session has no set end. */
    fun untilText(zone: ZoneId = ZoneId.systemDefault()): String? = until?.let { CLOCK.format(it.atZone(zone)) }

    companion object {
        private val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

        /** The lock at [now]: set while the session the phone tracks runs ([PlacementSession.current], the check the removal lock uses). */
        fun at(app: CompanionApp, now: Instant): GuardsLock? = PlacementSession.current(app, now)?.let { GuardsLock(it.endsAt, test = it.test) }
    }
}
