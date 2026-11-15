package com.accountabilibuddy.companion.session

import com.accountabilibuddy.shared.session.PhoneSessionCheckpoint
import com.accountabilibuddy.shared.session.PhoneSessionClock
import com.accountabilibuddy.shared.session.PhoneAwayKind
import com.accountabilibuddy.shared.session.PhoneSessionEffect
import com.accountabilibuddy.shared.session.PhoneSessionEngine
import com.accountabilibuddy.shared.session.PhoneSessionNotice
import com.accountabilibuddy.shared.session.PhoneSessionOffGap
import com.accountabilibuddy.shared.session.PhoneSessionRead
import com.accountabilibuddy.shared.session.PhoneSessionReason
import com.accountabilibuddy.shared.session.PhoneSessionRecord
import com.accountabilibuddy.shared.session.PhoneSessionRefusal
import com.accountabilibuddy.shared.session.PhoneSessionResult
import com.accountabilibuddy.shared.session.PhoneSessionStoreData
import com.accountabilibuddy.shared.sync.SessionTaskItem
import java.time.Duration
import java.time.Instant
import java.time.ZoneId

/**
 * The only writer of a phone session (ADR 85, PS2a): it applies the pure [PhoneSessionEngine] to the saved record on the session's own
 * trusted clock ([PhoneSessionClock], R12), keeps the result in the sealed store, and carries out what the engine asked for (alarms, the
 * sync file, the enforcement, a kept reason) through [PhoneSessionPorts]. No session rule lives here: this class decides only WHEN to ask the
 * engine (the alarm, a refresh, an app start, a lunch, the phone going off) and WHAT it may be told (never an untrusted time as a heartbeat).
 *
 * Every public method is synchronized, and every one first makes sure the app-start path has run for this boot ([ensureStarted]), so a refresh that
 * wins the race with [onStart] can't skip the off-gap check (S2). The ports' hooks run under that lock: an implementation must not block on this
 * controller from another thread (N4).
 */
class PhoneSessionController(private val ports: PhoneSessionPorts) {
    private var data: PhoneSessionStoreData
    val clock: PhoneSessionClock
    private var started = false
    private var heartbeatAlarmOn = false

    /**
     * The log could not be opened and has not been started again with a trusted history start (B5, N2): PS2b writes no `running` and
     * `coversFrom = writtenAt` until it is false.
     */
    @get:Synchronized
    var logUnreadable: Boolean = false
        private set

    private val lastAlarm = mutableMapOf<PhoneAlarmKind, Instant?>()

    init {
        when (val read = ports.store.read()) {
            PhoneSessionRead.Missing -> {
                data = PhoneSessionStoreData()
                clock = PhoneSessionClock(ports.clockSource)
            }
            is PhoneSessionRead.Readable -> {
                data = read.data
                clock = PhoneSessionClock(ports.clockSource, read.data.checkpoint?.anchor)
            }
            PhoneSessionRead.Unreadable -> {
                // Not empty (B5): a copy is kept aside under its own name, and a log that starts again says from when, so the sync file never claims
                // a history it lost. The marker is a time, so it is stored only from a trusted clock (N2); until then the log stays "unreadable".
                clock = PhoneSessionClock(ports.clockSource)
                val fresh = PhoneSessionStoreData(historyStartsAt = clock.now().takeIf { clock.isTrusted })
                data = fresh
                logUnreadable = !(ports.store.setAsideUnreadable() && ports.store.write(fresh) && fresh.historyStartsAt != null)
            }
        }
    }

    // --- What the app reads -----------------------------------------------------------------------------------------------------

    /** The running session (a test one too); null when none. */
    @get:Synchronized
    val running: PhoneSessionRecord? get() = data.checkpoint?.record

    /** Ended sessions of the last 90 days, never a test one, oldest first (PS2b writes the sync file's pieces from them). */
    @get:Synchronized
    val log: List<PhoneSessionRecord> get() = data.log

    @get:Synchronized
    val notice: PhoneSessionNotice? get() = data.notice

    /** The time the phone's history starts from when its log was lost (B5), else null. */
    @get:Synchronized
    val historyStartsAt: Instant? get() = data.historyStartsAt

    /** The session time now (R12). */
    fun now(): Instant = clock.now()

    // --- Start ------------------------------------------------------------------------------------------------------------------

