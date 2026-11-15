package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.session.PhoneAway
import com.accountabilibuddy.shared.session.PhoneAwayKind
import com.accountabilibuddy.shared.session.PhoneEndSettings
import com.accountabilibuddy.shared.session.PhoneLockArea
import com.accountabilibuddy.shared.session.PhoneSessionChecklist
import com.accountabilibuddy.shared.session.PhoneSessionEffect
import com.accountabilibuddy.shared.session.PhoneSessionEndKind
import com.accountabilibuddy.shared.session.PhoneSessionEndRules
import com.accountabilibuddy.shared.session.PhoneSessionEngine
import com.accountabilibuddy.shared.session.PhoneSessionLocks
import com.accountabilibuddy.shared.session.PhoneSessionMode
import com.accountabilibuddy.shared.session.PhoneSessionOffGap
import com.accountabilibuddy.shared.session.PhoneSessionProfileUse
import com.accountabilibuddy.shared.session.PhoneSessionRecord
import com.accountabilibuddy.shared.session.PhoneSessionRefusal
import com.accountabilibuddy.shared.session.PhoneSessionResult
import com.accountabilibuddy.shared.sync.GroupSessions
import com.accountabilibuddy.shared.sync.ReceivedSyncFile
import com.accountabilibuddy.shared.sync.SessionPieces
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.sync.SyncSession
import java.io.File
import java.time.Duration
import java.time.Instant
import java.time.ZoneOffset
import java.util.UUID

/**
 * PS1b (phone only; the rules shared with the PC are the `ps1` fixtures): every transition of the phone session engine, the refusals, R8's
 * terms by mode, R9 with B6, R10's limits, R13's thresholds (1:59, 2:00, 3:59 h, 4 h, midnight, same boot, untrusted), R11's scope by mode,
 * what the phone's file carries (no private text), and the source scans for trusted time. Each line names the rule it pins.
 */
object Ps1SelfCheck {
    private val zone = ZoneOffset.ofHours(-5)
    private val t0: Instant = Instant.parse("2026-11-16T17:00:00Z") // 12:00 local
    private fun at(minutes: Long, seconds: Long = 0): Instant = t0.plusSeconds(minutes * 60 + seconds)
    private val AB = PhoneSessionMode.ACCOUNTABILI_BUDDY
    private val PB = PhoneSessionMode.PRODUCTA_BUDDY
    private val defaults = PhoneEndSettings.DEFAULT
    private const val GUID_A = "aaaaaaaa-0000-0000-0000-000000000001"
    private const val GUID_B = "aaaaaaaa-0000-0000-0000-000000000002"

    private fun task(id: String, doneAt: Instant? = null) = SessionTaskItem(id, "Task $id", doneAt, emptyList())

    private fun started(minutes: Int? = 50, tasks: List<SessionTaskItem> = emptyList(), test: Boolean = false): PhoneSessionRecord =
        (PhoneSessionEngine.start("s1", "p1", "Deep work", minutes, t0, tasks, test) as PhoneSessionResult.Done).record

    private fun done(result: PhoneSessionResult): PhoneSessionResult.Done = result as? PhoneSessionResult.Done ?: error("refused: $result")
    private fun refusal(result: PhoneSessionResult): PhoneSessionRefusal? = (result as? PhoneSessionResult.Refused)?.reason

    fun run(report: CheckReport) {
        starting(report)
        stepAway(report)
        pausing(report)
        lunch(report)
        addTen(report)
        timeUp(report)
        endingEarly(report)
        allTasksDone(report)
        offGap(report)
        locks(report)
        theFile(report)
        clamp(report)
        reviewFixes(report)
        sources(report)
    }

    private fun starting(report: CheckReport) {
        val timed = PhoneSessionEngine.start("s1", "p1", "Deep work", 50, t0)
        val record = done(timed).record
        report.check("ps1/start: a 50 minute session ends 50 minutes after it starts, running, no tasks", record.phaseEndsAt == at(50) && record.isRunning && !record.checklist.madeBeforeStart)
        report.check("ps1/start: it asks the alarm and the enforcement for the end, writes the sync file and saves", done(timed).effects.containsAll(listOf(
            PhoneSessionEffect.ScheduleEndAlarm(at(50)), PhoneSessionEffect.EnforceUntil(at(50)), PhoneSessionEffect.WriteSyncFile, PhoneSessionEffect.Persist)))
        report.check("ps1/start: no set end has no end and no alarm time", done(PhoneSessionEngine.start("s1", "p1", "Deep work", null, t0)).record.phaseEndsAt == null)
        report.check("ps1/start: 0 and 721 minutes are refused", refusal(PhoneSessionEngine.start("s", "p", "n", 0, t0)) == PhoneSessionRefusal.BAD_LENGTH && refusal(PhoneSessionEngine.start("s", "p", "n", 721, t0)) == PhoneSessionRefusal.BAD_LENGTH)
        report.check("ps1/start: 720 minutes (12 h) is allowed", PhoneSessionEngine.start("s", "p", "n", 720, t0) is PhoneSessionResult.Done)
        val withTasks = started(tasks = listOf(task(GUID_A)))
        report.check("ps1/start: a checklist with tasks at the start is 'made before start' (R9)", withTasks.checklist.madeBeforeStart && withTasks.checklist.sessionStart == t0)
        val test = started(test = true)
        report.check("ps1/start: a test session never writes the sync file", PhoneSessionEffect.WriteSyncFile !in done(PhoneSessionEngine.start("s", "p", "n", 25, t0, isTest = true)).effects && test.isTest)
    }

    private fun stepAway(report: CheckReport) {
        val away = done(PhoneSessionEngine.stepAway(started(), at(10))).record
        report.check("ps1/step-away: opens a step-away stretch; the end stays put", away.openAway?.kind == PhoneAwayKind.STEP_AWAY && away.phaseEndsAt == at(50) && !away.isTimerPaused)
        report.check("ps1/step-away: a second step away or a pause while away is refused", refusal(PhoneSessionEngine.stepAway(away, at(11))) == PhoneSessionRefusal.ALREADY_AWAY && refusal(PhoneSessionEngine.pause(away, at(11))) == PhoneSessionRefusal.ALREADY_AWAY)
        val back = done(PhoneSessionEngine.back(away, at(20))).record
        report.check("ps1/step-away: I'm back moves nothing (start, end), the stretch is closed", back.phaseEndsAt == at(50) && back.phaseStartedAt == t0 && back.away == listOf(PhoneAway(PhoneAwayKind.STEP_AWAY, at(10), at(20))))
        report.check("ps1/step-away: I'm back with no stretch, or from a pause, is refused", refusal(PhoneSessionEngine.back(started(), at(1))) == PhoneSessionRefusal.NOT_AWAY &&
            refusal(PhoneSessionEngine.back(done(PhoneSessionEngine.pause(started(), at(1))).record, at(2))) == PhoneSessionRefusal.NOT_STEPPED_AWAY)
        val over = done(PhoneSessionEngine.tick(away, at(50))).record
        report.check("ps1/step-away: the timer keeps running: the set end still ends it, the stretch closed at the end", over.endKind == PhoneSessionEndKind.TIME_UP && over.endedAt == at(50) && over.away.single().to == at(50))
    }

    private fun pausing(report: CheckReport) {
        val paused = done(PhoneSessionEngine.pause(started(), at(10))).record
        report.check("ps1/pause: the countdown freezes: the end shown is the old end plus the time paused so far", paused.isTimerPaused && paused.effectiveEnd(at(25)) == at(65))
        report.check("ps1/pause: a paused session never ends by the clock", done(PhoneSessionEngine.tick(paused, at(600))).record.isRunning)
        val resumed = done(PhoneSessionEngine.resume(paused, at(25)))
        report.check("ps1/resume: start and end move later by the pause (15 minutes)", resumed.record.phaseEndsAt == at(65) && resumed.record.phaseStartedAt == at(15))
        report.check("ps1/resume: the alarm and the enforcement are asked for the new end", resumed.effects.containsAll(listOf(PhoneSessionEffect.ScheduleEndAlarm(at(65)), PhoneSessionEffect.EnforceUntil(at(65)))))
        report.check("ps1/resume: resume with no pause, or from a step away, is refused", refusal(PhoneSessionEngine.resume(started(), at(1))) == PhoneSessionRefusal.NOT_PAUSED &&
            refusal(PhoneSessionEngine.resume(done(PhoneSessionEngine.stepAway(started(), at(1))).record, at(2))) == PhoneSessionRefusal.NOT_PAUSED)
        report.check("ps1/pause: a pause with no set end is allowed and moves nothing", done(PhoneSessionEngine.resume(done(PhoneSessionEngine.pause(started(null), at(5))).record, at(9))).record.phaseEndsAt == null)
        report.check("ps1/pause: after the end passed it is refused (the session is over)", refusal(PhoneSessionEngine.pause(started(), at(50))) == PhoneSessionRefusal.TIME_IS_UP)
    }

