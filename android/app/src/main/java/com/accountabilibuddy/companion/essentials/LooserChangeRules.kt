package com.accountabilibuddy.companion.essentials

import java.time.Duration
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * The hard rule for the phone's lock settings (ADR 73): stricter changes apply at once; looser ones wait [WAIT] on
 * trusted time, never during a session, no "apply now". Pure (no Android API) so it can be checked on its own.
 */
object LooserChangeRules {
    /** How long a looser change waits (the same 10 minutes as the built-in screen choice). */
    val WAIT: Duration = Duration.ofMinutes(10)

    /** What asking for a change does right now. */
    enum class Decision { Applied, Waits, Refused }

    /** What a pending change does when it is looked at. */
    enum class Settle { Cancel, Apply, Keep }

    fun request(loosening: Boolean, sessionRunning: Boolean): Decision = when {
        !loosening -> Decision.Applied
        sessionRunning -> Decision.Refused
        else -> Decision.Waits
    }

    fun dueAt(now: Instant): Instant = now.plus(WAIT)

    /** A session starting cancels a pending change; otherwise it applies once [now] has reached [dueAtMs]. */
    fun settle(dueAtMs: Long, sessionRunning: Boolean, now: Instant): Settle = when {
        sessionRunning -> Settle.Cancel
        now.toEpochMilli() >= dueAtMs -> Settle.Apply
        else -> Settle.Keep
    }

    /**
     * True when moving the LockMyPhone-Extended profile from [current] to [next] may loosen the lock. Only the profile this
     * app keeps in step with the essentials ([strictest]) is known to be the strict one, so any other pick waits.
     */
    fun profileLoosens(current: String, next: String, strictest: String): Boolean = next != current && next != strictest

    private val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

    /** A time for the user: 12-hour with AM/PM (e.g. 4:05 PM), never 24-hour. */
    fun clockText(at: Instant, zone: ZoneId = ZoneId.systemDefault()): String = CLOCK.format(at.atZone(zone))

    /** The line under a setting while a looser change waits. */
    fun waitingText(what: String, at: Instant, zone: ZoneId = ZoneId.systemDefault()): String =
        "$what at ${clockText(at, zone)} (a looser change waits ${WAIT.toMinutes()} minutes)."

    /** The line shown when a looser change is refused during a session. */
    const val REFUSED_TEXT = "Not during a session: it's a looser change."
}