    /**
     * Starts a phone session of [lengthMinutes] (null: no set end) on the profile [profileId]/[profileName]. [tasks] is This session's list
     * (GUID ids) at the start. A new anchor is taken at the start (R12). R1: refused while another device of the group is live (a test session
     * is never blocked, and is never counted or synced). It is saved before anything else happens: a session that could not be saved is not started.
     * A start on an anchor that is not network-backed is marked, so the network time arriving moves every time of the record (B2).
     */
    @Synchronized
    fun start(profileId: String, profileName: String, lengthMinutes: Int?, tasks: List<SessionTaskItem> = emptyList(), isTest: Boolean = false): PhoneSessionStart {
        ensureStarted()
        if (running != null) return PhoneSessionStart.AlreadyRunning
        clock.reanchor()
        val now = clock.now()
        if (!isTest && ports.liveElsewhere(now)) return PhoneSessionStart.LiveElsewhere
        val done = when (val result = PhoneSessionEngine.start(ports.newId(), profileId, profileName, lengthMinutes, now, tasks, isTest)) {
            is PhoneSessionResult.Refused -> return PhoneSessionStart.Refused(result.reason)
            is PhoneSessionResult.Done -> result
        }
        val staged = data.copy(
            checkpoint = PhoneSessionCheckpoint(done.record, clock.anchor, ports.zone().id, startUntrusted = !clock.isTrusted),
            notice = null,
        )
        if (!ports.store.write(staged)) return PhoneSessionStart.StoreFailed
        data = staged
        dispatch(done.record, done.effects, now)
        // A lunch already running at the start pauses it at once (the session starts inside it).
        observeLunchRaw()
        ports.changed()
        return PhoneSessionStart.Started(running ?: done.record)
    }

    // --- The user's actions (each gives the engine's answer, so the UI can say why it was refused) ------------------------------

    @Synchronized fun stepAway(): PhoneSessionResult = transition { r, now -> PhoneSessionEngine.stepAway(r, now) }

    @Synchronized fun back(): PhoneSessionResult = transition { r, now -> PhoneSessionEngine.back(r, now) }

    @Synchronized fun pause(): PhoneSessionResult = transition { r, now -> PhoneSessionEngine.pause(r, now) }

    /** Resume after Pause timer; a lunch running at that moment keeps its pause (Q1165). */
    @Synchronized
    fun resume(): PhoneSessionResult = transition { r, now -> PhoneSessionEngine.resume(r, now, ports.lunchRunning()) }

    @Synchronized fun add10(): PhoneSessionResult = transition { r, now -> PhoneSessionEngine.add10(r, now) }

    /** The user opened End: the AccountabiliBuddy wait counts from here (R8). */
    @Synchronized fun openEnd(): PhoneSessionResult = transition { r, now -> PhoneSessionEngine.openEnd(r, now) }

    /** End early, by the mode read now (S3): AccountabiliBuddy arms or ends after the wait and a reason; ProductaBuddy ends at once. */
    @Synchronized
    fun endEarly(reason: String?): PhoneSessionResult =
        transition { r, now -> PhoneSessionEngine.endEarly(r, now, ports.mode(), ports.endSettings(), reason) }

    @Synchronized fun cancelEnd(): PhoneSessionResult = transition { r, _ -> PhoneSessionEngine.cancelEnd(r) }

    /** "All tasks done" (R9). */
    @Synchronized fun endAtOnce(): PhoneSessionResult = transition { r, now -> PhoneSessionEngine.endAtOnce(r, now) }

    /** A session that ends on command (no set end, a test). */
    @Synchronized fun endOnCommand(): PhoneSessionResult = transition { r, now -> PhoneSessionEngine.endOnCommand(r, now) }

    /** This session's list changed (a tick, an add, a removal, a park, a received edit): an open task that left counts as dropped (B6). */
    @Synchronized
    fun thisSessionChanged(tasks: List<SessionTaskItem>): PhoneSessionResult = transition { r, _ -> PhoneSessionEngine.checklistChanged(r, tasks) }

    /** Undo of a removed or parked task: its drop is given back only if it was dropped open (V-N1). */
    @Synchronized
    fun thisSessionUndone(tasks: List<SessionTaskItem>): PhoneSessionResult = transition { r, _ -> PhoneSessionEngine.checklistUndo(r, tasks) }

    /** The notice about the phone having been off was seen. */
    @Synchronized
    fun dismissNotice() {
        ensureStarted()
        if (data.notice == null) return
        data = data.copy(notice = null)
        persist()
        ports.changed()
    }

    // --- The clock's moments ----------------------------------------------------------------------------------------------------

    /** The end alarm or the armed-end alarm: ends the session when the engine says so. */
    @Synchronized fun tick(): PhoneSessionResult = transition { r, now -> PhoneSessionEngine.tick(r, now) }