    private fun lunch(report: CheckReport) {
        // B2: the pause is what was observed. Lunch 12:00-12:40 then a Discard of the lunch record still leaves 40 minutes away and the end 40 minutes on.
        val held = done(PhoneSessionEngine.lunchHold(started(), at(0), at(0))).record
        val ended = done(PhoneSessionEngine.lunchEnd(held, at(40))).record
        report.check("ps1/lunch (B2): 40 minutes of lunch is a 40 minute LUNCH stretch and the end moves 40 minutes", ended.away == listOf(PhoneAway(PhoneAwayKind.LUNCH, at(0), at(40))) && ended.phaseEndsAt == at(90))
        val again = done(PhoneSessionEngine.lunchEnd(ended, at(41)))
        report.check("ps1/lunch (B2): the lunch being seen gone again (a Discard) changes nothing: the closed stretch and the end stay", again.record == ended && again.effects.isEmpty())
        val rehold = done(PhoneSessionEngine.lunchHold(ended, at(40), at(41))).record
        report.check("ps1/lunch (B2): a later zero-length lunch record opens a stretch only from the end of the closed one, never shrinking it", rehold.away.first() == ended.away.single() && rehold.away.last().from >= at(40))
        report.check("ps1/lunch: the time before the session or ahead of now is clamped", done(PhoneSessionEngine.lunchHold(started(), at(-30), at(5))).record.away.single().from == t0 &&
            done(PhoneSessionEngine.lunchHold(started(), at(30), at(5))).record.away.single().from == at(5))
        val fromStep = done(PhoneSessionEngine.lunchHold(done(PhoneSessionEngine.stepAway(started(), at(3))).record, at(10), at(10))).record
        report.check("ps1/lunch: a step away in progress becomes its own lunch stretch (the step away closed where it begins)", fromStep.away == listOf(PhoneAway(PhoneAwayKind.STEP_AWAY, at(3), at(10)), PhoneAway(PhoneAwayKind.LUNCH, at(10), null)))
        val whilePaused = done(PhoneSessionEngine.pause(started(), at(3))).record
        report.check("ps1/lunch: a Pause timer already freezes the countdown: lunch adds no stretch and its end resumes nothing", done(PhoneSessionEngine.lunchHold(whilePaused, at(10), at(10))).record == whilePaused && done(PhoneSessionEngine.lunchEnd(whilePaused, at(20))).record == whilePaused)
        report.check("ps1/lunch: lunch pauses the end like Pause timer: effective end moves while it runs", held.effectiveEnd(at(20)) == at(70))
    }

    private fun addTen(report: CheckReport) {
        val added = done(PhoneSessionEngine.add10(started(), at(10)))
        report.check("ps1/add10 (R10): +10 minutes moves the end, counts the extension, re-asks the alarm and the enforcement", added.record.phaseEndsAt == at(60) && added.record.extensions == 1 &&
            added.effects.containsAll(listOf(PhoneSessionEffect.ScheduleEndAlarm(at(60)), PhoneSessionEffect.EnforceUntil(at(60)))))
        report.check("ps1/add10 (R10): refused while paused", refusal(PhoneSessionEngine.add10(done(PhoneSessionEngine.pause(started(), at(5))).record, at(6))) == PhoneSessionRefusal.ALREADY_AWAY)
        report.check("ps1/add10 (R10): refused while stepped away", refusal(PhoneSessionEngine.add10(done(PhoneSessionEngine.stepAway(started(), at(5))).record, at(6))) == PhoneSessionRefusal.ALREADY_AWAY)
        report.check("ps1/add10 (R10): refused with no set end", refusal(PhoneSessionEngine.add10(started(null), at(5))) == PhoneSessionRefusal.NO_SET_END)
        report.check("ps1/add10 (R10): refused in a test session", refusal(PhoneSessionEngine.add10(started(test = true), at(5))) == PhoneSessionRefusal.IS_TEST)
        report.check("ps1/add10 (R10): refused once the end has passed", refusal(PhoneSessionEngine.add10(started(), at(50))) == PhoneSessionRefusal.TIME_IS_UP)
        report.check("ps1/add10 (R10): 710 + 10 = 12 h is allowed, 715 + 10 is refused as too long", PhoneSessionEngine.add10(started(710), at(5)) is PhoneSessionResult.Done &&
            refusal(PhoneSessionEngine.add10(started(715), at(5))) == PhoneSessionRefusal.TOO_LONG)
        report.check("ps1/add10 (R10): after a pause the length counts, not the start (the 12 h limit survives a resume)",
            refusal(PhoneSessionEngine.add10(done(PhoneSessionEngine.resume(done(PhoneSessionEngine.pause(started(715), at(5))).record, at(125))).record, at(130))) == PhoneSessionRefusal.TOO_LONG)
    }

    private fun timeUp(report: CheckReport) {
        val ended = done(PhoneSessionEngine.tick(started(), at(55)))
        report.check("ps1/tick: a tick after the end ends it at the END moment (not at the late tick) as TIME_UP", ended.record.endedAt == at(50) && ended.record.endKind == PhoneSessionEndKind.TIME_UP)
        report.check("ps1/tick: it releases what the session asked for and cancels both alarms", ended.effects.containsAll(listOf(
            PhoneSessionEffect.ReleaseEnforcement(PhoneSessionEndKind.TIME_UP), PhoneSessionEffect.ScheduleEndAlarm(null), PhoneSessionEffect.ScheduleArmedAlarm(null))))
        report.check("ps1/tick: one second before the end nothing happens", done(PhoneSessionEngine.tick(started(), at(49, 59))).record.isRunning)
        report.check("ps1/tick: a session with no set end never times out", done(PhoneSessionEngine.tick(started(null), at(60 * 100))).record.isRunning)
        // B1: running is the store's word. A clock 2 hours past the set end, with no tick applied, changes nothing about the record.
        report.check("ps1/B1: 'running' never reads a clock: the record is running until a tick on a trusted time ends it", started().copy(phaseEndsAt = at(-600)).isRunning)
        report.check("ps1/tick: an ended session is not touched by any transition (history)", done(PhoneSessionEngine.tick(ended.record, at(500))).record == ended.record && refusal(PhoneSessionEngine.stepAway(ended.record, at(60))) == PhoneSessionRefusal.NOT_RUNNING)
    }

