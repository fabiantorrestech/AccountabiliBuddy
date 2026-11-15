package com.accountabilibuddy.shared.session

import com.accountabilibuddy.shared.sync.SessionTaskItem
import java.time.Duration
import java.time.Instant

/**
 * The phone session's pure transitions (ADR 85, R6 to R13): each takes the record and a TRUSTED `now` and returns the new record with the
 * effects the controller carries out (save, sync file, alarms, enforcement). No clock, no Android, no I/O. Only the phone ends its own
 * session (R6): by [endEarly], [applyArmed], [tick], [endAtOnce], [endOnCommand] or [applyOffGap].
 */
object PhoneSessionEngine {
    /** R10: the longest a phone session's phase may be made (a start or +10 minutes never goes past it). */
    val MAX_LENGTH: Duration = Duration.ofHours(12)

    private val ADD_TEN: Duration = Duration.ofMinutes(10)

    /** How long an opened End keeps its wait (PS1b review N2). */
    val END_REQUEST_KEPT_FOR: Duration = Duration.ofMinutes(10)

    /** The start sheet's longest choice, in minutes. */
    const val MAX_MINUTES = 12 * 60

    /**
     * A session starts at [now]: [lengthMinutes] long, or with no set end when null. [tasks] is This session's list at the start (GUID ids);
     * the checklist is "made before the start" when it has any (R9). Whether a start is allowed at all (R1, another device live) is the
     * caller's rule, not this one's.
     */
    fun start(
        id: String,
        profileId: String,
        profileName: String,
        lengthMinutes: Int?,
        now: Instant,
        tasks: List<SessionTaskItem> = emptyList(),
        isTest: Boolean = false,
    ): PhoneSessionResult {
        if (lengthMinutes != null && (lengthMinutes < 1 || lengthMinutes > MAX_MINUTES)) {
            return PhoneSessionResult.Refused(PhoneSessionRefusal.BAD_LENGTH)
        }

        val end = lengthMinutes?.let { now.plus(Duration.ofMinutes(it.toLong())) }
        val record = PhoneSessionRecord(
            id = id,
            profileId = profileId,
            profileName = profileName,
            startedAt = now,
            phaseStartedAt = now,
            phaseEndsAt = end,
            isTest = isTest,
            checklist = PhoneSessionChecklist(tasks).started(now),
        )
        return PhoneSessionResult.Done(record, changed(record) + listOf(PhoneSessionEffect.ScheduleEndAlarm(end), PhoneSessionEffect.EnforceUntil(end)))
    }

    // --- Away -------------------------------------------------------------------------------------------------------------------

    /** Step away (ADR 77): the timer keeps running, enforcement unchanged; the stretch is logged away and out of focus. */
    fun stepAway(record: PhoneSessionRecord, now: Instant): PhoneSessionResult =
        open(record, now, PhoneAwayKind.STEP_AWAY)