    /**
     * Every refresh (the pinned notification's loop, the 5-minute alarm, the waking heartbeat alarm, an unlock): the network time arriving re-anchors an
     * unchecked clock and corrects what was written on it (B2) and decides a waiting hold; a lunch is looked at; the end is checked; and the heartbeat is
     * saved (at most once a minute), only on a clock whose time may be a heartbeat (R13, S9).
     */
    @Synchronized
    fun refresh() {
        ensureStarted()
        // The network time arriving anchors the clock again (and, with a session running, corrects what was written on the old anchor, B2).
        if (running == null) clock.upgradeIfTrusted() else upgradeAndCorrect()
        markHistoryWhenTrusted()
        val record = running ?: return
        if (running?.offAt != null) settleOffGap(rebooted = false)
        observeLunchRaw()
        transitionRaw { r, now -> PhoneSessionEngine.tick(r, now) }
        val current = running ?: return
        if (Duration.between(current.heartbeatAt, clock.now()) >= HEARTBEAT_EVERY_SAVE && canBeat()) {
            transitionRaw { r, now -> PhoneSessionEngine.heartbeat(r, now, true) }
        }
        // The waking heartbeat alarm is set again (inexact, about every 15 minutes: the phone asleep still beats, and a killed process starts again, S7).
        if (running != null && record.isRunning) {
            heartbeatAlarmOn = true
            ports.scheduleAlarm(PhoneAlarmKind.HEARTBEAT, HEARTBEAT_ALARM_EVERY.toMillis())
        }
    }

    /**
     * The app starts (the process is created: a boot, an update, or Android killing it). A new boot re-anchors the clock on the trusted time and
     * judges the gap with the phone off (R13, B3); the same boot, with no `offAt`, is no gap at all (B3a: an app restart is not a reboot). The
     * alarms (which a reboot drops) and the enforcement are asked again.
     */
    @Synchronized
    fun onStart() {
        startPath()
    }

    /** `ACTION_SHUTDOWN`: the phone is going off. Saved only from a clock whose time may be a heartbeat; otherwise the last heartbeat stands (never an untrusted time, V-B1). */
    @Synchronized
    fun onShutdown() {
        ensureStarted()
        if (running == null || !canBeat()) return
        transitionRaw { r, now -> PhoneSessionEngine.phoneOff(r, now, true) }
    }

    /**
     * A lunch started or ended on some device, or a refresh looked: the countdown pauses from the lunch's RECORDED start (clamped by the engine, so
     * never before the session or the last stretch, never after now) and the stretch is kept as it was seen: a later Discard never shrinks it
     * (B2). When no lunch runs any more the stretch ends now, the time it was first seen not running.
     */
    @Synchronized
    fun observeLunch() {
        ensureStarted()
        observeLunchRaw()
    }

    // --- Inside -----------------------------------------------------------------------------------------------------------------

    // The start path runs once per process and again whenever the phone has been rebooted since the anchor (S2): whichever entry comes first does it.
    private fun ensureStarted() {
        if (!started || clock.rebooted()) startPath()
    }

    private fun startPath() {
        started = true
        markHistoryWhenTrusted()
        val record = running ?: return
        val rebooted = clock.rebooted()
        var storedTrusted = true
        if (rebooted) {
            // Times written on an unchecked anchor of the boot before can't be corrected across a reboot (the off time in between is unknown): the gap decides Pause-only.
            storedTrusted = data.checkpoint?.startUntrusted != true
            val endWrittenUnchecked = data.checkpoint?.endUntrusted == true
            clock.reanchor()
            // An End in progress is judged against the clock it was written on (V-B1): onto an unchecked clock, or written on one, it starts again: the
            // user confirms again and the wait is served on this clock. (An End on a checked clock that reboots onto a checked clock keeps its wait.)
            if (!clock.isTrusted || endWrittenUnchecked) dropEnd()
        }
        if (rebooted || record.offAt != null) settleOffGap(rebooted, storedTrusted)
        running?.let { current ->
            syncAlarms(current, force = true)
            if (current.isRunning) {
                ports.enforceUntil(current, current.effectiveEnd(clock.now()))
                heartbeatAlarmOn = true
                ports.scheduleAlarm(PhoneAlarmKind.HEARTBEAT, HEARTBEAT_ALARM_EVERY.toMillis())
            }
        }
        observeLunchRaw()
        transitionRaw { r, now -> PhoneSessionEngine.tick(r, now) }
    }

    // A clock whose time may be saved as a heartbeat or as the moment the phone went off: network-backed, or on a phone that can never have network time (S9:
    // there the session's own elapsed-anchored clock is all there is, and nothing there ever closes on an unchecked time).
    private fun canBeat(): Boolean = clock.isTrusted || !clock.trustCanCome