    private fun endingEarly(report: CheckReport) {
        val reason = "a real reason, long enough"
        report.check("ps1/terms (R8): AccountabiliBuddy timed = 60 s and 15 characters", PhoneSessionEndRules.terms(AB, started(), defaults).let { it.wait == Duration.ofSeconds(60) && it.minReasonLength == 15 })
        report.check("ps1/terms (R8): ProductaBuddy takes none, a no-set-end session takes none, a test takes none",
            PhoneSessionEndRules.terms(PB, started(), defaults).isNone && PhoneSessionEndRules.terms(AB, started(null), defaults).isNone && PhoneSessionEndRules.terms(AB, started(test = true), defaults).isNone)

        val shortReason = PhoneSessionEngine.endEarly(started(), at(5), AB, defaults, "too short")
        report.check("ps1/end (R8): a reason under 15 characters is refused", refusal(shortReason) == PhoneSessionRefusal.REASON_TOO_SHORT)
        val armed = done(PhoneSessionEngine.endEarly(started(), at(5), AB, defaults, reason))
        report.check("ps1/end (R8): AccountabiliBuddy before the wait ends nothing: the session runs and the end is armed for the wait's end", armed.record.isRunning && armed.record.armedEnd?.readyAt == at(6) &&
            PhoneSessionEffect.ScheduleArmedAlarm(at(6)) in armed.effects)
        val opened = done(PhoneSessionEngine.openEnd(started(), at(5))).record
        report.check("ps1/end (R8): the wait counts from opening: confirming 30 s after opening arms for 60 s after opening", done(PhoneSessionEngine.endEarly(opened, at(5, 30), AB, defaults, reason)).record.armedEnd?.readyAt == at(6))
        report.check("ps1/end (R8): confirming at the wait's end ends it as EARLY_END and keeps the reason", done(PhoneSessionEngine.endEarly(opened, at(6), AB, defaults, reason)).let {
            it.record.endKind == PhoneSessionEndKind.EARLY_END && it.record.endedAt == at(6) && PhoneSessionEffect.SaveReason(reason) in it.effects
        })
        report.check("ps1/end (R8): the armed end does not apply one second early", done(PhoneSessionEngine.tick(armed.record, at(5, 59))).record.isRunning && refusal(PhoneSessionEngine.applyArmed(armed.record, at(5, 59))) == PhoneSessionRefusal.NOT_ARMED)
        report.check("ps1/end (R8): the armed end applies at the wait's end (the alarm) and late when the phone slept, never banked as earlier", done(PhoneSessionEngine.tick(armed.record, at(6))).record.endKind == PhoneSessionEndKind.EARLY_END &&
            done(PhoneSessionEngine.applyArmed(armed.record, at(9))).record.endedAt == at(9))
        report.check("ps1/end (R8): Cancel is free: no armed end, no wait, the alarm is cancelled", done(PhoneSessionEngine.cancelEnd(armed.record)).let { it.record.armedEnd == null && it.record.endRequestedAt == null && PhoneSessionEffect.ScheduleArmedAlarm(null) in it.effects })
        report.check("ps1/end (R8): an armed end is dropped when the session ends otherwise (time up)", done(PhoneSessionEngine.tick(armed.record.copy(phaseEndsAt = at(5, 30)), at(5, 30))).record.let { it.armedEnd == null && it.endKind == PhoneSessionEndKind.TIME_UP })
        report.check("ps1/end (R8): ProductaBuddy ends at once, no reason, as ON_COMMAND", done(PhoneSessionEngine.endEarly(started(), at(5), PB, defaults, null)).record.let { it.endKind == PhoneSessionEndKind.ON_COMMAND && it.endedAt == at(5) })
        report.check("ps1/end (R8): a no-set-end session ends at once in AccountabiliBuddy too", done(PhoneSessionEngine.endEarly(started(null), at(5), AB, defaults, null)).record.endKind == PhoneSessionEndKind.ON_COMMAND)
        report.check("ps1/end (R8): a custom wait applies: 120 s", done(PhoneSessionEngine.endEarly(started(), at(5), AB, PhoneEndSettings(120, 5), "short")).record.armedEnd?.readyAt == at(7))
        report.check("ps1/end (R8): the reason never shows in a printed record or effect", !armed.record.toString().contains(reason) && !PhoneSessionEffect.SaveReason(reason).toString().contains(reason) && !armed.record.armedEnd.toString().contains(reason))
        report.check("ps1/end (R8): the reason is not in the sync file (the record's JSON has no place for it)", !SyncFileJson.write(SessionFile.of(armed.record, at(5))).toString().contains(reason))
        report.check("ps1/end (R8): End on command is refused for a timed session, allowed with no set end", refusal(PhoneSessionEngine.endOnCommand(started(), at(5))) == PhoneSessionRefusal.ENDS_ON_COMMAND_ONLY &&
            done(PhoneSessionEngine.endOnCommand(started(null), at(5))).record.endKind == PhoneSessionEndKind.ON_COMMAND && PhoneSessionEngine.endOnCommand(started(test = true), at(5)) is PhoneSessionResult.Done)
    }

    private fun allTasksDone(report: CheckReport) {
        val ticked = listOf(task(GUID_A, at(1)), task(GUID_B, at(2)))
        val record = started(tasks = listOf(task(GUID_A), task(GUID_B)))
        val allTicked = done(PhoneSessionEngine.checklistChanged(record, ticked)).record
        report.check("ps1/all-done (R9): all ticked after 10 minutes ends at once with the reason 'All tasks done'", done(PhoneSessionEngine.endAtOnce(allTicked, at(10))).let {
            it.record.endKind == PhoneSessionEndKind.ALL_TASKS_DONE && PhoneSessionEffect.SaveReason("All tasks done") in it.effects
        })
        report.check("ps1/all-done (R9): not before 10 minutes (9:59)", refusal(PhoneSessionEngine.endAtOnce(allTicked, at(9, 59))) == PhoneSessionRefusal.NOT_ALL_DONE)
        report.check("ps1/all-done (R9): a task still open refuses", refusal(PhoneSessionEngine.endAtOnce(done(PhoneSessionEngine.checklistChanged(record, listOf(task(GUID_A, at(1)), task(GUID_B)))).record, at(20))) == PhoneSessionRefusal.NOT_ALL_DONE)
        // B6: an open task moved out of This session counts as dropped, and the instant end is lost for good.
        val dropped = done(PhoneSessionEngine.checklistChanged(record, listOf(task(GUID_A, at(1))))).record
        report.check("ps1/all-done (B6): an open task taken out of This session counts as dropped; the rest ticked no longer ends at once", dropped.checklist.droppedOpen == 1 && refusal(PhoneSessionEngine.endAtOnce(dropped, at(20))) == PhoneSessionRefusal.NOT_ALL_DONE)
        report.check("ps1/all-done (B6): a done task taken out does not count as dropped",
            done(PhoneSessionEngine.checklistChanged(allTicked, listOf(task(GUID_A, at(1))))).record.checklist.droppedOpen == 0)
        report.check("ps1/all-done (R9): tasks added during the session only (none at the start) never end at once", refusal(PhoneSessionEngine.endAtOnce(done(PhoneSessionEngine.checklistChanged(started(), ticked)).record, at(30))) == PhoneSessionRefusal.NOT_ALL_DONE)
        report.check("ps1/all-done (B6): Undo gives back one drop (by the id kept), never below zero", dropped.checklist.withUndone(listOf(task(GUID_A, at(1)), task(GUID_B))).droppedOpen == 0 && PhoneSessionChecklist().withUndone(emptyList()).droppedOpen == 0)
        // The step tick rule (Q1154): ticking a task ticks its steps; ticking the last step ticks the task; unticking a step reopens the task.
        val stepped = PhoneSessionChecklist(listOf(SessionTaskItem(GUID_A, "T", null, listOf(task("cccccccc-0000-0000-0000-000000000001"), task("cccccccc-0000-0000-0000-000000000002")))))
        report.check("ps1/checklist: ticking a task ticks its steps, unticking clears them", stepped.tickTask(GUID_A, at(1)).tasks.single().let { it.isDone && it.subtasks.all { step -> step.isDone } } &&
            stepped.tickTask(GUID_A, at(1)).untickTask(GUID_A).tasks.single().let { !it.isDone && it.subtasks.none { step -> step.isDone } })
        report.check("ps1/checklist: the last step ticked ticks the task at that time; unticking a step reopens it",
            stepped.tickStep("cccccccc-0000-0000-0000-000000000001", at(1)).tasks.single().isDone.not() &&
                stepped.tickStep("cccccccc-0000-0000-0000-000000000001", at(1)).tickStep("cccccccc-0000-0000-0000-000000000002", at(2)).tasks.single().doneAt == at(2) &&
                !stepped.tickTask(GUID_A, at(1)).untickStep("cccccccc-0000-0000-0000-000000000002").tasks.single().isDone)
    }

