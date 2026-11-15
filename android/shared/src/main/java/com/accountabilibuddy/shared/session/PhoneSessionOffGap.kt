package com.accountabilibuddy.shared.session

import java.time.Duration
import java.time.Instant
import java.time.ZoneId

/**
 * R13 (the phone twin of ADR 60, pause-the-timer default only): what a gap with the phone off does to a running session, judged at the
 * next app start. Pure: the controller passes the trusted times and whether the boot is a new one.
 */
object PhoneSessionOffGap {
    /** A gap under this carries on with no notice. */
    val SHORT_GAP: Duration = Duration.ofMinutes(2)

    /** A gap this long (or longer) closes the session at the moment the phone went off. */
    val LONG_GAP: Duration = Duration.ofHours(4)

    /** Why a session closed at the gap. */
    enum class CloseWhy { SET_END_PASSED, FOUR_HOURS, NEW_DAY }

    sealed interface Decision {
        /** Nothing to do: the same boot, a gap under 2 minutes, or no gap. */
        data object Nothing : Decision

        /** An away stretch of kind DeviceOff from [from] to [to]: out of focus, the timer paused (the end moves on by the gap). */
        data class Pause(val from: Instant, val to: Instant) : Decision

        /**
         * The clock is behind the off moment and can't be trusted (B2 of the PS1b review): open a DeviceOff stretch at [from] and keep `offAt`.
         * The timer is paused, nothing counts as focus and enforcement is held until [decide] runs again with a time at or past [from].
         */
        data class Hold(val from: Instant) : Decision

        /** The session closes at [at] (the moment the phone went off, or the set end when it had passed before that). */
        data class Close(val at: Instant, val why: CloseWhy) : Decision
    }

    /**
     * @param record The running session as it was saved (its heartbeat and `offAt`).
     * @param now The time at the start: trusted (network-backed) or not, see [timeIsTrusted].
     * @param newBoot The boot count changed or `offAt` was saved. In the same boot with no `offAt` the session simply ran (an OEM kill, a
     * crash, a force-stop: the elapsed clock covers it), so no gap is made (B3a).
     * @param timeIsTrusted [now] is network-backed. An untrusted time never closes the session for the gap's length or for a new day (B3b),
     * and has no cap (Q1163b, PS1b verify): a clock set BACK gives a [Decision.Hold] (the timer paused and locked until the clock is at or past
     * the off moment); a clock at or past the off moment gives a [Decision.Pause] from the off moment to now (the end moves on by the whole gap,
     * which is stricter in both directions). The set end having passed before the phone went off needs no trust (stored times), so it still closes.
     *
     * While a Hold is open the gap is measured from the Hold's OWN start (a time that came from a trusted off moment), never from `offAt` or the
     * heartbeat, which the held boot may have written from an untrusted clock (V-B1): a held gap ends only as a Pause, never as a close for 4 h
     * or a new day (the time held was paused and locked anyway, so a close there could only loosen).
     */
    fun decide(record: PhoneSessionRecord, now: Instant, newBoot: Boolean, timeIsTrusted: Boolean, zone: ZoneId): Decision {
        if (!record.isRunning || !newBoot) return Decision.Nothing
        val held = record.holdsDeviceOff
        val from = if (held) record.openAway!!.from else record.offFrom
        val endAtOff = record.effectiveEnd(from)
        if (endAtOff != null && !endAtOff.isAfter(from)) return Decision.Close(endAtOff, CloseWhy.SET_END_PASSED)
        if (!timeIsTrusted) return if (now.isBefore(from)) Decision.Hold(from) else Decision.Pause(from, now)
        // A hold already paused the timer: it ends here as a pause whatever the gap, so the time off is never focus.
        if (held) return Decision.Pause(from, maxOf(from, now))
        val gap = Duration.between(from, now)
        if (gap < SHORT_GAP) return Decision.Nothing
        if (gap >= LONG_GAP) return Decision.Close(from, CloseWhy.FOUR_HOURS)
        if (crossesLocalDay(from, now, zone)) return Decision.Close(from, CloseWhy.NEW_DAY)
        return Decision.Pause(from, maxOf(from, now))
    }

    /** True when [from] and [to] fall on different local days (a new local day, judged after at least [SHORT_GAP]). */
    fun crossesLocalDay(from: Instant, to: Instant, zone: ZoneId): Boolean =
        from.atZone(zone).toLocalDate() != to.atZone(zone).toLocalDate()
}