    private fun zone(): ZoneId = data.checkpoint?.zoneId?.let { runCatching { ZoneId.of(it) }.getOrNull() } ?: ports.zone()

    private fun markHistoryWhenTrusted() {
        if (!logUnreadable || data.historyStartsAt != null || !clock.isTrusted) return
        data = data.copy(historyStartsAt = clock.now())
        persist()
        logUnreadable = false
    }

    /**
     * The network time arrived on an anchor that was not network-backed: the clock is anchored again and the times written on the old one are moved by the
     * correction (B2): the whole record when the session started on it, otherwise only an End in progress (a hold forbids every other write). A session
     * started on a clock set back 2 hours is then still 2 hours long; an End confirmed during a hold still serves its full wait on trusted time.
     */
    private fun upgradeAndCorrect() {
        val correction = clock.upgradeIfTrusted() ?: return
        val checkpoint = data.checkpoint ?: return
        val record = checkpoint.record
        val moved = when {
            checkpoint.startUntrusted -> record.shifted(correction)
            checkpoint.endUntrusted -> record.endShifted(correction)
            else -> record
        }
        data = data.copy(checkpoint = checkpoint.copy(record = moved, anchor = clock.anchor, startUntrusted = false, endUntrusted = false))
        persist()
        syncAlarms(moved, force = true)
        if (moved != record) ports.changed()
    }

    private fun settleOffGap(rebooted: Boolean, storedTrusted: Boolean = true) {
        val record = running ?: return
        val now = clock.now()
        val trusted = clock.isTrusted && storedTrusted
        // Trust can still come only while the clock itself is unchecked; when the clock is checked but the stored times are not, nothing is better to wait for.
        val canCome = clock.trustCanCome && !clock.isTrusted
        val decision = PhoneSessionOffGap.decide(record, now, rebooted || record.offAt != null, trusted, canCome, zone())
        val result = PhoneSessionEngine.applyOffGap(record, decision, now, canBeat())
        apply(record, result, now)
        if (clock.isTrusted) data.checkpoint?.takeIf { it.startUntrusted }?.let { data = data.copy(checkpoint = it.copy(startUntrusted = false)); persist() }
    }

    private fun dropEnd() {
        val current = running ?: return
        if (current.endRequestedAt == null && current.armedEnd == null) {
            data.checkpoint?.takeIf { it.endUntrusted }?.let { data = data.copy(checkpoint = it.copy(endUntrusted = false)); persist() }
            return
        }
        transitionRaw { r, _ -> PhoneSessionEngine.cancelEnd(r) }
    }

    // Where the 90-day log and the reasons may be pruned (V-S1): on a trusted clock by the time now; below Android 13 (an unchecked clock is all there is) by
    // the time now but never past the newest end already kept, so one session ending on a clock set far ahead can't sweep them; on 13+ with an unchecked clock not at all.
    private fun mayPrune(): Boolean = clock.isTrusted || !clock.trustCanCome

    private fun capPrune(): Boolean = !clock.isTrusted

    private fun observeLunchRaw() {
        val record = running ?: return
        val lunchRunning = ports.lunchRunning()
        val open = record.openAway
        if (lunchRunning && open?.kind != PhoneAwayKind.LUNCH) {
            transitionRaw { r, now -> PhoneSessionEngine.lunchHold(r, minOf(ports.lunchStart() ?: now, now), now) }
        } else if (!lunchRunning && open?.kind == PhoneAwayKind.LUNCH) {
            transitionRaw { r, now -> PhoneSessionEngine.lunchEnd(r, now) }
        }
    }

    private fun transition(change: (PhoneSessionRecord, Instant) -> PhoneSessionResult): PhoneSessionResult {
        ensureStarted()
        return transitionRaw(change)
    }

    private fun transitionRaw(change: (PhoneSessionRecord, Instant) -> PhoneSessionResult): PhoneSessionResult {
        val record = running ?: return PhoneSessionResult.Refused(PhoneSessionRefusal.NOT_RUNNING)
        val now = clock.now()
        val result = change(record, now)
        apply(record, result, now)
        return result
    }