    private fun offGap(report: CheckReport) {
        val running = started(60)
        fun decide(record: PhoneSessionRecord, now: Instant, newBoot: Boolean = true, trusted: Boolean = true) = PhoneSessionOffGap.decide(record, now, newBoot, trusted, zone)
        val off = running.copy(heartbeatAt = at(10))
        report.check("ps1/off-gap (R13): 1:59 off carries on with nothing", decide(off, at(11, 59)) == PhoneSessionOffGap.Decision.Nothing)
        report.check("ps1/off-gap (R13): 2:00 off makes a DeviceOff pause", decide(off, at(12)) == PhoneSessionOffGap.Decision.Pause(at(10), at(12)))
        report.check("ps1/off-gap (R13): 3 h 59 min 59 s off still pauses", decide(running.copy(heartbeatAt = at(1), phaseEndsAt = null), at(1, 3 * 3600 + 59 * 60 + 59)) is PhoneSessionOffGap.Decision.Pause)
        report.check("ps1/off-gap (R13): exactly 4 h off closes the session at the moment the phone went off", decide(running.copy(heartbeatAt = at(1), phaseEndsAt = null), at(1, 4 * 3600)) ==
            PhoneSessionOffGap.Decision.Close(at(1), PhoneSessionOffGap.CloseWhy.FOUR_HOURS))
        // 12:00 local is t0 (UTC-5); 23:50 local is t0 + 11h50.
        val lateNight = running.copy(phaseEndsAt = null, heartbeatAt = at(11 * 60 + 50))
        report.check("ps1/off-gap (R13): a new local day after 2 minutes or more closes the session where the phone went off", decide(lateNight, at(12 * 60 + 10)) ==
            PhoneSessionOffGap.Decision.Close(at(11 * 60 + 50), PhoneSessionOffGap.CloseWhy.NEW_DAY))
        report.check("ps1/off-gap (R13): a quick restart across midnight (under 2 minutes) carries on", decide(lateNight.copy(heartbeatAt = at(11 * 60 + 59, 0)), at(12 * 60, 59)) == PhoneSessionOffGap.Decision.Nothing)
        report.check("ps1/off-gap (R13): the same day does not close (an 11:50 to 11:55 gap is a pause)", decide(lateNight.copy(heartbeatAt = at(1)), at(10)) is PhoneSessionOffGap.Decision.Pause)
        report.check("ps1/off-gap (B3a): the same boot makes no gap, however long the app was dead", decide(off, at(10 + 300), newBoot = false) == PhoneSessionOffGap.Decision.Nothing)
        report.check("ps1/off-gap (Q1163b): an untrusted clock at or past the off moment gives a Pause from the off moment to now (5 h, a new day, a short gap alike): never a close, no cap",
            decide(lateNight.copy(heartbeatAt = at(1)), at(1, 5 * 3600), trusted = false) == PhoneSessionOffGap.Decision.Pause(at(1), at(1, 5 * 3600)) &&
                decide(lateNight, at(12 * 60 + 10), trusted = false) == PhoneSessionOffGap.Decision.Pause(at(11 * 60 + 50), at(12 * 60 + 10)) &&
                decide(off, at(10, 30), trusted = false) == PhoneSessionOffGap.Decision.Pause(at(10), at(10, 30)))
        report.check("ps1/off-gap (B2): an untrusted clock set BACK (a negative gap) is a Hold, never Nothing and never a close", decide(off, at(-120), trusted = false) == PhoneSessionOffGap.Decision.Hold(at(10)))
        val held = done(PhoneSessionEngine.applyOffGap(off, PhoneSessionOffGap.Decision.Hold(at(10)), at(-120))).record
        report.check("ps1/off-gap (B2): a Hold opens a DeviceOff stretch at the moment the phone went off, keeps offAt, pauses the timer and sends awaySince; nothing counts as focus meanwhile",
            held.holdsDeviceOff && held.openAway?.from == at(10) && held.offAt == at(10) && held.isTimerPaused && SessionPieces.runningOf(held)?.awaySince == at(10) &&
                SessionPieces.build(listOf(held), at(100), zone, null).sessions.let { it.size == 2 && it[0].end == at(10) && it[1].start == at(100) })
        report.check("ps1/off-gap (B2): the Hold is applied once (a second Hold changes nothing)", done(PhoneSessionEngine.applyOffGap(held, PhoneSessionOffGap.Decision.Hold(at(10)), at(-100))).record == held)
        report.check("ps1/off-gap (V-B1): while a Hold is open, trusted time ends it only as a Pause from the hold's own start: even after 5 h or a new day it never closes",
            decide(held, at(10, 5 * 3600)) == PhoneSessionOffGap.Decision.Pause(at(10), at(10, 5 * 3600)) && decide(held, at(12 * 60 + 10)) is PhoneSessionOffGap.Decision.Pause)
        // The reviewer's scenario: set the clock back 5 h, reboot offline (Hold), power off again: ACTION_SHUTDOWN reports an untrusted time.
        val offAgain = done(PhoneSessionEngine.phoneOff(held, at(-290))).record
        val forward = done(PhoneSessionEngine.heartbeat(held, at(10, 20 * 3600))).record
        report.check("ps1/off-gap (V-B1): phoneOff and heartbeat change nothing while a Hold is open (their times may come from an untrusted anchor)", offAgain == held && forward == held)
        report.check("ps1/off-gap (V-B1): a held decide ignores offAt and the heartbeat (even if an untrusted clock wrote them): it measures from the hold's own start",
            decide(held.copy(offAt = at(-290), heartbeatAt = at(10, 20 * 3600)), at(16)) == PhoneSessionOffGap.Decision.Pause(at(10), at(16)))
        report.check("ps1/off-gap (V-B1): the scenario: Hold at 10:10, power off at an untrusted -5 h, boot with network time at 10:16: a pause of 6 minutes, the session lives (never a close at the fake off time)",
            decide(offAgain, at(16)) == PhoneSessionOffGap.Decision.Pause(at(10), at(16)) &&
                done(PhoneSessionEngine.applyOffGap(offAgain, decide(offAgain, at(16)), at(16))).record.let { it.isRunning && it.phaseEndsAt == at(66) })
        val resolved = done(PhoneSessionEngine.applyOffGap(held, decide(held, at(40)), at(40))).record
        report.check("ps1/off-gap (B2): trusted time 30 minutes later closes the stretch there and the end moves on by it",
            decide(held, at(40)) is PhoneSessionOffGap.Decision.Pause && resolved.away == listOf(PhoneAway(PhoneAwayKind.DEVICE_OFF, at(10), at(40))) && resolved.phaseEndsAt == at(90) && resolved.offAt == null && !resolved.isAway)
        report.check("ps1/off-gap (B2): a gap under 2 minutes still closes a hold that is open (the timer was paused), never Nothing", decide(held, at(10, 30)) is PhoneSessionOffGap.Decision.Pause)
        val heldPaused = done(PhoneSessionEngine.applyOffGap(done(PhoneSessionEngine.pause(started(100), at(5))).record.copy(heartbeatAt = at(10)), PhoneSessionOffGap.Decision.Hold(at(10)), at(11))).record
        val heldBack = done(PhoneSessionEngine.applyOffGap(heldPaused, decide(heldPaused, at(40)), at(40))).record
        report.check("ps1/off-gap (B2): a Hold in a Pause timer gives the pause back when it ends (open again from the hold's end), and the shifts add up",
            heldPaused.heldAway == PhoneAwayKind.PAUSED && heldBack.openAway == PhoneAway(PhoneAwayKind.PAUSED, at(40), null) && heldBack.heldAway == null && heldBack.phaseEndsAt == at(100 + 5 + 30))
        report.check("ps1/off-gap (B3c): the set end passed before the phone went off closes at the SET END, not at the off moment", decide(running.copy(heartbeatAt = at(70)), at(75)) ==
            PhoneSessionOffGap.Decision.Close(at(60), PhoneSessionOffGap.CloseWhy.SET_END_PASSED))
        report.check("ps1/off-gap (B3c): that holds on an untrusted clock too (it needs no trust: stored times only)", decide(running.copy(heartbeatAt = at(70)), at(75), trusted = false) is PhoneSessionOffGap.Decision.Close)
        report.check("ps1/off-gap (R13): offAt (ACTION_SHUTDOWN) is used before the last heartbeat", decide(off.copy(offAt = at(20)), at(30)) == PhoneSessionOffGap.Decision.Pause(at(20), at(30)))

        val paused = done(PhoneSessionEngine.applyOffGap(off, PhoneSessionOffGap.Decision.Pause(at(10), at(40)), at(40)))
        report.check("ps1/off-gap: a pause adds a DeviceOff stretch, moves start and end on by the gap (30 min), tells the user",
            paused.record.away == listOf(PhoneAway(PhoneAwayKind.DEVICE_OFF, at(10), at(40))) && paused.record.phaseEndsAt == at(90) && paused.record.phaseStartedAt == at(30) &&
                PhoneSessionEffect.OffGapNotice(at(10), at(40), closed = false) in paused.effects)
        val pieces = SessionPieces.build(listOf(paused.record), at(50), zone, null).sessions
        report.check("ps1/off-gap: the pieces cut the DeviceOff gap out and the next piece carries offMinutesBefore 30", pieces.size == 2 && pieces[0].end == at(10) && pieces[1].start == at(40) && pieces[1].end == null && pieces[1].offMinutesBefore == 30)
        val closed = done(PhoneSessionEngine.applyOffGap(off, PhoneSessionOffGap.Decision.Close(at(10), PhoneSessionOffGap.CloseWhy.FOUR_HOURS), at(400)))
        report.check("ps1/off-gap: a close ends the session at the off moment as PHONE_OFF and releases the enforcement", closed.record.endedAt == at(10) && closed.record.endKind == PhoneSessionEndKind.PHONE_OFF &&
            PhoneSessionEffect.ReleaseEnforcement(PhoneSessionEndKind.PHONE_OFF) in closed.effects)
        val pausedWhileOff = done(PhoneSessionEngine.pause(started(100), at(5))).record.copy(heartbeatAt = at(10))
        val both = done(PhoneSessionEngine.applyOffGap(pausedWhileOff, PhoneSessionOffGap.Decision.Pause(at(10), at(40)), at(40))).record
        report.check("ps1/off-gap: a gap in a Pause timer keeps the pause going after it (open again from the gap's end) and the shifts add up",
            both.openAway == PhoneAway(PhoneAwayKind.PAUSED, at(40), null) && both.away.size == 3 && both.phaseEndsAt == at(100 + 5 + 30))
        report.check("ps1/off-gap: nothing to do clears the saved off time", done(PhoneSessionEngine.applyOffGap(off.copy(offAt = at(11)), PhoneSessionOffGap.Decision.Nothing, at(12))).record.offAt == null)
        report.check("ps1/heartbeat: saved at each refresh, never moving back; ACTION_SHUTDOWN saves offAt", done(PhoneSessionEngine.heartbeat(running, at(3))).record.heartbeatAt == at(3) &&
            done(PhoneSessionEngine.heartbeat(off, at(2))).record.heartbeatAt == at(10) && done(PhoneSessionEngine.phoneOff(running, at(4))).record.offAt == at(4))
    }

