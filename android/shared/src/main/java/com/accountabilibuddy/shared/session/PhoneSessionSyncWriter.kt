package com.accountabilibuddy.shared.session

import com.accountabilibuddy.shared.sync.SessionPieces
import com.accountabilibuddy.shared.sync.SyncRunning
import com.accountabilibuddy.shared.sync.SyncSession
import java.time.Instant
import java.time.ZoneId

/**
 * What the session part of this phone's sync file is made from (PS2b): a snapshot the controller takes under its lock, so the file never mixes two
 * states. Pure data.
 *
 * @property running The running session (a test one too), null when none.
 * @property log Ended sessions of the last 90 days, never a test one.
 * @property logUnreadable The log could not be opened and has no trusted history start yet (B5): the file says nothing about sessions.
 * @property historyStartsAt The time the phone's history starts from after a lost log (B5), else null.
 * @property runningUnchecked The running session's times were written on a clock that could not be checked and are not corrected yet (V-N1).
 * @property uncheckedEnded Ended sessions in the same state (V-N1): they count on this phone, the file leaves them out until they are corrected.
 * @property sessionNow The session clock's time now (R12). It stamps the file only while a real session runs (S2): otherwise the file's time is the phone's
 * trusted time, so a clock that was fixed after the app started never leaves a stamp ahead of the others' edits.
 * @property stampable [sessionNow] may be written as the file's time (B4): the clock is network-backed, or no network time can ever come (below Android 13).
 */
data class PhoneSessionSyncInput(
    val running: PhoneSessionRecord?,
    val log: List<PhoneSessionRecord>,
    val logUnreadable: Boolean,
    val historyStartsAt: Instant?,
    val runningUnchecked: Boolean,
    val uncheckedEnded: Set<String>,
    val sessionNow: Instant,
    val stampable: Boolean,
    val zone: ZoneId,
)

/**
 * The session part of a sync file: the file's own time ([writtenAt], B4), the `running` card, the counted sessions of the last 14 days as pieces
 * and the time the file speaks from. [coversFrom] is never null (plan check S2): a phone that has nothing to say about its history says
 * "from now", which makes receivers keep every session they hold.
 */
data class PhoneSessionSyncPart(val writtenAt: Instant, val running: SyncRunning?, val sessions: List<SyncSession>, val coversFrom: Instant)

/**
 * Makes the [PhoneSessionSyncPart] of this phone's file (ADR 85, plan section 2) from the controller's snapshot: [SessionPieces] cuts the pieces
 * exactly as the PC does and builds the `running` card; this decides WHAT may travel and with which time. Pure.
 *
 * The rules, each from the plan check:
 *  - **B4** the file is stamped with the session clock, never the phone's wall clock; when that clock can't be checked yet (Android 13 and later, before
 *    the network time) the file says nothing about sessions at all.
 *  - **B5** an unreadable log is not an empty one: no `running`, no sessions, `coversFrom` = the time written, so receivers keep what they hold. After
 *    the log started again, `coversFrom` is never earlier than its trusted history start.
 *  - **V-N1** a session whose times were written on an unchecked clock is not sent until the network time corrected them.
 *  - **R14** nothing else travels: [SessionPieces] writes the profile's name and the checklist's GUID ids, never an app name, a lock or Curbox name,
 *    a pickup or an end reason (none of those is in the record's sync shape).
 */
object PhoneSessionSyncWriter {
    /** The part for the file. [fallbackNow] is the phone's trusted time when the session clock can't stamp the file ([PhoneSessionSyncInput.stampable] false). */
    fun forFile(input: PhoneSessionSyncInput, fallbackNow: Instant): PhoneSessionSyncPart {
        val stamp = if (input.stampable && input.running?.isTest == false) input.sessionNow else fallbackNow
        if (input.logUnreadable || !input.stampable) return silent(stamp)

        val everything = input.log + listOfNotNull(input.running)
        val sendable = input.log.filter { it.id !in input.uncheckedEnded } + listOfNotNull(input.running?.takeIf { !input.runningUnchecked })
        val built = SessionPieces.build(sendable, stamp, input.zone, everything.minOfOrNull { it.startedAt })
        val coversFrom = input.historyStartsAt?.let { maxOf(built.coversFrom, it) } ?: built.coversFrom
        val running = input.running?.takeIf { !input.runningUnchecked }?.let(SessionPieces::runningOf)
        return PhoneSessionSyncPart(stamp, running, built.sessions, coversFrom)
    }

    /**
     * The phone's own sessions as a device's part, for counting on this phone (Today, Stats, goals, focus days): every session the phone holds, a
     * not yet corrected one too (it is this phone's own data), stamped with the session clock. Null when the phone holds no session at all.
     */
    fun forCounting(input: PhoneSessionSyncInput): PhoneSessionSyncPart? {
        val all = input.log + listOfNotNull(input.running)
        if (all.none { !it.isTest }) return null

        // The phone's whole 90-day log counts (a receiver keeps 90 days of another device's sessions too), not the file's 14.
        val built = SessionPieces.build(all, input.sessionNow, input.zone, all.minOfOrNull { it.startedAt }, SessionPieces.KEPT_FOR)
        return PhoneSessionSyncPart(input.sessionNow, input.running?.let(SessionPieces::runningOf), built.sessions, built.coversFrom)
    }

    private fun silent(stamp: Instant): PhoneSessionSyncPart {
        val built = SessionPieces.whenLogUnreadable(stamp)
        return PhoneSessionSyncPart(stamp, null, built.sessions, built.coversFrom)
    }
}