    private fun apply(before: PhoneSessionRecord, result: PhoneSessionResult, now: Instant) {
        val done = result as? PhoneSessionResult.Done ?: return
        if (done.record == before && done.effects.isEmpty()) return
        var next = done.record
        // The heartbeat at every state change (R13), and only a time that may be one becomes one.
        if (next.isRunning && canBeat()) {
            (PhoneSessionEngine.heartbeat(next, now, true) as? PhoneSessionResult.Done)?.let { next = it.record }
        }

        val notice = done.effects.filterIsInstance<PhoneSessionEffect.OffGapNotice>().lastOrNull()
            ?.let { PhoneSessionNotice(next.id, it.from, it.to, it.closed, untrusted = !clock.isTrusted) }
        val previous = data.checkpoint
        data = when {
            next.isRunning -> {
                // An End written on an unchecked clock is marked: the network time arriving moves it (B2).
                val endChanged = next.endRequestedAt != before.endRequestedAt || next.armedEnd != before.armedEnd
                val endUntrusted = when {
                    next.endRequestedAt == null && next.armedEnd == null -> false
                    endChanged && !clock.isTrusted -> true
                    else -> previous?.endUntrusted == true
                }
                data.copy(
                    checkpoint = PhoneSessionCheckpoint(next, clock.anchor, previous?.zoneId, previous?.startUntrusted == true, endUntrusted),
                    notice = notice ?: data.notice,
                )
            }
            next.isTest -> data.copy(checkpoint = null, notice = notice ?: data.notice)
            // The 90-day log is pruned only on a trusted clock (S6).
            else -> data.withEnded(next, now, prune = mayPrune(), capAtNewest = capPrune()).copy(notice = notice ?: data.notice)
        }
        persist()
        // Saved first: what the effects ask (the sync file, the enforcement) reads the saved session.
        dispatch(next, done.effects, now)
        // A heartbeat alone (or a new anchor) is saved but tells nobody: the refresh that saves it would otherwise be asked to run again.
        if (next.copy(heartbeatAt = before.heartbeatAt) != before || notice != null) ports.changed()
    }

    private fun persist() {
        ports.store.write(data)
    }

    private fun dispatch(record: PhoneSessionRecord, effects: List<PhoneSessionEffect>, now: Instant) {
        syncAlarms(record, force = false)
        for (effect in effects) {
            when (effect) {
                PhoneSessionEffect.Persist, is PhoneSessionEffect.ScheduleEndAlarm, is PhoneSessionEffect.ScheduleArmedAlarm, is PhoneSessionEffect.OffGapNotice -> Unit
                PhoneSessionEffect.WriteSyncFile -> ports.requestSyncWrite()
                is PhoneSessionEffect.EnforceUntil -> ports.enforceUntil(record, effect.endsAt)
                is PhoneSessionEffect.ReleaseEnforcement -> ports.releaseEnforcement(record, effect.kind)
                is PhoneSessionEffect.SaveReason -> ports.store.addReason(PhoneSessionReason(record.id, now, effect.reason), now, prune = mayPrune(), capAtNewest = capPrune())
            }
        }
    }

    // The alarms follow the record, not the engine's effects: a paused countdown has no end alarm (it is set again on Resume), and an armed
    // end has its own. [force] sets them again whatever was set before (an app start: a reboot dropped them). The waking heartbeat alarm runs only
    // while a session does.
    private fun syncAlarms(record: PhoneSessionRecord, force: Boolean) {
        val now = clock.now()
        val end = record.takeIf { it.isRunning && !it.isTimerPaused }?.phaseEndsAt
        val armed = record.takeIf { it.isRunning }?.armedEnd?.readyAt
        set(PhoneAlarmKind.END, end, now, force)
        set(PhoneAlarmKind.ARMED, armed, now, force)
        if (record.isRunning && !heartbeatAlarmOn) {
            heartbeatAlarmOn = true
            ports.scheduleAlarm(PhoneAlarmKind.HEARTBEAT, HEARTBEAT_ALARM_EVERY.toMillis())
        } else if (!record.isRunning && heartbeatAlarmOn) {
            heartbeatAlarmOn = false
            ports.scheduleAlarm(PhoneAlarmKind.HEARTBEAT, null)
        }
    }

    private fun set(kind: PhoneAlarmKind, at: Instant?, now: Instant, force: Boolean) {
        if (!force && lastAlarm.containsKey(kind) && lastAlarm[kind] == at) return
        lastAlarm[kind] = at
        ports.scheduleAlarm(kind, at?.let { maxOf(0L, Duration.between(now, it).toMillis()) })
    }

    private companion object {
        /** The heartbeat is saved at most this often by a refresh (N3: each save seals the whole 90-day log again). */
        val HEARTBEAT_EVERY_SAVE: Duration = Duration.ofSeconds(60)

        /** The waking heartbeat alarm's period (S7). */
        val HEARTBEAT_ALARM_EVERY: Duration = Duration.ofMinutes(15)
    }
}