    private fun locks(report: CheckReport) {
        val ab = PhoneSessionLocks.scope(AB, PhoneSessionProfileUse.NOTHING)
        val pb = PhoneSessionLocks.scope(PB, PhoneSessionProfileUse.NOTHING)
        report.check("ps1/locks (R11): AccountabiliBuddy locks every area, plan, removal, restore and the session profiles included", ab == PhoneLockArea.values().toSet() && PhoneLockArea.PLAN in ab && PhoneLockArea.REMOVAL in ab && PhoneLockArea.SESSION_PROFILES in ab)
        val plainTimer = PhoneSessionProfileUse.NOTHING
        val lmp = PhoneSessionProfileUse(lockMyPhoneOrEssentials = true, curbox = false)
        val curbox = PhoneSessionProfileUse(lockMyPhoneOrEssentials = false, curbox = true)
        val both = PhoneSessionProfileUse(true, true)
        report.check("ps1/locks (S4): a ProductaBuddy phone session locks only what its profile turns on: a plain timer locks just the session profiles and the end-early setting",
            pb == setOf(PhoneLockArea.SESSION_PROFILES, PhoneLockArea.END_EARLY_SETTING) && PhoneSessionLocks.scope(PB, plainTimer) == pb)
        report.check("ps1/locks (S4): an LMP/Essentials profile adds the Guards rows, a Curbox profile adds the Curbox rows, both add both",
            PhoneSessionLocks.scope(PB, lmp) == pb + PhoneLockArea.GUARDS_LMP_ESSENTIALS && PhoneSessionLocks.scope(PB, curbox) == pb + PhoneLockArea.CURBOX_ROWS &&
                PhoneSessionLocks.scope(PB, both) == pb + PhoneLockArea.GUARDS_LMP_ESSENTIALS + PhoneLockArea.CURBOX_ROWS)
        report.check("ps1/locks (S4): AccountabiliBuddy locks everything whatever the profile", PhoneSessionLocks.scope(AB, plainTimer) == ab)
        report.check("ps1/locks (R11): ProductaBuddy leaves the plan, sync, removal and other settings open", listOf(PhoneLockArea.PLAN, PhoneLockArea.SYNC, PhoneLockArea.REMOVAL, PhoneLockArea.OTHER_SETTINGS, PhoneLockArea.RESTORE).none { it in pb })
        report.check("ps1/locks (R11): nothing locks with no session, an ended one or a test", PhoneSessionLocks.scope(AB, null, PhoneSessionProfileUse.NOTHING).isEmpty() && PhoneSessionLocks.scope(AB, started().copy(endedAt = at(1)), PhoneSessionProfileUse.NOTHING).isEmpty() && PhoneSessionLocks.scope(AB, started(test = true), PhoneSessionProfileUse.NOTHING).isEmpty())
        report.check("ps1/locks (R11): a running one gives its mode's scope; with a PC session too, the stricter of the two", PhoneSessionLocks.scope(PB, started(), both) == pb + PhoneLockArea.GUARDS_LMP_ESSENTIALS + PhoneLockArea.CURBOX_ROWS && PhoneSessionLocks.stricter(pb, ab) == ab)
    }