    /** "I'm back" from a step away: moves nothing (the timer never stopped). */
    fun back(record: PhoneSessionRecord, now: Instant): PhoneSessionResult {
        val open = record.openAway
        return when {
            !record.isRunning -> PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_RUNNING)
            open == null -> PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_AWAY)
            open.kind != PhoneAwayKind.STEP_AWAY -> PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_STEPPED_AWAY)
            else -> {
                val next = closeOpenAway(record, now)
                PhoneSessionResult.Done(next, changed(next))
            }
        }
    }

    /** Pause timer: the countdown freezes, enforcement unchanged; on [resume] start and end move later by the pause. */
    fun pause(record: PhoneSessionRecord, now: Instant): PhoneSessionResult =
        open(record, now, PhoneAwayKind.PAUSED)

    /**
     * Resume after Pause timer: start and end move later by the pause (the PC's `SessionEngine.PauseTimer` rule); enforcement is asked again
     * for the new end. When a lunch is running at that moment ([lunchRunning]) the lunch keeps its pause: the Pause timer stretch becomes the
     * lunch's, from now, and the timer does not restart (hard rules: lunch always pauses; PS1b review S3).
     */
    fun resume(record: PhoneSessionRecord, now: Instant, lunchRunning: Boolean = false): PhoneSessionResult {
        val open = record.openAway
        return when {
            !record.isRunning -> PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_RUNNING)
            open == null || open.kind != PhoneAwayKind.PAUSED -> PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_PAUSED)
            else -> {
                var next = closeOpenAway(record, now)
                if (lunchRunning) next = next.copy(away = next.away + PhoneAway(PhoneAwayKind.LUNCH, now, null))
                PhoneSessionResult.Done(next, changed(next) + endEffects(next))
            }
        }
    }

    /**
     * Lunch began on some device: the countdown pauses from [from] (the lunch's start as the controller saw it: clamped to the session's
     * start, the end of the last stretch and [now]). It is recorded from what was OBSERVED (B2): a later Discard of the lunch record never
     * shrinks a stretch that is already here, so the controller passes the time it first saw the lunch running. A step away in progress
     * becomes the lunch (hard rules: lunch turns a running-timer Step away into its own); a Pause timer already freezes the countdown and
     * stays as it is.
     */
    fun lunchHold(record: PhoneSessionRecord, from: Instant, now: Instant): PhoneSessionResult {
        if (!record.isRunning) return PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_RUNNING)
        val open = record.openAway
        if (open != null && open.kind != PhoneAwayKind.STEP_AWAY) return PhoneSessionResult.Done(record)
        // A late alarm plus a lunch must not count minutes past the set end.
        if (record.effectiveEnd(now)?.let { !now.isBefore(it) } == true) return PhoneSessionResult.Refused(PhoneSessionRefusal.TIME_IS_UP)
        val lastEnd = record.away.lastOrNull { !it.isOpen }?.to
        var at = maxOf(from, record.startedAt, lastEnd ?: record.startedAt)
        if (open != null) at = maxOf(at, open.from)
        at = minOf(at, now)
        // `now` can be earlier than the session's start or the last stretch (a clock that was not yet anchored): never open a stretch before them (S2 of the PS2a review).
        at = maxOf(at, record.startedAt, lastEnd ?: record.startedAt)
        if (open != null) at = maxOf(at, open.from)
        val closed = if (open != null) closeOpenAway(record, at) else record
        val next = closed.copy(away = closed.away + PhoneAway(PhoneAwayKind.LUNCH, at, null))
        return PhoneSessionResult.Done(next, changed(next))
    }

    /**
     * Lunch is no longer running (counted, discarded, ended or a record received): the stretch ends at [now], the time it was first seen
     * not running (B2), and start and end move later by it. Nothing happens when no lunch stretch is open.
     */
    fun lunchEnd(record: PhoneSessionRecord, now: Instant): PhoneSessionResult {
        val open = record.openAway
        if (!record.isRunning || open == null || open.kind != PhoneAwayKind.LUNCH) return PhoneSessionResult.Done(record)
        val next = closeOpenAway(record, now)
        return PhoneSessionResult.Done(next, changed(next) + endEffects(next))
    }

    // --- Length -----------------------------------------------------------------------------------------------------------------

    /**
     * Add 10 minutes (R10): only in a timed focus phase that is not away or paused, not no-set-end, not a test; at once (it is stricter);
     * the phase may not pass 12 hours; enforcement is asked again for the new end.
     */
    fun add10(record: PhoneSessionRecord, now: Instant): PhoneSessionResult {
        val end = record.phaseEndsAt
        val length = record.length
        return when {
            !record.isRunning -> PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_RUNNING)
            record.isTest -> PhoneSessionResult.Refused(PhoneSessionRefusal.IS_TEST)
            end == null || length == null -> PhoneSessionResult.Refused(PhoneSessionRefusal.NO_SET_END)
            record.isAway -> PhoneSessionResult.Refused(PhoneSessionRefusal.ALREADY_AWAY)
            !now.isBefore(end) -> PhoneSessionResult.Refused(PhoneSessionRefusal.TIME_IS_UP)
            length.plus(ADD_TEN) > MAX_LENGTH -> PhoneSessionResult.Refused(PhoneSessionRefusal.TOO_LONG)
            else -> {
                val next = record.copy(phaseEndsAt = end.plus(ADD_TEN), extensions = record.extensions + 1)
                PhoneSessionResult.Done(next, changed(next) + endEffects(next))
            }
        }
    }

    // --- The clock --------------------------------------------------------------------------------------------------------------

    /**
     * Called on a trusted time (the alarm, a refresh): the set end passing ends it as TIME_UP at the end moment (a stretch away that keeps the
     * timer running does not stop it; a frozen countdown never ends); an armed end whose wait is over applies (R8). Nothing else ends a
     * session by the clock.
     */
    fun tick(record: PhoneSessionRecord, now: Instant): PhoneSessionResult {
        if (!record.isRunning) return PhoneSessionResult.Done(record)
        val end = record.effectiveEnd(now)
        if (end != null && !record.isTimerPaused && !now.isBefore(end)) {
            return PhoneSessionResult.Done(finish(record, end, PhoneSessionEndKind.TIME_UP), endedEffects(record, PhoneSessionEndKind.TIME_UP, null))
        }

        val armed = record.armedEnd
        if (armed != null && !now.isBefore(armed.readyAt)) return applyArmed(record, now)
        return PhoneSessionResult.Done(record)
    }

    /**
     * Records that the phone is alive at [now] (R13): saved at every refresh and state change. Changes nothing while an untrusted-clock hold is
     * open. PS2a must never pass a time from an untrusted anchor to this or to [phoneOff]: [PhoneSessionOffGap.decide] treats them as trusted.
     */
    fun heartbeat(record: PhoneSessionRecord, now: Instant, timeIsTrusted: Boolean): PhoneSessionResult {
        if (!timeIsTrusted || !record.isRunning || record.holdsDeviceOff || !now.isAfter(record.heartbeatAt)) return PhoneSessionResult.Done(record)
        return PhoneSessionResult.Done(record.copy(heartbeatAt = now), listOf(PhoneSessionEffect.Persist))
    }

    /** `ACTION_SHUTDOWN`: the phone is going off at [now] (R13). Changes nothing while a hold is open (see [heartbeat]). */
    fun phoneOff(record: PhoneSessionRecord, now: Instant, timeIsTrusted: Boolean): PhoneSessionResult {
        if (!timeIsTrusted || !record.isRunning || record.holdsDeviceOff) return PhoneSessionResult.Done(record)
        return PhoneSessionResult.Done(record.copy(offAt = now, heartbeatAt = maxOf(now, record.heartbeatAt)), listOf(PhoneSessionEffect.Persist))
    }

    /**
     * Carries out [decision] ([PhoneSessionOffGap.decide]) at the next app start [now]: a close ends the session at that time (PHONE_OFF; the
     * set end that passed before it for SET_END_PASSED, still told as ended by time); a pause makes a DeviceOff stretch (out of focus, the
     * end moves on by the gap) and the next piece carries `offMinutesBefore` ([com.accountabilibuddy.shared.sync.SessionPieces]); a hold
     * (untrusted clock) opens the DeviceOff stretch and keeps `offAt` until trusted time decides (a later pause closes it, a close ends the session).
     *
     * @param timeIsTrusted [now] may be saved as a heartbeat (network-backed, or a phone that can never have network time: the controller decides). When it is not, the heartbeat is left as it was: an untrusted time is never saved as a heartbeat
     * ([PhoneSessionOffGap.decide] treats the stored heartbeat as trusted; PS2a review of the PS1b engine).
     */
    fun applyOffGap(record: PhoneSessionRecord, decision: PhoneSessionOffGap.Decision, now: Instant, timeIsTrusted: Boolean): PhoneSessionResult {
        if (!record.isRunning) return PhoneSessionResult.Done(record)
        return when (decision) {
            PhoneSessionOffGap.Decision.Nothing ->
                PhoneSessionResult.Done(record.copy(offAt = null, heartbeatAt = beat(record, now, timeIsTrusted)), listOf(PhoneSessionEffect.Persist))
            is PhoneSessionOffGap.Decision.Close -> {
                val kind = if (decision.why == PhoneSessionOffGap.CloseWhy.SET_END_PASSED) PhoneSessionEndKind.TIME_UP else PhoneSessionEndKind.PHONE_OFF
                val ended = finish(record, decision.at, kind)
                PhoneSessionResult.Done(
                    ended,
                    endedEffects(record, kind, null) + PhoneSessionEffect.OffGapNotice(record.offFrom, now, closed = true),
                )
            }
            is PhoneSessionOffGap.Decision.Hold -> {
                if (record.holdsDeviceOff) {
                    PhoneSessionResult.Done(record, listOf(PhoneSessionEffect.Persist))
                } else {
                    // The clock can't be trusted yet: the timer is paused from the moment the phone went off, until trusted time decides.
                    val open = record.openAway
                    var next = record
                    if (open != null) next = closeOpenAway(next, maxOf(decision.from, open.from))
                    val at = maxOf(decision.from, next.startedAt)
                    next = next.copy(away = next.away + PhoneAway(PhoneAwayKind.DEVICE_OFF, at, null), offAt = decision.from, heldAway = open?.kind)
                    PhoneSessionResult.Done(next, changed(next))
                }
            }
            is PhoneSessionOffGap.Decision.Pause -> {
                if (record.holdsDeviceOff) {
                    // The hold ends where trusted time puts it: the stretch closes (the end moves on by it), what it interrupted goes on.
                    var next = closeOpenAway(record, decision.to)
                    next.heldAway?.let { kind -> next = next.copy(away = next.away + PhoneAway(kind, decision.to, null)) }
                    next = next.copy(offAt = null, heldAway = null, heartbeatAt = beat(next, now, timeIsTrusted))
                    return PhoneSessionResult.Done(next, changed(next) + endEffects(next) + PhoneSessionEffect.OffGapNotice(decision.from, decision.to, closed = false))
                }
                val open = record.openAway
                var next = record
                if (open != null) next = closeOpenAway(next, maxOf(decision.from, open.from))
                next = addClosedAway(next, PhoneAwayKind.DEVICE_OFF, decision.from, decision.to)
                if (open != null) next = next.copy(away = next.away + PhoneAway(open.kind, decision.to, null))
                next = next.copy(offAt = null, heartbeatAt = beat(next, now, timeIsTrusted))
                PhoneSessionResult.Done(next, changed(next) + endEffects(next) + PhoneSessionEffect.OffGapNotice(decision.from, decision.to, closed = false))
            }
        }
    }

    // --- Ending -----------------------------------------------------------------------------------------------------------------

    /** The user opened End (R8): the wait counts from here. Opening again keeps the first time. Nothing for a session that ends on command. */
    fun openEnd(record: PhoneSessionRecord, now: Instant): PhoneSessionResult {
        if (!record.isRunning) return PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_RUNNING)
        if (record.endRequestedAt != null) return PhoneSessionResult.Done(record)
        val next = record.copy(endRequestedAt = now)
        return PhoneSessionResult.Done(next, listOf(PhoneSessionEffect.Persist))
    }

    /**
     * End early (R8). With no terms (ProductaBuddy, no set end, a test) it ends at once as ON_COMMAND and the reason is not asked. With
     * terms: a reason that is too short is refused; before the wait is over (counted from [openEnd], else from now) the end is ARMED (the
     * record keeps running; it applies at the wait's end, [applyArmed]); after it the session ends as EARLY_END. The reason is kept sealed.
     */
    fun endEarly(record: PhoneSessionRecord, now: Instant, mode: PhoneSessionMode, settings: PhoneEndSettings, reason: String?): PhoneSessionResult {
        if (!record.isRunning) return PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_RUNNING)
        val terms = PhoneSessionEndRules.terms(mode, record, settings)
        if (terms.isNone) {
            return PhoneSessionResult.Done(finish(record, now, PhoneSessionEndKind.ON_COMMAND), endedEffects(record, PhoneSessionEndKind.ON_COMMAND, null))
        }

        if (!PhoneSessionEndRules.reasonLongEnough(terms, reason)) return PhoneSessionResult.Refused(PhoneSessionRefusal.REASON_TOO_SHORT)
        val text = reason?.trim().orEmpty()
        // An End sheet dismissed without Cancel does not keep its wait for hours: an older request counts as a new one.
        val requestedAt = record.endRequestedAt?.takeIf { !it.isAfter(now) && Duration.between(it, now) <= END_REQUEST_KEPT_FOR } ?: now
        val readyAt = PhoneSessionEndRules.readyAt(terms, requestedAt)
        if (!now.isBefore(readyAt)) {
            return PhoneSessionResult.Done(finish(record, now, PhoneSessionEndKind.EARLY_END), endedEffects(record, PhoneSessionEndKind.EARLY_END, text))
        }

        val armed = record.copy(endRequestedAt = requestedAt, armedEnd = PhoneArmedEnd(requestedAt, readyAt, text))
        return PhoneSessionResult.Done(armed, listOf(PhoneSessionEffect.Persist, PhoneSessionEffect.ScheduleArmedAlarm(readyAt)))
    }

    /** Cancel an End in progress (free, R8): the wait and any armed end are dropped. */
    fun cancelEnd(record: PhoneSessionRecord): PhoneSessionResult {
        if (!record.isRunning) return PhoneSessionResult.Done(record)
        if (record.endRequestedAt == null && record.armedEnd == null) return PhoneSessionResult.Done(record)
        val next = record.copy(endRequestedAt = null, armedEnd = null)
        return PhoneSessionResult.Done(next, listOf(PhoneSessionEffect.Persist, PhoneSessionEffect.ScheduleArmedAlarm(null)))
    }

    /**
     * The armed end's alarm: applies once its wait is over, at [now] (late when the phone was asleep, never early). Refused when nothing is
     * armed or the wait still runs.
     */
    fun applyArmed(record: PhoneSessionRecord, now: Instant): PhoneSessionResult {
        if (!record.isRunning) return PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_RUNNING)
        val armed = record.armedEnd ?: return PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_ARMED)
        if (now.isBefore(armed.readyAt)) return PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_ARMED)
        return PhoneSessionResult.Done(finish(record, now, PhoneSessionEndKind.EARLY_END), endedEffects(record, PhoneSessionEndKind.EARLY_END, armed.reason))
    }

    /** "All tasks done" (R9, ADR 25): ends at once with the reason "All tasks done" when [PhoneSessionEndRules.canEndAtOnce] holds. */
    fun endAtOnce(record: PhoneSessionRecord, now: Instant): PhoneSessionResult {
        if (!record.isRunning) return PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_RUNNING)
        if (!PhoneSessionEndRules.canEndAtOnce(record, now)) return PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_ALL_DONE)
        return PhoneSessionResult.Done(
            finish(record, now, PhoneSessionEndKind.ALL_TASKS_DONE),
            endedEffects(record, PhoneSessionEndKind.ALL_TASKS_DONE, PhoneSessionEndRules.ALL_TASKS_DONE_REASON),
        )
    }

    /** A session that ends on command (no set end, a test) ends at once. Refused for a timed session: that is [endEarly]. */
    fun endOnCommand(record: PhoneSessionRecord, now: Instant): PhoneSessionResult {
        if (!record.isRunning) return PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_RUNNING)
        if (!PhoneSessionEndRules.endsOnCommand(record)) return PhoneSessionResult.Refused(PhoneSessionRefusal.ENDS_ON_COMMAND_ONLY)
        return PhoneSessionResult.Done(finish(record, now, PhoneSessionEndKind.ON_COMMAND), endedEffects(record, PhoneSessionEndKind.ON_COMMAND, null))
    }

    // --- The checklist ----------------------------------------------------------------------------------------------------------

    /** This session's list changed (a tick, an add, a removal, a park, a received edit): the checklist counts what left open (B6). */
    fun checklistChanged(record: PhoneSessionRecord, tasks: List<SessionTaskItem>): PhoneSessionResult {
        if (!record.isRunning) return PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_RUNNING)
        val next = record.copy(checklist = record.checklist.withTaskList(tasks))
        return PhoneSessionResult.Done(next, changed(next))
    }

    /**
     * Undo of a removed or parked task (S7 of the PS1b review): This session's list is [tasks] again, and when the task was OPEN when it left
     * its drop is taken back (decided by the ids the checklist kept, not by a flag), so "All tasks done" survives a remove plus Undo (the PC's `WithOneLessDropped`).
     */
    fun checklistUndo(record: PhoneSessionRecord, tasks: List<SessionTaskItem>): PhoneSessionResult {
        if (!record.isRunning) return PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_RUNNING)
        val next = record.copy(checklist = record.checklist.withUndone(tasks))
        return PhoneSessionResult.Done(next, changed(next))
    }

    // --- Helpers ----------------------------------------------------------------------------------------------------------------

    private fun open(record: PhoneSessionRecord, now: Instant, kind: PhoneAwayKind): PhoneSessionResult = when {
        !record.isRunning -> PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_RUNNING)
        record.isAway -> PhoneSessionResult.Refused(PhoneSessionRefusal.ALREADY_AWAY)
        !now.isBefore(record.effectiveEnd(now) ?: now.plusSeconds(1)) -> PhoneSessionResult.Refused(PhoneSessionRefusal.TIME_IS_UP)
        else -> {
            val next = record.copy(away = record.away + PhoneAway(kind, now, null))
            PhoneSessionResult.Done(next, changed(next))
        }
    }

    /** Closes the open stretch at [at] (never before it began); a stretch that pauses the timer moves the phase's start and end later by its length. */
    private fun closeOpenAway(record: PhoneSessionRecord, at: Instant): PhoneSessionRecord {
        val open = record.openAway ?: return record
        val to = maxOf(at, open.from)
        val closed = open.copy(to = to)
        val shifted = if (open.kind.pausesTimer) shift(record, Duration.between(open.from, to)) else record
        return shifted.copy(away = shifted.away.dropLast(1) + closed)
    }

    private fun addClosedAway(record: PhoneSessionRecord, kind: PhoneAwayKind, from: Instant, to: Instant): PhoneSessionRecord {
        val end = maxOf(to, from)
        val shifted = if (kind.pausesTimer) shift(record, Duration.between(from, end)) else record
        return shifted.copy(away = shifted.away + PhoneAway(kind, from, end))
    }

    private fun beat(record: PhoneSessionRecord, now: Instant, trusted: Boolean): Instant = if (trusted) maxOf(now, record.heartbeatAt) else record.heartbeatAt

    private fun shift(record: PhoneSessionRecord, by: Duration): PhoneSessionRecord =
        record.copy(phaseStartedAt = record.phaseStartedAt.plus(by), phaseEndsAt = record.phaseEndsAt?.plus(by))

    /** The session ends at [at] (never before it started): its open stretch is closed there, an armed end or a wait in progress is dropped. */
    private fun finish(record: PhoneSessionRecord, at: Instant, kind: PhoneSessionEndKind): PhoneSessionRecord {
        val end = maxOf(at, record.startedAt)
        val open = record.openAway
        val away = if (open != null) record.away.dropLast(1) + open.copy(to = maxOf(end, open.from)) else record.away
        return record.copy(away = away, endedAt = end, endKind = kind, armedEnd = null, endRequestedAt = null, offAt = null)
    }

    private fun changed(record: PhoneSessionRecord): List<PhoneSessionEffect> =
        if (record.isTest) listOf(PhoneSessionEffect.Persist) else listOf(PhoneSessionEffect.Persist, PhoneSessionEffect.WriteSyncFile)

    // The end moved: the alarm and the enforcement are asked again (R10, and on resume).
    private fun endEffects(record: PhoneSessionRecord): List<PhoneSessionEffect> =
        listOf(PhoneSessionEffect.ScheduleEndAlarm(record.phaseEndsAt), PhoneSessionEffect.EnforceUntil(record.phaseEndsAt))

    private fun endedEffects(record: PhoneSessionRecord, kind: PhoneSessionEndKind, reason: String?): List<PhoneSessionEffect> = buildList {
        add(PhoneSessionEffect.Persist)
        if (!record.isTest) add(PhoneSessionEffect.WriteSyncFile)
        add(PhoneSessionEffect.ScheduleEndAlarm(null))
        add(PhoneSessionEffect.ScheduleArmedAlarm(null))
        add(PhoneSessionEffect.ReleaseEnforcement(kind))
        if (reason != null) add(PhoneSessionEffect.SaveReason(reason))
    }
}
