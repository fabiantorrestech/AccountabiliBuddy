package com.accountabilibuddy.shared.sync

import java.time.Duration
import java.time.Instant
import java.util.UUID

/**
 * How often the phone was picked up (unlocked) during another device's counted session (ADR 26: times and counts only),
 * as the phone's sync file carries it; as on the PC (Core's SyncPickup).
 *
 * @property deviceId The device whose session it is.
 * @property sessionStart That session's start, which names it.
 * @property count Unlocks from the session's start until [countedUntil].
 * @property longestDownMinutes The longest stretch in that time without an unlock.
 * @property countedUntil The session's end, or when the phone last counted a running one.
 */
data class SyncPickup(val deviceId: UUID, val sessionStart: Instant, val count: Int, val longestDownMinutes: Int, val countedUntil: Instant) {
    companion object {
        /** Sessions this old aren't counted (the files carry 14 days). */
        private val KEPT: Duration = Duration.ofDays(14)

        /**
         * One entry per counted session of another device ([sessions]: device id and session; hand-added ones are left
         * out) that started in the last 14 days, from the phone's unlock times. A running session counts until [now].
         */
        fun count(sessions: List<Pair<UUID, SyncSession>>, unlocks: List<Instant>, now: Instant): List<SyncPickup> {
            val sorted = unlocks.sorted()
            return sessions
                .filter { (_, session) -> !session.added && session.start >= now.minus(KEPT) && session.start < now }
                .map { (deviceId, session) ->
                    val end = minOf(session.end ?: now, now)
                    val inside = sorted.filter { it >= session.start && it < end }
                    val marks = listOf(session.start) + inside + end
                    val longest = marks.zipWithNext { a, b -> Duration.between(a, b) }.maxOrNull() ?: Duration.ZERO
                    SyncPickup(deviceId, session.start, inside.size, longest.toMinutes().toInt(), end)
                }
        }
    }
}