    private fun theFile(report: CheckReport) {
        val record = started(tasks = listOf(task(GUID_A)))
        val running = SessionPieces.runningOf(record)
        report.check("ps1/file (R14): the running card has the profile NAME, the phase times, no break and no profile id", running?.profile == "Deep work" && running.phaseStartedAt == t0 && running.phaseEndsAt == at(50) && !running.isBreak &&
            running.profileId == null && running.countUpProfileId == null && running.offSince == null && running.planningEndsAt == null)
        report.check("ps1/file (R7): awaySince only while the timer is paused (Pause timer, lunch), never for a step away (ADR 77)",
            SessionPieces.runningOf(done(PhoneSessionEngine.pause(record, at(5))).record)?.awaySince == at(5) && SessionPieces.runningOf(done(PhoneSessionEngine.lunchHold(record, at(5), at(5))).record)?.awaySince == at(5) &&
                SessionPieces.runningOf(done(PhoneSessionEngine.stepAway(record, at(5))).record)?.awaySince == null)
        report.check("ps1/file: a test session and an ended one have no running card and no pieces", SessionPieces.runningOf(started(test = true)) == null && SessionPieces.runningOf(record.copy(endedAt = at(5))) == null &&
            SessionPieces.build(listOf(started(test = true)), at(5), zone, null).sessions.isEmpty())
        report.check("ps1/file (B5): an unreadable log writes no sessions and coversFrom = writtenAt", SessionPieces.whenLogUnreadable(at(7)).let { it.sessions.isEmpty() && it.coversFrom == at(7) })
        report.check("ps1/file (B11): ids go as lower-case GUID text; a task or step with another id is left out; a local task is a stub",
            SessionPieces.forSync(listOf(task(GUID_A.uppercase()), task("task-1"), SessionTaskItem(GUID_B, "private", at(1), emptyList(), localTo = "11111111-0000-0000-0000-00000000000a"))).let { out ->
                out.size == 2 && out[0].id == GUID_A && out[1].title == "" && out[1].doneAt == null && out[1].localTo != null
            })
        report.check("ps1/file (B11): a category id that is not a GUID is dropped with its name and colour", SessionPieces.forSync(listOf(SessionTaskItem(GUID_A, "T", null, emptyList(), "work", "Work", "#2563EB"))).single().let { it.categoryId == null && it.category == null && it.categoryColor == null })
        val file = SessionFile.of(record.copy(checklist = record.checklist.copy(tasks = listOf(task(GUID_A)))), at(5))
        val text = SyncFileJson.write(file).toString()
        report.check("ps1/file (R14): nothing of the profile ids, lock names or reasons is in the file", !text.contains("\"profileId\"") && !text.contains("\"p1\"") && !text.contains("\"s1\""))
        // R2 on a file built from a record: the PC-side rule sees the phone's session as live while the file is fresh, and not after 15 minutes.
        val pcLive: (ReceivedSyncFile) -> com.accountabilibuddy.shared.sync.SyncRunning? = { null }
        report.check("ps1/file (R2): a file built from a running record is live for another device while fresh and not once 16 minutes quiet",
            GroupSessions.isLive(ReceivedSyncFile(file, at(5)), at(10), pcLive) && !GroupSessions.isLive(ReceivedSyncFile(file, at(5)), at(21), pcLive))
        report.check("ps1/file (B2): heardAt: a stamp more than 2 minutes ahead of the arrival is held to the arrival (phone and PC), up to 2:00 ahead is believed",
            GroupSessions.heardAt(file.device.copy(writtenAt = at(180)), at(10)) == at(10) && GroupSessions.heardAt(file.device.copy(kind = SyncDeviceCard.KIND_PC, writtenAt = at(180)), at(10)) == at(10) &&
                GroupSessions.heardAt(file.device.copy(writtenAt = at(12)), at(10)) == at(12) && GroupSessions.heardAt(file.device.copy(writtenAt = at(12, 1)), at(10)) == at(10) && GroupSessions.heardAt(file.device.copy(writtenAt = at(5)), at(10)) == at(5))
        // The writer cuts every stretch away (owner "cut paused time"), the one going now included; the open piece that follows is empty.
        val pausedNow = done(PhoneSessionEngine.pause(started(null), at(30))).record
        val cut = SessionPieces.build(listOf(pausedNow), at(45), zone, null).sessions
        report.check("ps1/file (B1): a session paused now has a closed piece up to the pause and an empty open piece (start = the write time)",
            cut.size == 2 && cut[0].end == at(30) && cut[1].start == at(45) && cut[1].end == null)
        val stepping = done(PhoneSessionEngine.stepAway(started(null), at(30))).record
        report.check("ps1/file (B1): the same for a step away going now (it has no awaySince, the writer's start time is enough)",
            SessionPieces.build(listOf(stepping), at(45), zone, null).sessions.let { it.size == 2 && it[0].end == at(30) && it[1].start == at(45) })
        val resumed = done(PhoneSessionEngine.resume(pausedNow, at(40))).record
        report.check("ps1/file (B1): a finished pause is cut; the open piece after it starts where the pause ended",
            SessionPieces.build(listOf(resumed), at(45), zone, null).sessions.let { it.size == 2 && it[0].end == at(30) && it[1].start == at(40) && it[1].end == null })
        report.check("ps1/file (S1): the DeviceOff gap's minutes round half up (0:59 none, 1:00 1, 30:30 31, 31:30 32)", listOf(59L to null, 60L to 1, 1830L to 31, 1890L to 32).all { (seconds, minutes) ->
            val gap = started(null).copy(away = listOf(PhoneAway(PhoneAwayKind.DEVICE_OFF, at(10), at(10, seconds))))
            SessionPieces.build(listOf(gap), at(100), zone, null).sessions.last().offMinutesBefore == minutes
        })
        report.check("ps1/file (S2): a file with no sessions still says coversFrom (never null): the 90-day rule, or writtenAt when the log is unreadable",
            SessionPieces.build(emptyList(), at(5), zone, null).coversFrom == at(5).minus(Duration.ofDays(90)) && SessionPieces.whenLogUnreadable(at(5)).coversFrom == at(5))
        // V2: a stub of a task kept local to another device is never a blank open task in a reader's list.
        val stub = SessionTaskItem(GUID_B, "", null, emptyList(), localTo = "11111111-0000-0000-0000-00000000000a")
        val pieces = listOf(
            SyncSession(at(0), at(30), "Deep work", false, listOf(task(GUID_A), stub)),
            SyncSession(at(40), null, "Deep work", false, listOf(task(GUID_A), stub)),
        )
        report.check("ps1/file (V2): the running tasks are the open piece's and skip a local stub", SessionPieces.runningTasks(pieces).map { it.id } == listOf(GUID_A))
        report.check("ps1/file (B1): a running session with no checklist after one with a checklist (same profile, 20 minutes apart) shows none: no walk back lends the earlier list",
            SessionPieces.runningTasks(listOf(SyncSession(at(0), at(30), "Deep work", false, listOf(task(GUID_A))), SyncSession(at(50), null, "Deep work", false))).isEmpty())
        val withTasks = started(null, tasks = listOf(task(GUID_A)))
        val pausedWithTasks = done(PhoneSessionEngine.pause(withTasks, at(30))).record
        val written = SessionPieces.build(listOf(pausedWithTasks), at(45), zone, null).sessions
        report.check("ps1/file (B1): the writer carries the checklist on the first piece AND the open piece (a session cut by a pause keeps its list for a reader)",
            written.size == 2 && written[0].tasks.isNotEmpty() && written[1].end == null && written[1].tasks.map { it.id } == listOf(GUID_A) && SessionPieces.runningTasks(written).map { it.id } == listOf(GUID_A))
        val cutTwice = done(PhoneSessionEngine.stepAway(done(PhoneSessionEngine.resume(pausedWithTasks, at(40))).record, at(42))).record
        val ended = cutTwice.copy(endedAt = at(50), endKind = PhoneSessionEndKind.ON_COMMAND, away = cutTwice.away.map { it.copy(to = it.to ?: at(50)) })
        report.check("ps1/file (B1): an ENDED session keeps the checklist on its first piece only",
            SessionPieces.build(listOf(ended), at(60), zone, null).sessions.let { list -> list.size >= 2 && list.first().tasks.isNotEmpty() && list.drop(1).all { it.tasks.isEmpty() } })
        val sessionFile = SessionFile.of(pausedWithTasks, at(45))
        report.check("ps1/file (B3): running.sessionStartedAt is the record's start (it does not move with the pieces), is written and read back; an older file without it reads as null",
            SessionPieces.runningOf(pausedWithTasks)?.sessionStartedAt == t0 && SyncFileJson.read(SyncFileJson.write(sessionFile))?.running?.sessionStartedAt == t0 &&
                SyncFileJson.read(SyncFileJson.write(sessionFile.copy(running = sessionFile.running?.copy(sessionStartedAt = null))))?.running?.sessionStartedAt == null &&
                !SyncFileJson.write(sessionFile.copy(running = sessionFile.running?.copy(sessionStartedAt = null))).toString().contains("sessionStartedAt"))
        report.check("ps1/file (S5): currentTask goes only as a GUID (the PC refuses a file with another form)", SessionPieces.runningOf(withTasks.copy(currentTask = "task-1"))?.currentTask == null &&
            SessionPieces.runningOf(withTasks.copy(currentTask = GUID_A.uppercase()))?.currentTask == GUID_A)
        // S2: another device's open piece, for the goals: closed where a receiver counts it, not run to now.
        val away = SyncFile(1, SyncDeviceCard(UUID(0, 41), "PC", SyncDeviceCard.KIND_PC, "0.1.78", at(60), listOf(SyncDeviceCard.METHOD_RELAY), false, emptyMap()),
            listOf(SyncSession(at(-60), null, "Deep work", false)), emptyList(), null, emptyList(), null,
            running = com.accountabilibuddy.shared.sync.SyncRunning("Deep work", at(-60), null, false, at(-20)))
        val later = at(600)
        fun goalMinutes(file: SyncFile): Long = com.accountabilibuddy.shared.goals.GoalFocusAttribution.fromSynced(
            (SyncFileJson.write(file)["sessions"] as kotlinx.serialization.json.JsonArray).map(com.accountabilibuddy.shared.goals.GoalJson::readPiece), later, zone,
        ).flatMap { it.windows }.sumOf { Duration.between(it.start, it.end).toMinutes() }
        report.check("ps1/goals (S2): another device's open piece run to now is the problem (over 600 minutes); closed through GroupSessions.closeOpenPieces it ends where the user went away (40 minutes)",
            goalMinutes(away) > 600 && goalMinutes(GroupSessions.closeOpenPieces(away, at(60), later)) == 40L)
    }

