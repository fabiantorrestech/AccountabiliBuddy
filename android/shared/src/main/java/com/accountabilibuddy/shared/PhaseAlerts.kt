package com.accountabilibuddy.shared

import java.time.Duration
import java.time.Instant

/** A buzz the watch gives for the running phase: a minute before it ends, or when it ends. */
data class PhaseAlert(val at: Instant, val kind: Kind, val onBreak: Boolean) {
    enum class Kind { Warning, End }
}

/** When the watch buzzes for the running session's phase (focus or break): at its end, and a minute before when wanted. */
object PhaseAlerts {
    private val WARNING_BEFORE: Duration = Duration.ofMinutes(1)

    /** The alerts still ahead of [now], soonest first; none for a session with no set end, or one that has ended. */
    fun due(running: RunningSession?, now: Instant, warning: Boolean): List<PhaseAlert> {
        val endsAt = running?.endsAt?.takeIf { it.isAfter(now) } ?: return emptyList()
        val warnAt = endsAt.minus(WARNING_BEFORE)
        return listOfNotNull(
            PhaseAlert(warnAt, PhaseAlert.Kind.Warning, running.onBreak).takeIf { warning && warnAt.isAfter(now) },
            PhaseAlert(endsAt, PhaseAlert.Kind.End, running.onBreak),
        )
    }
}
