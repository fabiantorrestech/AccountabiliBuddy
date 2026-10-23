package com.accountabilibuddy.companion.pause

import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId

/** How long the phone's guiding stays off once the wait is over. */
enum class PauseLength {
    /** Back on at midnight. */
    RestOfDay,

    /** Back on at a chosen time ([PauseState.untilTime], 2 AM by default). */
    UntilTime,

    /** Off until turned back on; a reminder each morning. */
    UntilOnRemind,

    /** Off until turned back on; no reminders. */
    UntilOnQuiet,
}

/**
 * The phone's master switch (like Curbox's): turning AccountabiliBuddy off on the phone first waits ([waitMinutes]:
 * 10 by default, or 30, 60 or a day), then stays off for the chosen [length]. Turning it back on is at once.
 *
 * @property requestedAt When "turn off" was asked; null when no request waits.
 * @property pausedAt When the wait ended and guiding went off; null while on.
 * @property quiet Stretches when the phone wasn't guiding (paused or in the car): their unlocks aren't pickups.
 */
data class PauseState(
    val requestedAt: Instant? = null,
    val waitMinutes: Int = 10,
    val length: PauseLength = PauseLength.RestOfDay,
    val untilTime: LocalTime = LocalTime.of(2, 0),
    val pausedAt: Instant? = null,
    val carPause: Boolean = false,
    val inCar: Boolean = false,
    val lastReminder: LocalDate? = null,
    val quiet: List<QuietStretch> = emptyList(),
)

/** A stretch when the phone wasn't guiding; [end] is null while it lasts. */
data class QuietStretch(val start: Instant, val end: Instant?)

/** What the switch says now. */
sealed interface PauseStatus {
    data object On : PauseStatus
    data class Waiting(val left: Duration) : PauseStatus
    data class Paused(val until: Instant?) : PauseStatus
}

/** The switch's rules, plain Kotlin so the checks run them. */
object PauseRules {
    val WAITS: List<Int> = listOf(10, 30, 60, 24 * 60)
    private val KEPT: Duration = Duration.ofDays(14)

    /** Applies a request whose wait is over, and ends a pause whose time is up; returns the state as it is now. */
    fun advance(state: PauseState, now: Instant, zone: ZoneId): PauseState {
        var next = state
        val requested = next.requestedAt
        if (requested != null && next.pausedAt == null && !now.isBefore(requested.plus(Duration.ofMinutes(next.waitMinutes.toLong())))) {
            val pausedAt = requested.plus(Duration.ofMinutes(next.waitMinutes.toLong()))
            next = next.copy(requestedAt = null, pausedAt = pausedAt, quiet = next.quiet + QuietStretch(pausedAt, null))
        }

        val until = next.pausedAt?.let { until(next, it, zone) }
        if (until != null && !now.isBefore(until)) {
            next = resume(next, until)
        }

        return next.copy(quiet = next.quiet.filter { (it.end ?: now) >= now.minus(KEPT) })
    }

    fun status(state: PauseState, now: Instant, zone: ZoneId): PauseStatus {
        state.pausedAt?.let { return PauseStatus.Paused(until(state, it, zone)) }
        state.requestedAt?.let { return PauseStatus.Waiting(Duration.between(now, it.plus(Duration.ofMinutes(state.waitMinutes.toLong()))).coerceAtLeast(Duration.ZERO)) }
        return PauseStatus.On
    }

    /** True while the phone shouldn't guide: paused, or in the car with "Pause in the car" on. */
    fun isQuiet(state: PauseState): Boolean = state.pausedAt != null || (state.carPause && state.inCar)

    /** When a pause that began at [pausedAt] ends; null for "until turned back on". */
    fun until(state: PauseState, pausedAt: Instant, zone: ZoneId): Instant? {
        val local = pausedAt.atZone(zone)
        return when (state.length) {
            PauseLength.RestOfDay -> local.toLocalDate().plusDays(1).atStartOfDay(zone).toInstant()
            PauseLength.UntilTime -> {
                val today = local.toLocalDate().atTime(state.untilTime).atZone(zone)
                (if (today.isAfter(local)) today else today.plusDays(1)).toInstant()
            }
            PauseLength.UntilOnRemind, PauseLength.UntilOnQuiet -> null
        }
    }

    /** Back on at [at]: the request and the pause go, and the quiet stretch it opened ends. */
    fun resume(state: PauseState, at: Instant): PauseState =
        state.copy(requestedAt = null, pausedAt = null, quiet = closeOpen(state.quiet, at, keepCar = state.carPause && state.inCar))

    /** The car connected or disconnected: a quiet stretch opens or closes (only with "Pause in the car" on). */
    fun car(state: PauseState, inCar: Boolean, at: Instant): PauseState {
        if (inCar == state.inCar) {
            return state
        }

        val wasQuiet = isQuiet(state)
        val next = state.copy(inCar = inCar)
        val nowQuiet = isQuiet(next)
        return when {
            !wasQuiet && nowQuiet -> next.copy(quiet = next.quiet + QuietStretch(at, null))
            wasQuiet && !nowQuiet -> next.copy(quiet = closeOpen(next.quiet, at, keepCar = false))
            else -> next
        }
    }

    /** True for an unlock in a quiet stretch (not a pickup). */
    fun inQuiet(state: PauseState, at: Instant): Boolean = state.quiet.any { at >= it.start && (it.end == null || at < it.end) }

    /** A morning reminder is due: paused "until turned back on, remind me", after 8 AM, not yet today. */
    fun reminderDue(state: PauseState, now: Instant, zone: ZoneId): Boolean {
        val local = now.atZone(zone)
        return state.pausedAt != null && state.length == PauseLength.UntilOnRemind && local.toLocalTime() >= LocalTime.of(8, 0)
            && state.lastReminder != local.toLocalDate() && local.toLocalDate().isAfter(state.pausedAt.atZone(zone).toLocalDate())
    }

    private fun closeOpen(stretches: List<QuietStretch>, at: Instant, keepCar: Boolean): List<QuietStretch> =
        if (keepCar) stretches else stretches.map { if (it.end == null) it.copy(end = at) else it }
}