    // B2 and B4 in the Kotlin readers of a received file: an open piece ends at heardAt (a stamp more than 2 minutes ahead of the arrival is
    // held to it), never after now, never after awaySince; a PC's file is read the same way.
    private fun clamp(report: CheckReport) {
        val now = at(0)
        val open = SyncSession(at(-60), null, "Deep work", false)
        fun file(kind: String, writtenAt: Instant, id: Long, sessions: List<SyncSession> = listOf(open), coversFrom: Instant? = null, awaySince: Instant? = null) = SyncFile(
            1, SyncDeviceCard(UUID(0, id), "Device$id", kind, "0.1.78", writtenAt, listOf(SyncDeviceCard.METHOD_RELAY), false, emptyMap()), sessions, emptyList(), null, emptyList(), coversFrom,
            running = awaySince?.let { com.accountabilibuddy.shared.sync.SyncRunning("Deep work", at(-60), null, false, it) },
        )
        val today = now.atZone(zone).toLocalDate()
        fun focused(file: SyncFile, receivedAt: Instant) =
            com.accountabilibuddy.shared.sync.FocusDayCombine.combine(listOf("d" to file), today, zone, now, receivedAtOf = { receivedAt })?.focusedMinutes
        report.check("ps1/clamp (B2): FocusDayCombine ends a phone's open piece where it was last heard (stamp 3 h ahead, arrived 10 minutes ago): 50 minutes",
            focused(file(SyncDeviceCard.KIND_PHONE, at(180), 1), at(-10)) == 50)
        report.check("ps1/clamp (B2): the same for a PC's file (any device)", focused(file(SyncDeviceCard.KIND_PC, at(180), 2), at(-10)) == 50)
        report.check("ps1/clamp (B4): a stamp 3 h ahead that arrived just now still counts only until now: 60", focused(file(SyncDeviceCard.KIND_PHONE, at(180), 1), now) == 60)
        report.check("ps1/clamp (B2): an honest file ends at its stamp (stamp 5 minutes ago, arrived then: 55); a stamp 1 minute ahead of its arrival is believed (56)",
            focused(file(SyncDeviceCard.KIND_PC, at(-5), 2), at(-5)) == 55 && focused(file(SyncDeviceCard.KIND_PHONE, at(-4), 2), at(-5)) == 56)
        report.check("ps1/clamp (B4): a stamp 1 minute ahead of now (believed, within 2 minutes) still ends the open piece at now: 60, not 61", focused(file(SyncDeviceCard.KIND_PHONE, at(1), 1), at(0)) == 60)
        val carryOwn = UUID(0, 99)
        fun carry(stamp: Instant, receivedAt: Instant, incomingStamp: Instant): com.accountabilibuddy.companion.sync.SyncCarry.Outcome {
            val storedDevice = com.accountabilibuddy.companion.sync.ReceivedDevice(file(SyncDeviceCard.KIND_PHONE, stamp, 7), receivedAt, SyncDeviceCard.METHOD_DRIVE, ByteArray(0))
            return com.accountabilibuddy.companion.sync.SyncCarry.accept(listOf(storedDevice), file(SyncDeviceCard.KIND_PHONE, incomingStamp, 7, sessions = emptyList(), coversFrom = at(-1)), ByteArray(0), SyncDeviceCard.METHOD_DRIVE, carryOwn, now)
        }
        report.check("ps1/clamp (V1): the phone's receive path refuses an honest older file (stored stamp 5 s ahead of its arrival, incoming 1 s ahead) and takes one after a skewed copy",
            carry(at(0, 5), at(0), at(0, 1)).error != null && carry(at(180), at(-10), at(-1)).error == null)
        report.check("ps1/clamp (B1): a device that went away 30 minutes ago counts only to then, whoever wrote the file",
            focused(file(SyncDeviceCard.KIND_PC, at(-1), 2, awaySince = at(-30)), at(-1)) == 30 && focused(file(SyncDeviceCard.KIND_PHONE, at(-1), 1, awaySince = at(-30)), at(-1)) == 30)
        val stored = file(SyncDeviceCard.KIND_PHONE, at(180), 3)
        val incoming = file(SyncDeviceCard.KIND_PHONE, at(240), 3, sessions = emptyList(), coversFrom = at(60))
        val kept = com.accountabilibuddy.shared.sync.SyncMerge.merge(stored, incoming, now, storedReceivedAt = at(-5)).sessions
        report.check("ps1/clamp (B2): SyncMerge closes a stored copy's open piece where it was last heard (arrived 5 minutes ago), not at its stamp", kept.singleOrNull()?.end == at(-5), "$kept")
        val seconds = com.accountabilibuddy.shared.sync.SyncMerge.merge(
            file(SyncDeviceCard.KIND_PHONE, at(0, 5), 5), file(SyncDeviceCard.KIND_PHONE, at(0, 1), 5, sessions = emptyList(), coversFrom = at(-1)), now, storedReceivedAt = at(0),
        )
        report.check("ps1/clamp (V1): an honest older file is refused when the stored stamp is only 5 seconds ahead of its arrival (the stored copy is kept)", seconds.device.writtenAt == at(0, 5))
        val skewed = com.accountabilibuddy.shared.sync.SyncMerge.merge(
            file(SyncDeviceCard.KIND_PHONE, at(180), 6), file(SyncDeviceCard.KIND_PHONE, at(-1), 6, sessions = emptyList(), coversFrom = at(-1)), now, storedReceivedAt = at(-10),
        )
        report.check("ps1/clamp (B2): a later honest file is taken after a skewed one (stored stamp 3 h ahead, arrived 10 minutes ago)", skewed.device.writtenAt == at(-1))
    }

    // The PS1b review's fixes: S3 (resume during a lunch), S7 (Undo), and the cheap nits N1 to N4.
    private fun reviewFixes(report: CheckReport) {
        // V-S1: pickups of another device's running session key its OPEN piece by the session's own start (it moves while away), as the PC's PcPickupsService.
        val other = UUID(0, 77)
        val piece = SyncSession(at(40), null, "Deep work", false)
        val unlocks = listOf(at(10), at(20), at(50))
        val keyed = com.accountabilibuddy.shared.sync.SyncPickup.count(listOf(other to piece), unlocks, at(60), sessionStartedAt = mapOf(other to t0))
        val unkeyed = com.accountabilibuddy.shared.sync.SyncPickup.count(listOf(other to piece), unlocks, at(60))
        report.check("ps1/pickups (V-S1): the open piece is keyed by running.sessionStartedAt (a stable start, unlocks counted from it): 3 pickups from the start; without it the piece's own start counts only 1",
            keyed.single().sessionStart == t0 && keyed.single().count == 3 && unkeyed.single().sessionStart == at(40) && unkeyed.single().count == 1)
        report.check("ps1/pickups (V-S1): a started time AFTER the piece's start is not believed, and a closed piece keeps its own start",
            com.accountabilibuddy.shared.sync.SyncPickup.count(listOf(other to piece), unlocks, at(60), sessionStartedAt = mapOf(other to at(50))).single().sessionStart == at(40) &&
                com.accountabilibuddy.shared.sync.SyncPickup.count(listOf(other to piece.copy(end = at(55))), unlocks, at(60), sessionStartedAt = mapOf(other to t0)).single().sessionStart == at(40))
        // S3: Pause timer 12:00, lunch starts, Resume at 12:10 while the lunch still runs: the lunch keeps its pause, the timer does not restart.
        val paused = done(PhoneSessionEngine.pause(started(100), at(0))).record
        val lunchRunning = done(PhoneSessionEngine.resume(paused, at(10), lunchRunning = true)).record
        report.check("ps1/resume (S3): resuming while a lunch runs hands the pause to the lunch (a LUNCH stretch opens at once): the timer stays frozen", lunchRunning.openAway == PhoneAway(PhoneAwayKind.LUNCH, at(10), null) && lunchRunning.isTimerPaused &&
            done(PhoneSessionEngine.tick(lunchRunning, at(600))).record.isRunning)
        val lunchOver = done(PhoneSessionEngine.lunchEnd(lunchRunning, at(40))).record
        report.check("ps1/resume (S3): the lunch's end then moves start and end by the whole time away (the pause and the lunch: 40 minutes)", lunchOver.phaseEndsAt == at(140) && !lunchOver.isAway)
        report.check("ps1/resume (S3): resuming with no lunch running restarts the timer as before", !done(PhoneSessionEngine.resume(paused, at(10))).record.isAway)

        // S7: a removed open task and its Undo give "All tasks done" back.
        val record = started(tasks = listOf(task(GUID_A), task(GUID_B)))
        val removed = done(PhoneSessionEngine.checklistChanged(record, listOf(task(GUID_A)))).record
        val undone = done(PhoneSessionEngine.checklistUndo(removed, listOf(task(GUID_A), task(GUID_B)))).record
        val ticked = done(PhoneSessionEngine.checklistChanged(undone, listOf(task(GUID_A, at(1)), task(GUID_B, at(2))))).record
        report.check("ps1/undo (S7): remove an open task counts a drop; Undo takes it back, so 'All tasks done' ends it at once after ticking both", removed.checklist.droppedOpen == 1 && undone.checklist.droppedOpen == 0 &&
            PhoneSessionEngine.endAtOnce(ticked, at(10)) is PhoneSessionResult.Done)
        val removedDone = done(PhoneSessionEngine.checklistChanged(done(PhoneSessionEngine.checklistChanged(record, listOf(task(GUID_A, at(1)), task(GUID_B)))).record, listOf(task(GUID_B)))).record
        report.check("ps1/undo (S7): Undo of a task that was ticked when it left takes no drop off (it added none); never below zero", done(PhoneSessionEngine.checklistUndo(removedDone, listOf(task(GUID_A, at(1)), task(GUID_B)))).record.checklist.droppedOpen == 0 &&
            done(PhoneSessionEngine.checklistUndo(record, listOf(task(GUID_A), task(GUID_B)))).record.checklist.droppedOpen == 0)

        // An Undo of a TICKED task takes no drop off even when another open task was dropped (the floor at zero would hide it otherwise).
        val tickedGone = done(PhoneSessionEngine.checklistChanged(done(PhoneSessionEngine.checklistChanged(record, listOf(task(GUID_A), task(GUID_B, at(1))))).record, listOf(task(GUID_A)))).record
        val bothGone = done(PhoneSessionEngine.checklistChanged(tickedGone, emptyList())).record
        report.check("ps1/undo (S7): with an open task dropped (1) and a ticked one removed, Undo of the ticked one keeps that drop (1), Undo of the open one clears it",
            bothGone.checklist.droppedOpen == 1 && done(PhoneSessionEngine.checklistUndo(bothGone, listOf(task(GUID_B, at(1))))).record.checklist.droppedOpen == 1 &&
                done(PhoneSessionEngine.checklistUndo(bothGone, listOf(task(GUID_A)))).record.checklist.droppedOpen == 0)
        report.check("ps1/undo (V-N1): a caller that claims an Undo for a plain add (a task that was never dropped) gets no drop back", bothGone.let { g ->
            done(PhoneSessionEngine.checklistUndo(g, g.checklist.tasks + task("aaaaaaaa-0000-0000-0000-0000000000ff"))).record.checklist.droppedOpen == 1 })
        // N1: a test session never asks for a sync file write, not even when it ends.
        report.check("ps1/nit (N1): ending a test session asks for no sync file write", PhoneSessionEffect.WriteSyncFile !in done(PhoneSessionEngine.endOnCommand(started(null, test = true), at(5))).effects)
        // N2: an End opened and never cancelled does not keep its wait for hours.
        val opened = done(PhoneSessionEngine.openEnd(started(), at(5))).record
        report.check("ps1/nit (N2): an End opened 30 minutes ago counts as new: confirming now arms for a full wait, it does not end at once",
            done(PhoneSessionEngine.endEarly(opened, at(35), AB, defaults, "a real reason, long enough")).record.let { it.isRunning && it.armedEnd?.readyAt == at(36) })
        // N3: no reason asked (minimum 0) with a wait, and none given.
        report.check("ps1/nit (N3): a wait with no minimum reason and no reason given arms instead of throwing", done(PhoneSessionEngine.endEarly(started(), at(5), AB, PhoneEndSettings(60, 0), null)).record.armedEnd != null)
        // N4: a lunch after the set end passed is refused (the late alarm has not ended it yet).
        report.check("ps1/nit (N4): a lunch seen after the set end is refused: it must not count minutes past the end", refusal(PhoneSessionEngine.lunchHold(started(), at(55), at(55))) == PhoneSessionRefusal.TIME_IS_UP)
    }

