package com.accountabilibuddy.shared.session

import java.time.Duration
import java.time.Instant

/**
 * A phone focus session (ADR 85) as the pure engine sees it: one immutable value, replaced by every transition of
 * [PhoneSessionEngine]. No Android types and no clock: every time in it is a trusted time handed in (R12).
 *
 * @property phaseStartedAt The phase's start as the sync file's `running` says it: moved later by each pause (a Resume, lunch end, device-off gap).
 * @property phaseEndsAt The phase's set end, moved the same way; null = no set end. While a stretch that pauses the timer is open it keeps
 * the old value, and [effectiveEnd] adds the time away so far (the PC's rule).
 * @property isTest A test session: never counted, never synced, ends on command, no +10 minutes.
 * @property away The stretches away, oldest first; the open one (if any) is last.
 * @property extensions How many times +10 minutes was used (kept in the 90-day log).
 * @property endRequestedAt When the user opened End: the AccountabiliBuddy wait counts from here, so confirming at once and waiting are the same wait.
 * @property heartbeatAt The last trusted time the session was seen alive (R13).
 * @property offAt When the phone said it was going off (`ACTION_SHUTDOWN`), else null.
 * @property heldAway The kind of stretch an untrusted-clock hold interrupted at the phone's start (the hold's DeviceOff stretch is open); it is opened again when the hold ends.
 * @property snapshot What the session's profile turned on, copied at the start (PS2c review S3): the one thing the enforcement readers consult.
 * @property usage Pickups and distraction inside focus (PS2c): set once, when the session has ended; phone-local, never in the sync file (R14).
 * @property endedAt Null while it runs. A record with an end is history: no transition touches it.
 */
data class PhoneSessionRecord(
    val id: String,
    val profileId: String,
    val profileName: String,
    val startedAt: Instant,
    val phaseStartedAt: Instant,
    val phaseEndsAt: Instant?,
    val isTest: Boolean = false,
    val away: List<PhoneAway> = emptyList(),
    val checklist: PhoneSessionChecklist = PhoneSessionChecklist(),
    val currentTask: String? = null,
    val extensions: Int = 0,
    val endRequestedAt: Instant? = null,
    val armedEnd: PhoneArmedEnd? = null,
    val heartbeatAt: Instant = startedAt,
    val offAt: Instant? = null,
    val endedAt: Instant? = null,
    val endKind: PhoneSessionEndKind? = null,
    val heldAway: PhoneAwayKind? = null,
    val usage: PhoneSessionUsageSummary? = null,
    val snapshot: PhoneSessionProfileSnapshot = PhoneSessionProfileSnapshot.NONE,
) {
    /**
     * True while the store says it runs. Never a comparison with a caller's clock (B1): a phone clock moved past the set end must not
     * release any lock; only [PhoneSessionEngine.tick], called on a trusted time, ends a session.
     */
    val isRunning: Boolean get() = endedAt == null

    val noSetEnd: Boolean get() = phaseEndsAt == null

    /** The stretch away that is still going, or null. */
    val openAway: PhoneAway? get() = away.lastOrNull()?.takeIf { it.isOpen }

    val isAway: Boolean get() = openAway != null

    /** The countdown is frozen: Pause timer, lunch (a step away keeps it running). */
    val isTimerPaused: Boolean get() = openAway?.kind?.pausesTimer == true

    /** The planned length of the phase (kept under pauses: both ends move together); null for no set end. */
    val length: Duration? get() = phaseEndsAt?.let { Duration.between(phaseStartedAt, it) }

    /**
     * When the set end falls at [at]: [phaseEndsAt] moved on by the time since the open pausing stretch began (the countdown is frozen,
     * so the session ends that much later); null for no set end.
     */
    fun effectiveEnd(at: Instant): Instant? {
        val end = phaseEndsAt ?: return null
        val open = openAway?.takeIf { it.kind.pausesTimer } ?: return end
        return if (at.isAfter(open.from)) end.plus(Duration.between(open.from, at)) else end
    }

    /** An untrusted-clock hold is on: a DeviceOff stretch is open ([PhoneSessionOffGap.Decision.Hold]) and waits for trusted time. */
    val holdsDeviceOff: Boolean get() = openAway?.kind == PhoneAwayKind.DEVICE_OFF

    /** The start of a gap with the phone off (R13): when it said it was going off, else the last heartbeat. */
    val offFrom: Instant get() = offAt ?: heartbeatAt

    /**
     * Every time of the record moved by [by] (B2 of the PS2a review): the times a session wrote on a clock that could not be checked, moved by the
     * correction when the network time arrives, so the start, the end, the stretches and the heartbeat are on the trusted time again.
     */
    fun shifted(by: Duration): PhoneSessionRecord = copy(
        startedAt = startedAt.plus(by),
        phaseStartedAt = phaseStartedAt.plus(by),
        phaseEndsAt = phaseEndsAt?.plus(by),
        away = away.map { it.copy(from = it.from.plus(by), to = it.to?.plus(by)) },
        checklist = checklist.copy(sessionStart = checklist.sessionStart?.plus(by)),
        endRequestedAt = endRequestedAt?.plus(by),
        armedEnd = armedEnd?.copy(requestedAt = armedEnd.requestedAt.plus(by), readyAt = armedEnd.readyAt.plus(by)),
        heartbeatAt = heartbeatAt.plus(by),
        offAt = offAt?.plus(by),
        endedAt = endedAt?.plus(by),
    )

    /** Only an End in progress moved by [by]: its request time and its armed end's times (written during a hold on an unchecked clock). */
    fun endShifted(by: Duration): PhoneSessionRecord = copy(
        endRequestedAt = endRequestedAt?.plus(by),
        armedEnd = armedEnd?.copy(requestedAt = armedEnd.requestedAt.plus(by), readyAt = armedEnd.readyAt.plus(by)),
    )
}
