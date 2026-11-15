package com.accountabilibuddy.shared.session

import java.time.Duration
import java.time.Instant

/** A running session as saved: the record and the clock anchor it counts with (R12), so a restart in the same boot carries on. */
data class PhoneSessionCheckpoint(
    val record: PhoneSessionRecord,
    val anchor: PhoneSessionAnchor,
    /** The time zone the session started in: the new-day close of a gap with the phone off is judged in it (S1), whatever the phone says later. Null in an older file. */
    val zoneId: String? = null,
    /** The record's times were written on an anchor that was not network-backed and are not corrected yet (B2): the network time arriving moves them all. */
    val startUntrusted: Boolean = false,
    /** An End in progress (its request time and armed end) was written on an unchecked clock and is not corrected yet (B2). */
    val endUntrusted: Boolean = false,
)

/**
 * What the phone being off did to a session (R13), kept until the user has seen it. Times only, never text.
 *
 * @property untrusted The phone's time could not be checked (no network time yet), so the session was only paused.
 */
data class PhoneSessionNotice(val sessionId: String, val from: Instant, val to: Instant, val closed: Boolean, val untrusted: Boolean)

/**
 * Everything the phone keeps about its sessions in `phone-sessions.sealed` (never synced, never in a backup): the running session, the
 * last 90 days of ended ones, the off-gap notice, and [historyStartsAt].
 *
 * @property log Ended sessions (never a test), oldest first, kept [KEPT_FOR].
 * @property historyStartsAt Set when the log could not be opened and was started again (B5): the phone knows no session from before this time, so
 * its sync file's `coversFrom` is never earlier than it (receivers keep what they hold).
 */
data class PhoneSessionStoreData(
    val checkpoint: PhoneSessionCheckpoint? = null,
    val log: List<PhoneSessionRecord> = emptyList(),
    val notice: PhoneSessionNotice? = null,
    val historyStartsAt: Instant? = null,
) {
    /**
     * The log after the session ended: the record is added; those older than [KEPT_FOR] are dropped only when [prune] (a trusted [now], S6: an unchecked
     * clock set far ahead must not sweep the log, which would make the sync file tell receivers the phone has no history).
     */
    fun withEnded(record: PhoneSessionRecord, now: Instant, prune: Boolean, capAtNewest: Boolean): PhoneSessionStoreData {
        val all = log.filter { it.id != record.id } + record
        // Capped (below Android 13, an unchecked clock): never past the newest end already kept (V-S1).
        val base = if (capAtNewest) log.mapNotNull { it.endedAt }.maxOrNull()?.let { minOf(now, it) } ?: now else now
        return copy(checkpoint = null, log = if (prune) prune(all, base) else all)
    }

    companion object {
        val KEPT_FOR: Duration = Duration.ofDays(90)

        /** The ended sessions that are not older than [KEPT_FOR] at [now] (by their end). */
        fun prune(records: List<PhoneSessionRecord>, now: Instant): List<PhoneSessionRecord> {
            val limit = now.minus(KEPT_FOR)
            return records.filter { (it.endedAt ?: it.startedAt).isAfter(limit) }
        }
    }
}

/** What reading `phone-sessions.sealed` gave: a missing file is an empty phone; one that can't be opened is NOT empty (B5). */
sealed interface PhoneSessionRead {
    data object Missing : PhoneSessionRead
    data object Unreadable : PhoneSessionRead
    data class Readable(val data: PhoneSessionStoreData) : PhoneSessionRead
}

/** One kept end reason (private text: sealed, 90 days, never synced, never logged). */
data class PhoneSessionReason(val sessionId: String, val at: Instant, val text: String) {
    override fun toString(): String = "PhoneSessionReason(sessionId=$sessionId, at=$at)"
}