    private fun sources(report: CheckReport) {
        val shared = "../shared/src/main/java/com/accountabilibuddy/shared"
        val files = (File("$shared/session").listFiles { f -> f.extension == "kt" }.orEmpty().toList() + listOf(File("$shared/sync/GroupSessions.kt"), File("$shared/sync/SessionPieces.kt")))
        report.check("ps1/sources: the session files were found", files.size >= 14 && files.all { it.isFile })
        val clock = Regex("""Instant\.now\(|System\.currentTimeMillis|Clock\.|LocalDateTime\.now|ZonedDateTime\.now|OffsetDateTime\.now|SystemClock""")
        val bad = files.filter { file -> Sync1cCheck.code(file.readText()).let { clock.containsMatchIn(it) } }.map { it.name }
        report.check("ps1/sources (R12): no wall clock anywhere in the session engine, its rules, GroupSessions or SessionPieces: every time is handed in", bad.isEmpty(), "reads a clock: $bad")
        val androidImport = files.filter { Regex("""^import (android|androidx)\.""", RegexOption.MULTILINE).containsMatchIn(it.readText()) }.map { it.name }
        report.check("ps1/sources: no Android type in shared/session or the new sync files", androidImport.isEmpty(), "$androidImport")
        val forbidden = Regex("""packageName|package name|distractingApps|curboxBlocks|lmpProfile|curboxFocusGroup""", RegexOption.IGNORE_CASE)
        val leaks = listOf(File("$shared/sync/SessionPieces.kt")).filter { forbidden.containsMatchIn(Sync1cCheck.code(it.readText())) }.map { it.name }
        report.check("ps1/sources (R14): the file writer never names an app list, a package, an LMP profile or a Curbox id", leaks.isEmpty(), "$leaks")
        val app = Sync1cCheck.appSource("CompanionApp.kt")?.let(Sync1cCheck::code)
        report.check("ps1/sources (S6): the sync coordinator is built with trusted time (the arrival time of a file and this phone's stamp)",
            app != null && app.substringAfter("sync = SyncCoordinator(").substringBefore("backupOpener").contains("TrustedTime.now()"))
        val stats = Sync1cCheck.appSource("ui/StatsScreen.kt")?.let(Sync1cCheck::code)
        report.check("ps1/sources (S6): Stats passes trusted time to StatsData.build", stats != null && stats.contains("TrustedTime.now(), zone) { date") && !stats.contains("Instant.now(), zone) { date"))
        val placement = Sync1cCheck.appSource("placement/PlacementSession.kt")?.let(Sync1cCheck::code)
        report.check("ps1/sources (B3): the placement session's start is running.sessionStartedAt first, the open piece's start only for an older file", placement != null &&
            placement.contains("running.sessionStartedAt ?: file.file.sessions.lastOrNull { it.end == null && !it.added }?.start"))
        val control = Sync1cCheck.appSource("sync/PcFileControl.kt")?.let(Sync1cCheck::code)
        report.check("ps1/sources (S1): the group's PC rule on the phone is liveRunning + guardsHold (PcFileControl.groupLive), the one the Today PC card and the locks read",
            control != null && control.contains("fun groupLive(") && control.substringAfter("fun groupLive(").substringBefore("fun pcs(").let { it.contains("liveRunning(received.file, now)") && it.contains("guardsHold(received.file, now)") })
        val coordinator = Sync1cCheck.appSource("sync/SyncCoordinator.kt")?.let(Sync1cCheck::code)
        report.check("ps1/sources (V-S1): the phone's pickups pass each device's running.sessionStartedAt to SyncPickup.count",
            coordinator != null && coordinator.contains("device.file.running?.sessionStartedAt") && coordinator.contains("SyncPickup.count(sessions, unlocks, now, emergencyWindows(), started)"))
        val goalData = Sync1cCheck.appSource("goals/PhoneGoalData.kt")?.let(Sync1cCheck::code)
        report.check("ps1/sources (S2): the phone's goals read another device's sessions through GroupSessions.closeOpenPieces", goalData != null && goalData.contains("GroupSessions.closeOpenPieces(file, receivedAtOf(file), now)"))
        val windows = Sync1cCheck.appSource("stats/FocusWindows.kt")?.let(Sync1cCheck::code)
        report.check("ps1/sources (B4): FocusWindows reads the open piece through GroupSessions.openPieceEnd", windows != null && windows.contains("GroupSessions.openPieceEnd(file, received.receivedAt, now)") && !windows.contains("file.device.writtenAt"))
    }

    /** A sync file for a record, as the phone writes it: the running card, the pieces and what they speak for. */
    private object SessionFile {
        fun of(record: PhoneSessionRecord, now: Instant): SyncFile {
            val built = SessionPieces.build(listOf(record), now, zone, null)
            val card = SyncDeviceCard(UUID.fromString("33333333-0000-0000-0000-00000000000c"), "Pixel", SyncDeviceCard.KIND_PHONE, "0.1.78", now, listOf(SyncDeviceCard.METHOD_RELAY), false, emptyMap())
            return SyncFile(1, card, built.sessions, emptyList(), null, emptyList(), built.coversFrom, running = SessionPieces.runningOf(record))
        }
    }
}
