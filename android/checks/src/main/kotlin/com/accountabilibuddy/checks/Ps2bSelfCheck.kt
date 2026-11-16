package com.accountabilibuddy.checks

import com.accountabilibuddy.checks.Ps2aSelfCheck.FakeSource
import com.accountabilibuddy.checks.Ps2aSelfCheck.World
import com.accountabilibuddy.companion.session.PhoneSessionFiles
import com.accountabilibuddy.companion.stats.FocusWindows
import com.accountabilibuddy.companion.sync.OwnFileWriteGate
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.companion.sync.arrivalOf
import com.accountabilibuddy.shared.session.PhoneAway
import com.accountabilibuddy.shared.session.PhoneAwayKind
import com.accountabilibuddy.shared.session.PhoneSessionChecklist
import com.accountabilibuddy.shared.session.PhoneSessionRecord
import com.accountabilibuddy.shared.session.PhoneSessionRead
import com.accountabilibuddy.shared.session.PhoneSessionSyncInput
import com.accountabilibuddy.shared.session.PhoneSessionSyncPart
import com.accountabilibuddy.shared.session.PhoneSessionSyncWriter
import com.accountabilibuddy.shared.sync.ReceivedSyncFile
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.sync.SyncSession
import java.time.Duration
import java.time.Instant
import java.time.ZoneOffset
import java.util.UUID

/**
 * PS2b (phone only): what the phone writes about its own sessions and how it counts them. The pieces' cutting is [Ps1Check]'s (against the PC's own
 * output); here: the writer's decisions (B4 the stamp, B5 an unreadable log, V-N1 unchecked times, R14 privacy, B11 stubs), the phone's own sessions
 * counted with the other devices' (R4: overlap once), the 5-minute write (B13) driven through the real controller, and source scans for the wiring and
 * for every `allReceived` reader (device lists keep to the received files, anything that counts focus goes through `focusDevices`).
 */
object Ps2bSelfCheck {
    private val zone = ZoneOffset.ofHours(-5)
    private val t0: Instant = Instant.parse("2026-11-16T17:00:00Z") // 12:00 local
    private fun at(minutes: Long): Instant = t0.plusSeconds(minutes * 60)
    private val min = 60_000L
    private const val PROFILE_ID = "profile-id-secret-77"
    private const val GUID_A = "aaaaaaaa-0000-0000-0000-000000000001"
    private const val GUID_B = "aaaaaaaa-0000-0000-0000-000000000002"

    fun run(report: CheckReport) {
        writer(report)
        counting(report)
        cadence(report)
        unchecked(report)
        history(report)
        boundedWait(report)
        asideFailure(report)
        sendStatus(report)
        stamp(report)
        lateTime(report)
        qr(report)
        gate(report)
        sources(report)
    }

    // --- helpers ----------------------------------------------------------------------------------------------------------------

    private fun record(
        id: String, start: Instant, end: Instant?, away: List<PhoneAway> = emptyList(), isTest: Boolean = false,
        tasks: List<SessionTaskItem> = emptyList(), setEnd: Instant? = null,
    ) = PhoneSessionRecord(id, PROFILE_ID, "Deep work", start, start, setEnd, isTest = isTest, away = away, checklist = PhoneSessionChecklist(tasks), endedAt = end)

    private fun input(
        running: PhoneSessionRecord? = null, log: List<PhoneSessionRecord> = emptyList(), unreadable: Boolean = false, history: Instant? = null,
        runningUnchecked: Boolean = false, uncheckedEnded: Set<String> = emptySet(), now: Instant = at(60), stampable: Boolean = true,
    ) = PhoneSessionSyncInput(running, log, unreadable, history, runningUnchecked, uncheckedEnded, now, stampable, zone)

    private fun card(id: UUID = UUID(0, 5), kind: String = SyncDeviceCard.KIND_PHONE, writtenAt: Instant = at(60)) =
        SyncDeviceCard(id, "Pixel", kind, "0.1.78", writtenAt, emptyList(), false, emptyMap())

    private fun fileOf(part: PhoneSessionSyncPart): SyncFile =
        SyncFile(1, card(writtenAt = part.writtenAt), part.sessions, emptyList(), null, emptyList(), part.coversFrom, running = part.running)

    private fun task(id: String, title: String = "Task", localTo: String? = null) = SessionTaskItem(id, title, null, emptyList(), localTo = localTo)

    // --- the writer -------------------------------------------------------------------------------------------------------------

    private fun writer(report: CheckReport) {
        val ended = record("e1", at(0), at(25), tasks = listOf(task(GUID_A, "Write the report")))
        val running = record("r1", at(40), null, tasks = listOf(task(GUID_B, "Plan the week")), setEnd = at(100))
        val part = PhoneSessionSyncWriter.forFile(input(running, listOf(ended)), at(60))
        report.check("ps2b/writer: a running session writes `running` (profile name, phase, set end, the session's start) and its sessions (the ended one whole, the running one open)",
            part.running?.profile == "Deep work" && part.running?.phaseEndsAt == at(100) && part.running?.sessionStartedAt == at(40) && part.running?.isBreak == false &&
                part.sessions.size == 2 && part.sessions[0].end == at(25) && part.sessions[1].end == null && part.sessions[1].start == at(40), "$part")
        val text = SyncFileJson.write(fileOf(part)).toString()
        report.check("ps2b/writer (R14): the file carries the profile's NAME and the checklist's text, never the profile id", text.contains("Deep work") && !text.contains(PROFILE_ID))
        report.check("ps2b/writer (R14): `running` has no profile id, no planning fields, no offSince and no break", part.running!!.profileId == null && part.running!!.countUpProfileId == null && part.running!!.offSince == null && !part.running!!.isBreak)
        report.check("ps2b/writer: the open piece carries the checklist, and so does the first (PS1a follow-up)", part.sessions[1].tasks.map { it.id } == listOf(GUID_B) && part.sessions[0].tasks.map { it.id } == listOf(GUID_A))

        // B4: the file's time is the session clock's; a wall clock far from it changes nothing.
        val far = PhoneSessionSyncWriter.forFile(input(running, listOf(ended), now = at(60)), at(60 + 600))
        val back = PhoneSessionSyncWriter.forFile(input(running, listOf(ended), now = at(60)), at(60 - 600))
        report.check("ps2b/writer (B4): the file is stamped with the session clock, not the phone's wall clock (a clock 10 h ahead or behind changes nothing)", far.writtenAt == at(60) && back.writtenAt == at(60) && far == back)

        // B5: an unreadable log is not an empty one.
        val unreadable = PhoneSessionSyncWriter.forFile(input(running, listOf(ended), unreadable = true), at(60))
        report.check("ps2b/writer (B5): an unreadable log writes no `running` and no sessions, and coversFrom = the time written (receivers keep what they hold)",
            unreadable.running == null && unreadable.sessions.isEmpty() && unreadable.coversFrom == unreadable.writtenAt)
        val after = PhoneSessionSyncWriter.forFile(input(null, listOf(ended), history = at(30)), at(60))
        report.check("ps2b/writer (B5, N2): after a lost log started again, coversFrom is never earlier than its history start", after.coversFrom == at(30) && after.sessions.isNotEmpty())
        val untouched = PhoneSessionSyncWriter.forFile(input(null, emptyList()), at(60))
        report.check("ps2b/writer: a phone with no session at all says it speaks for the last 90 days, never null, never after the time written", untouched.coversFrom == at(60).minus(Duration.ofDays(90)) && untouched.sessions.isEmpty() && untouched.running == null)

        // An unchecked clock (Android 13 and later, before the network time): nothing about sessions at all, stamped with the phone's trusted time.
        val silent = PhoneSessionSyncWriter.forFile(input(running, listOf(ended), stampable = false, now = at(60 + 7)), at(60))
        report.check("ps2b/writer (B3b): a clock that can't stamp yet writes no sessions, no `running`, coversFrom = the time written, and uses the phone's own trusted time",
            silent.running == null && silent.sessions.isEmpty() && silent.coversFrom == silent.writtenAt && silent.writtenAt == at(60))

        // V-N1: a session whose times are not corrected yet is not sent.
        val uncheckedRunning = PhoneSessionSyncWriter.forFile(input(running, listOf(ended), runningUnchecked = true), at(60))
        report.check("ps2b/writer (V-N1): a running session started on an unchecked clock is left out (no `running`, no pieces); the checked ended one still goes",
            uncheckedRunning.running == null && uncheckedRunning.sessions.size == 1 && uncheckedRunning.sessions[0].start == at(0))
        val uncheckedEnded = PhoneSessionSyncWriter.forFile(input(null, listOf(ended, record("e2", at(30), at(35))), uncheckedEnded = setOf("e1")), at(60))
        report.check("ps2b/writer (V-N1): an ended session marked unchecked is left out, the others go", uncheckedEnded.sessions.map { it.start } == listOf(at(30)))
        report.check("ps2b/writer (V-N1): the phone still counts the unchecked ones itself", PhoneSessionSyncWriter.forCounting(input(running, listOf(ended), runningUnchecked = true, uncheckedEnded = setOf("e1")))!!.sessions.size == 2)

        // A test session never goes.
        val test = record("t1", at(40), null, isTest = true)
        val withTest = PhoneSessionSyncWriter.forFile(input(test, listOf(ended)), at(60))
        report.check("ps2b/writer: a running TEST session writes no `running` and no piece", withTest.running == null && withTest.sessions.size == 1 && withTest.sessions[0].start == at(0))
        report.check("ps2b/writer: a phone with only a test session counts nothing of its own", PhoneSessionSyncWriter.forCounting(input(test)) == null)

        // B11: ids and local-only tasks.
        val mixed = record("m1", at(0), at(20), tasks = listOf(task(GUID_A, "Shared"), task("not-a-guid", "Odd"), task(GUID_B, "Mine only", localTo = "device-1")))
        val mixedPart = PhoneSessionSyncWriter.forFile(input(null, listOf(mixed)), at(60))
        val written = mixedPart.sessions.single().tasks
        report.check("ps2b/writer (B11): a task with a non-GUID id is left out (the PC would refuse the whole file); a task kept local to a device goes as its stub (no text, localTo)",
            written.map { it.id } == listOf(GUID_A, GUID_B) && written[1].title == "" && written[1].localTo == "device-1" && written[0].title == "Shared")
        report.check("ps2b/writer (B11): the text of a local-only task is not in the file", !SyncFileJson.write(fileOf(mixedPart)).toString().contains("Mine only"))

        // ADR 77 / PS1: what `awaySince` says.
        val paused = record("p1", at(40), null, away = listOf(PhoneAway(PhoneAwayKind.PAUSED, at(50), null)))
        val stepped = record("s1", at(40), null, away = listOf(PhoneAway(PhoneAwayKind.STEP_AWAY, at(50), null)))
        report.check("ps2b/writer (ADR 77): Pause timer carries `awaySince`, Step away does not",
            PhoneSessionSyncWriter.forFile(input(paused), at(60)).running?.awaySince == at(50) && PhoneSessionSyncWriter.forFile(input(stepped), at(60)).running?.awaySince == null)
        report.check("ps2b/writer: a stretch away going now cuts the piece before it where it began, then an empty open piece (the paused time is never counted)",
            PhoneSessionSyncWriter.forFile(input(paused), at(60)).sessions.let { it.size == 2 && it[0].end == at(50) && it[1].end == null && it[1].start == at(60) })
        report.check("ps2b/writer: every part of the part is consistent: coversFrom never after writtenAt",
            listOf(part, far, unreadable, after, untouched, silent, uncheckedRunning, uncheckedEnded, withTest, mixedPart).all { !it.coversFrom.isAfter(it.writtenAt) })
    }

    // --- counting ---------------------------------------------------------------------------------------------------------------

    private fun pcFile(sessions: List<SyncSession>, writtenAt: Instant = at(120)) =
        ReceivedDevice(SyncFile(1, card(UUID(0, 1), SyncDeviceCard.KIND_PC, writtenAt), sessions, emptyList(), null, emptyList()), writtenAt, "folder", ByteArray(0))

    private fun focusMinutes(devices: List<ReceivedDevice>, now: Instant): Long {
        val windows = FocusWindows.of(devices.map { ReceivedSyncFile(it.file, it.receivedAt) }, zone, now)
        val day = FocusWindows.daysIn(windows, java.time.LocalDate.of(2026, 11, 16), java.time.LocalDate.of(2026, 11, 16)).single()
        return day.focused.toMinutes()
    }

    private fun counting(report: CheckReport) {
        val me = UUID(0, 5)
        report.check("ps2b/counting: a phone with no session adds nothing (works alone: the list is the received files as they are)",
            PhoneSessionFiles.ownDevice(input(), me, "Pixel", "1") == null && PhoneSessionFiles.focusFiles(listOf(pcFile(emptyList())), null).size == 1)

        // R4: the PC ran 12:00-13:00 and the phone 12:30-13:30: 90 minutes, not 120.
        val pc = pcFile(listOf(SyncSession(at(0), at(60), "Deep work", false)))
        val own = PhoneSessionFiles.ownDevice(input(null, listOf(record("o1", at(30), at(90)))), me, "Pixel", "1")!!
        val stale0 = pcFile(emptyList(), at(7))
        report.check("ps2b/counting (R4): the phone's own session and an overlapping PC session count the overlap once (90 minutes, not 120)", focusMinutes(PhoneSessionFiles.focusFiles(listOf(pc), own), at(120)) == 90L)
        report.check("ps2b/counting: the phone's own session alone counts", focusMinutes(PhoneSessionFiles.focusFiles(emptyList(), own), at(120)) == 60L)
        report.check("ps2b/counting: the own device is the phone itself, a phone, named as it names itself", own.file.device.deviceId == me && own.file.device.kind == SyncDeviceCard.KIND_PHONE && own.file.device.name == "Pixel")

        // B4: a running own session's open piece ends at the session clock's time, whatever the reader's clock says.
        val running = record("r1", at(60), null)
        val live = PhoneSessionFiles.ownDevice(input(running, now = at(90)), me, "Pixel", "1")!!
        report.check("ps2b/counting: a running own session counts up to the session's time now (30 minutes)", focusMinutes(listOf(live), at(90)) == 30L)
        report.check("ps2b/counting (B1, B4): a reader whose clock is 3 hours ahead does not make it longer: the open piece ends at the session clock's stamp", focusMinutes(listOf(live), at(90 + 180)) == 30L)
        report.check("ps2b/counting: a reader whose clock is earlier counts only to its own now", focusMinutes(listOf(live), at(75)) == 15L)
        val pausedNow = PhoneSessionFiles.ownDevice(input(record("r2", at(60), null, away = listOf(PhoneAway(PhoneAwayKind.PAUSED, at(80), null))), now = at(100)), me, "Pixel", "1")!!
        report.check("ps2b/counting: a paused own session counts only to the pause (20 minutes of 40)", focusMinutes(listOf(pausedNow), at(100)) == 20L)

        // The arrival time of each file (PS2b): the list's own arrival, or the stamp for a file that is not in it.
        val lateCopy = ReceivedDevice(pc.file, at(333), "folder", ByteArray(0))
        val arrival = listOf(lateCopy, own).arrivalOf()
        report.check("ps2b/counting: arrivalOf gives each file's arrival in the list (a skewed stamp is judged by it), and the stamp for a file that is not in it",
            arrival(pc.file) == at(333) && arrival(own.file) == own.receivedAt && arrival(stale0.file) == stale0.file.device.writtenAt)

        // Replaced, never doubled.
        val stale = ReceivedDevice(own.file.copy(sessions = listOf(SyncSession(at(0), at(500), "x", false))), at(1), "folder", ByteArray(0))
        val merged = PhoneSessionFiles.focusFiles(listOf(stale, pc), own)
        report.check("ps2b/counting: a received copy of this phone's own file is replaced by the live own sessions, never counted twice", merged.count { it.file.device.deviceId == me } == 1 && merged.last() === own && merged.size == 2)

        // The 90-day log counts, not only the file's 14 days.
        val old = record("old", at(0).minus(Duration.ofDays(60)), at(30).minus(Duration.ofDays(60)))
        val ownOld = PhoneSessionFiles.ownDevice(input(null, listOf(old), now = at(60)), me, "Pixel", "1")!!
        report.check("ps2b/counting: the phone's own counting keeps its whole 90-day log (a session 60 days ago is there, though the file speaks for 14 days)", ownOld.file.sessions.size == 1)
        val fileOld = PhoneSessionSyncWriter.forFile(input(null, listOf(old), now = at(60)), at(60))
        report.check("ps2b/writer: while the FILE carries only the last 14 days (and says so with coversFrom)", fileOld.sessions.isEmpty() && fileOld.coversFrom == at(60).minus(Duration.ofDays(14)).atZone(zone).toLocalDate().atStartOfDay(zone).toInstant())
    }

    // --- B13: the 5-minute write ------------------------------------------------------------------------------------------------

    private fun gaps(world: World): List<Long> = world.ports.syncWriteAt.zipWithNext { a, b -> b - a }

    private fun cadence(report: CheckReport) {
        val w = World()
        w.controller.start("p", "Focus", 120)
        val atStart = w.ports.syncWrites
        report.check("ps2b/cadence (B13): the start asks for the file at once", atStart >= 1, "$atStart")
        // A refresh every minute, 3 minutes: nothing more is asked (the state did not change).
        repeat(3) {
            w.source.advance(min)
            w.controller.refresh()
        }

        report.check("ps2b/cadence (B13): refreshes within 4 minutes do not ask again (nothing changed)", w.ports.syncWrites == atStart, "${w.ports.syncWrites} vs $atStart")
        // 40 more minutes, a refresh every minute (the alarm and the notification both call refresh): never longer than 5 minutes between asks.
        repeat(40) {
            w.source.advance(min)
            w.controller.refresh()
        }

        val gaps = gaps(w)
        report.check("ps2b/cadence (B13): over 43 minutes the file is asked for at least every 5 minutes (largest gap ${gaps.maxOrNull()?.div(min)} min)", gaps.isNotEmpty() && gaps.all { it <= 5 * min }, "$gaps")
        report.check("ps2b/cadence (B13): and not wastefully (about every 4 minutes: ${w.ports.syncWrites} asks in 43 minutes)", w.ports.syncWrites in 9..14, "${w.ports.syncWrites}")

        // A paused session is still running: it keeps writing.
        val p = World()
        p.controller.start("p", "Focus", 120)
        p.source.advance(5 * min)
        p.controller.pause()
        val beforePause = p.ports.syncWrites
        repeat(30) {
            p.source.advance(min)
            p.controller.refresh()
        }

        report.check("ps2b/cadence (B13): a paused session keeps writing (R2 keeps a paused session live by it)", p.ports.syncWrites - beforePause >= 6 && gaps(p).takeLast(p.ports.syncWrites - beforePause).all { it <= 5 * min }, "${gaps(p)}")

        // A test session is never asked for; the end is.
        val t = World()
        t.controller.start("p", "Focus", 25, isTest = true)
        repeat(20) {
            t.source.advance(min)
            t.controller.refresh()
        }

        report.check("ps2b/cadence: a test session never asks for the file", t.ports.syncWrites == 0, "${t.ports.syncWrites}")
        val e = World()
        e.controller.start("p", "Focus", 25)
        e.source.advance(26 * min)
        e.controller.tick()
        val ended = e.ports.syncWrites
        report.check("ps2b/cadence: the end asks for the file (the ended session goes at once)", e.controller.running == null && ended >= 2, "${ended}")
        repeat(10) {
            e.source.advance(min)
            e.controller.refresh()
        }

        report.check("ps2b/cadence: with no session running a refresh asks for nothing", e.ports.syncWrites == ended)
        val saved = e.ports.store.read()
        report.check("ps2b/cadence: the file is asked for AFTER the save (it is written from the saved session)", saved is PhoneSessionRead.Readable && e.ports.logSizeAtLastSyncWrite == 1, "${e.ports.logSizeAtLastSyncWrite}")

        // A restart of the app: the first refresh asks at once.
        val r = World()
        r.controller.start("p", "Focus", 120)
        r.source.advance(10 * min)
        val restarted = r.restart()
        val before = r.ports.syncWrites
        restarted.refresh()
        report.check("ps2b/cadence (B13): a process that starts again asks for the file at its first refresh", r.ports.syncWrites > before)
        report.check("ps2b/cadence (B13, S7): the waking alarm that carries it is every 5 minutes", r.ports.alarms[com.accountabilibuddy.companion.session.PhoneAlarmKind.HEARTBEAT] == 5 * min)
    }

    // --- V-N1 -------------------------------------------------------------------------------------------------------------------

    private fun unchecked(report: CheckReport) {
        // Android 13 and later, no network time yet: the phone's clock is wrong by 2 hours (the real time is t0 + 2 h).
        val offline = World(FakeSource(network = false, canCome = true))
        offline.controller.start("p", "Focus", 25)
        val started = offline.controller.syncInput()
        report.check("ps2b/unchecked (V-N1): a session started before the network time is marked unchecked, and the clock can't stamp the file", started.runningUnchecked && !started.stampable)
        val silent = PhoneSessionSyncWriter.forFile(started, offline.source.wall)
        report.check("ps2b/unchecked (V-N1): so the file says nothing about it", silent.running == null && silent.sessions.isEmpty() && silent.coversFrom == silent.writtenAt)
        // The network time arrives: the real time is 2 hours later.
        offline.source.advance(3 * min)
        offline.source.wall = offline.source.wall.plus(Duration.ofHours(2))
        offline.source.network = true
        val writesBefore = offline.ports.syncWrites
        offline.controller.refresh()
        val corrected = offline.controller.syncInput()
        report.check("ps2b/unchecked (V-N1): the network time corrects the session (start +2 h), it is checked and the file may say it", !corrected.runningUnchecked && corrected.stampable && corrected.running!!.startedAt == at(120))
        report.check("ps2b/unchecked (V-N1): and the file is asked for at once", offline.ports.syncWrites > writesBefore)
        report.check("ps2b/unchecked (V-N1): the file then carries it", PhoneSessionSyncWriter.forFile(corrected, offline.source.wall).running?.sessionStartedAt == at(120))

        // An ended session: marked, left out of the file, counted by the phone, corrected at the network time (same process).
        val ended = World(FakeSource(network = false, canCome = true))
        ended.controller.start("p", "Focus", 5)
        ended.source.advance(6 * min)
        ended.controller.tick()
        val endedInput = ended.controller.syncInput()
        val id = ended.controller.log.single().id
        report.check("ps2b/unchecked (V-N1): a session that started on an unchecked clock and ended there is marked", ended.controller.running == null && endedInput.uncheckedEnded == setOf(id))
        report.check("ps2b/unchecked (V-N1): it counts on this phone", PhoneSessionSyncWriter.forCounting(endedInput)!!.sessions.size == 1)
        ended.source.advance(2 * min)
        ended.source.wall = ended.source.wall.plus(Duration.ofHours(2))
        ended.source.network = true
        val writes = ended.ports.syncWrites
        ended.controller.refresh()
        val fixed = ended.controller.syncInput()
        report.check("ps2b/unchecked (V-N1): the network time corrects it (moved 2 h later), the mark is gone and the file is asked for", fixed.uncheckedEnded.isEmpty() && ended.controller.log.single().startedAt == at(120) && ended.ports.syncWrites > writes, "${ended.controller.log.single().startedAt}")
        report.check("ps2b/unchecked (V-N1): the file then carries it", PhoneSessionSyncWriter.forFile(fixed, ended.source.wall).sessions.size == 1)

        // A process that dies before the network time arrives (Q1181): the anchor is saved with the mark, so the same boot can still correct it.
        val died = World(FakeSource(network = false, canCome = true))
        died.controller.start("p", "Focus", 5)
        died.source.advance(6 * min)
        died.controller.tick()
        val marksOnDisk = (died.ports.store.read() as PhoneSessionRead.Readable).data.uncheckedEnded
        report.check("ps2b/unchecked (Q1181): the mark is saved with the anchor its times were written on", marksOnDisk.size == 1 && marksOnDisk[0].anchor != null && marksOnDisk[0].id == died.controller.log.single().id)
        val restarted = died.restart()
        died.source.advance(2 * min)
        died.source.wall = died.source.wall.plus(Duration.ofHours(2))
        died.source.network = true
        restarted.refresh()
        report.check("ps2b/unchecked (Q1181): after the process died, the network time in the same boot still corrects it (+2 h, by its own anchor) and the file may say it",
            restarted.log.single().startedAt == at(120) && restarted.syncInput().uncheckedEnded.isEmpty(), "${restarted.log.single().startedAt}")

        // A reboot between loses the correction: a record from an earlier boot stays out of the file for good (Q1181).
        val rebooted = World(FakeSource(network = false, canCome = true))
        rebooted.controller.start("p", "Focus", 5)
        rebooted.source.advance(6 * min)
        rebooted.controller.tick()
        val rc = rebooted.rebootAndStart(Duration.ofMinutes(1), networkAfter = true)
        rebooted.source.advance(6_000_000L) // this boot has run longer than the old anchor's elapsed time: only the boot count tells them apart
        rc.refresh()
        report.check("ps2b/unchecked (Q1181): a record from an earlier boot is never corrected and stays out of the file", rc.syncInput().uncheckedEnded.size == 1 && rc.log.single().startedAt == at(0))
        // The same for a RUNNING session that crossed a reboot: its start (written on the earlier boot) stays unchecked, whatever the new clock says.
        val runAcross = World(FakeSource(network = false, canCome = true))
        runAcross.controller.start("p", "Focus", 600)
        val rr = runAcross.rebootAndStart(Duration.ofMinutes(1), networkAfter = true)
        rr.refresh()
        report.check("ps2b/unchecked (N2): a running session whose start was written in an earlier boot is still unchecked after the reboot: not sent, never shifted by the new boot's correction",
            rr.syncInput().runningUnchecked && rr.running!!.startedAt == at(0), "${rr.syncInput().runningUnchecked} ${rr.running?.startedAt}")

        val runAcross2 = World(FakeSource(network = false, canCome = true))
        runAcross2.controller.start("p", "Focus", 600)
        val rr2 = runAcross2.rebootAndStart(Duration.ofMinutes(1), networkAfter = false)
        runAcross2.source.advance(2 * min)
        runAcross2.source.wall = runAcross2.source.wall.plus(Duration.ofHours(2))
        runAcross2.source.network = true
        rr2.refresh()
        report.check("ps2b/unchecked (N2): the network time of a LATER boot never moves a start written in an earlier one",
            rr2.running!!.startedAt == at(0) && rr2.syncInput().runningUnchecked, "${rr2.running?.startedAt}")

        // Two sessions on two anchors of ONE boot, the clock moved in between: each is corrected by its OWN anchor, even after the new start.
        val moved = World(FakeSource(network = false, canCome = true))
        moved.controller.start("p", "Focus", 5)
        moved.source.advance(6 * min)
        moved.controller.tick()
        moved.source.wall = moved.source.wall.plus(Duration.ofHours(1))
        moved.controller.start("p", "Focus", 5)
        moved.source.advance(6 * min)
        moved.controller.tick()
        val secondStart = moved.controller.log.last().startedAt
        moved.source.advance(min)
        moved.source.wall = moved.source.wall.plus(Duration.ofHours(1))
        moved.source.network = true
        moved.controller.refresh()
        report.check("ps2b/unchecked (Q1181): two sessions on two anchors of one boot are each corrected by their own anchor (+2 h and +1 h), and both go out",
            moved.controller.log.first().startedAt == at(120) && moved.controller.log.last().startedAt == secondStart.plus(Duration.ofHours(1)) && moved.controller.syncInput().uncheckedEnded.isEmpty(),
            "${moved.controller.log.map { it.startedAt }} ${moved.controller.syncInput().uncheckedEnded}")

        // The network time arriving corrects a RUNNING session by a few seconds: the file is asked for at that moment (not left to the 4-minute rule).
        val small = World(FakeSource(network = false, canCome = true))
        small.controller.start("p", "Focus", 60)
        small.source.advance(min)
        small.source.wall = small.source.wall.plus(Duration.ofSeconds(30))
        small.source.network = true
        val smallBefore = small.ports.syncWrites
        small.controller.refresh()
        report.check("ps2b/unchecked (V-N1): the correction of a running session asks for the file at once, even when the last ask was a minute ago", small.ports.syncWrites == smallBefore + 1 && !small.controller.syncInput().runningUnchecked, "${small.ports.syncWrites} vs $smallBefore")

        // S1: a session that STARTED on a checked clock and ended in a hold (a reboot onto a clock that can't be checked) is safe: its focus pieces are all trusted.
        val endHold = World(FakeSource(network = true, canCome = true))
        endHold.ports.mode = com.accountabilibuddy.shared.session.PhoneSessionMode.PRODUCTA_BUDDY
        endHold.controller.start("p", "Focus", 60)
        endHold.source.advance(5 * min)
        endHold.controller.refresh() // saves the heartbeat: the hold's off moment is 12:05
        val held = endHold.rebootAndStart(Duration.ofMinutes(1), networkAfter = false)
        held.endEarly(null)
        report.check("ps2b/unchecked (S1): a session that started on a checked clock and ended in a hold is NOT marked (nothing to correct, nothing to withhold)",
            held.running == null && held.log.size == 1 && held.syncInput().uncheckedEnded.isEmpty(), "${held.running} ${held.log.size} ${held.syncInput().uncheckedEnded}")
        endHold.source.advance(2 * min)
        endHold.source.network = true
        held.refresh()
        val heldPart = PhoneSessionSyncWriter.forFile(held.syncInput(), endHold.source.wall)
        report.check("ps2b/unchecked (S1): once the clock is checked again the file carries it, so receivers keep the pieces they already hold", heldPart.sessions.size == 1 && heldPart.sessions[0].start == at(0) && heldPart.coversFrom.isBefore(at(0)), "${heldPart.sessions.size}")

        // Below Android 13 no network time ever comes: nothing there is "unchecked".
        val old = World(FakeSource(network = false, canCome = false))
        old.controller.start("p", "Focus", 5)
        val oldRunning = old.controller.syncInput()
        old.source.advance(6 * min)
        old.controller.tick()
        val oldEnded = old.controller.syncInput()
        report.check("ps2b/unchecked: below Android 13 a session is not 'unchecked' (no network time ever comes): it writes, and stamps with its own clock", !oldRunning.runningUnchecked && oldRunning.stampable && oldEnded.uncheckedEnded.isEmpty())

        // Trusted all along: nothing is marked.
        val ok = World()
        ok.controller.start("p", "Focus", 5)
        ok.source.advance(6 * min)
        ok.controller.tick()
        report.check("ps2b/unchecked: a session on a checked clock is never marked", ok.controller.syncInput().uncheckedEnded.isEmpty() && ok.controller.syncInput().stampable)
    }

    // --- B1: an unreadable or lost log never turns into a file that erases history ----------------------------------------------------

    private fun sessionsFile(world: World) = java.io.File(world.dir, "session/" + com.accountabilibuddy.companion.session.PhoneSessionStore.SESSIONS_FILE)

    // A phone that ran one session and then lost its log (the file is damaged); returns the world with the damaged file in place.
    private fun withDamagedLog(source: FakeSource): World {
        val w = World(source)
        w.controller.start("p", "Focus", 5)
        w.source.advance(6 * min)
        w.controller.tick()
        sessionsFile(w).writeText("garbage")
        return w
    }

    private fun history(report: CheckReport) {
        // Android 13 and later, the log is damaged at a start before the network time: silent, and STILL silent after a restart (the state is saved).
        val a = withDamagedLog(FakeSource(network = false, canCome = true))
        val c1 = a.restart()
        val first = PhoneSessionSyncWriter.forFile(c1.syncInput(), a.source.wall)
        report.check("ps2b/history (B1): a log that can't be opened writes a silent file (no sessions, coversFrom = the time written)", c1.logUnreadable && first.sessions.isEmpty() && first.running == null && first.coversFrom == first.writtenAt)
        val saved = (a.ports.store.read() as PhoneSessionRead.Readable).data
        report.check("ps2b/history (B1): and 'history unknown' is SAVED with the new log (no history start can be set yet)", saved.historyUnknown && saved.historyStartsAt == null)
        val c2 = a.restart()
        val second = PhoneSessionSyncWriter.forFile(c2.syncInput(), a.source.wall)
        report.check("ps2b/history (B1): after a restart the file is still silent: coversFrom = the time written, never now minus 90 days", c2.logUnreadable && second.coversFrom == second.writtenAt && second.sessions.isEmpty(), "${second.coversFrom} ${second.writtenAt}")
        a.source.advance(3 * min)
        a.source.network = true
        c2.refresh()
        val third = PhoneSessionSyncWriter.forFile(c2.syncInput(), a.source.wall)
        report.check("ps2b/history (B1): the network time arriving sets the history start (saved, 'unknown' cleared): the file speaks from it, never earlier",
            !c2.logUnreadable && c2.historyStartsAt != null && third.coversFrom == c2.historyStartsAt && (a.ports.store.read() as PhoneSessionRead.Readable).data.let { !it.historyUnknown && it.historyStartsAt == c2.historyStartsAt })
        val c3 = a.restart()
        report.check("ps2b/history (B1): and that start is kept across a restart", !c3.logUnreadable && c3.historyStartsAt == c2.historyStartsAt)

        // Below Android 13 no network time ever comes: the history starts at once, a restart is safe, the file is not silent for ever.
        val b = withDamagedLog(FakeSource(network = false, canCome = false))
        val d1 = b.restart()
        val d1Part = PhoneSessionSyncWriter.forFile(d1.syncInput(), b.source.wall)
        report.check("ps2b/history (B1): below Android 13 the history start is set at once", !d1.logUnreadable && d1.historyStartsAt != null && d1Part.coversFrom == d1.historyStartsAt, "${d1.historyStartsAt}")
        val b2 = withDamagedLog(FakeSource(network = false, canCome = false))
        val built = com.accountabilibuddy.companion.session.PhoneSessionController(b2.ports)
        val builtSaved = (b2.ports.store.read() as PhoneSessionRead.Readable).data
        report.check("ps2b/history (B1): below Android 13 the history start is saved by the very start from a lost log (not by a later call)", built.historyStartsAt != null && builtSaved.historyStartsAt == built.historyStartsAt && !builtSaved.historyUnknown)
        val d2 = b.restart()
        val d2Part = PhoneSessionSyncWriter.forFile(d2.syncInput(), b.source.wall)
        report.check("ps2b/history (B1): and after a restart too: coversFrom = the history start, never now minus 90 days", d2.historyStartsAt == d1.historyStartsAt && d2Part.coversFrom == d1.historyStartsAt && d2Part.coversFrom.isAfter(d2Part.writtenAt.minus(Duration.ofDays(90))))

        // A missing file next to a set-aside copy is a LOST log (the write after the copy failed), not a new phone.
        val m = withDamagedLog(FakeSource(network = false, canCome = true))
        m.restart() // sets the damaged file aside and writes the new one
        sessionsFile(m).delete()
        val mc = m.restart()
        val mPart = PhoneSessionSyncWriter.forFile(mc.syncInput(), m.source.wall)
        report.check("ps2b/history (B1): a missing log with a set-aside copy beside it is unreadable, not empty: the file is silent and 'unknown' is saved again",
            mc.logUnreadable && mPart.coversFrom == mPart.writtenAt && (m.ports.store.read() as PhoneSessionRead.Readable).data.historyUnknown)
        val mOld = withDamagedLog(FakeSource(network = false, canCome = false))
        mOld.restart()
        sessionsFile(mOld).delete()
        val moc = mOld.restart()
        report.check("ps2b/history (B1): the same below Android 13: the history starts at once", !moc.logUnreadable && moc.historyStartsAt != null)

        // A phone that never had a log has nothing to protect: no aside copy, no flag.
        val fresh = World(FakeSource(network = false, canCome = true))
        report.check("ps2b/history: a phone with no log and no set-aside copy is a new phone: not unreadable, nothing saved", !fresh.controller.logUnreadable && fresh.controller.historyStartsAt == null && !sessionsFile(fresh).exists())
    }

    // --- V-S1: the receiver is never held past its limit ---------------------------------------------------------------------------

    private fun boundedWait(report: CheckReport) {
        val finished = java.util.concurrent.atomic.AtomicBoolean(false)
        val started = System.nanoTime()
        val inTime = com.accountabilibuddy.companion.session.PhoneSessionBoundedWait.run(300) {
            Thread.sleep(3_000) // a pass blocked on a socket: nothing can interrupt it
            finished.set(true)
        }
        val tookMs = (System.nanoTime() - started) / 1_000_000
        report.check("ps2b/wait (V-S1): a pass that blocks for 3 s is waited for 0.3 s only: the receiver is released, the pass goes on by itself", !inTime && tookMs < 1_500 && !finished.get(), "$inTime ${tookMs}ms")
        var ran = false
        report.check("ps2b/wait (V-S1): a quick pass is waited for to its end", com.accountabilibuddy.companion.session.PhoneSessionBoundedWait.run(2_000) { ran = true } && ran)
        report.check("ps2b/wait (V-S1): a pass that throws does not throw into the receiver", com.accountabilibuddy.companion.session.PhoneSessionBoundedWait.run(2_000) { error("network") })
    }

    // --- V-N1: an unreadable log that can't be set aside is never written over -----------------------------------------------------

    private class NoAsideStore(sessions: java.io.File, reasons: java.io.File, sealer: com.accountabilibuddy.companion.session.PhoneSessionSealer) :
        com.accountabilibuddy.companion.session.PhoneSessionStore(sessions, reasons, sealer) {
        override fun setAsideUnreadable(): Boolean = false
    }

    private class StorePorts(private val base: Ps2aSelfCheck.FakePorts, override val store: com.accountabilibuddy.companion.session.PhoneSessionStore) :
        com.accountabilibuddy.companion.session.PhoneSessionPorts by base

    private fun asideFailure(report: CheckReport) {
        val w = withDamagedLog(FakeSource(network = false, canCome = false))
        val file = sessionsFile(w)
        val failing = NoAsideStore(file, java.io.File(w.dir, "session/" + com.accountabilibuddy.companion.session.PhoneSessionStore.REASONS_FILE), Ps2aSelfCheck.AesSealer())
        val c = com.accountabilibuddy.companion.session.PhoneSessionController(StorePorts(w.ports, failing))
        report.check("ps2b/aside (V-N1): when the unreadable log can't be set aside, the original is left alone and this process stays silent", file.readText() == "garbage" && c.logUnreadable)
        val start = c.start("p", "Focus", 25)
        c.refresh()
        report.check("ps2b/aside (V-N1): nothing is ever written over it (a session can't start, a refresh saves nothing)", start is com.accountabilibuddy.companion.session.PhoneSessionStart.StoreFailed && file.readText() == "garbage")
        // On 13+ the network time arriving would set the history start and save it: with the original still unsaved-over, that save must not happen either.
        val w13 = withDamagedLog(FakeSource(network = false, canCome = true))
        val file13 = sessionsFile(w13)
        val failing13 = NoAsideStore(file13, java.io.File(w13.dir, "session/" + com.accountabilibuddy.companion.session.PhoneSessionStore.REASONS_FILE), Ps2aSelfCheck.AesSealer())
        val c13 = com.accountabilibuddy.companion.session.PhoneSessionController(StorePorts(w13.ports, failing13))
        w13.source.advance(min)
        w13.source.network = true
        c13.refresh()
        report.check("ps2b/aside (V-N1): not even the history start is saved over it when the network time arrives", file13.readText() == "garbage")
        val later = w.restart()
        report.check("ps2b/aside (V-N1): a later start that CAN set it aside recovers: the copy is kept and a new log starts", java.io.File(file.parentFile, file.name + ".unreadable").readText() == "garbage" && !later.logUnreadable && later.historyStartsAt != null)
    }

    // --- V-N3: what Troubleshoot says -----------------------------------------------------------------------------------------------

    private fun sendStatus(report: CheckReport) {
        val same = World(FakeSource(network = false, canCome = true))
        same.controller.start("p", "Focus", 5)
        same.source.advance(6 * min)
        same.controller.tick()
        val s1 = same.controller.sendStatus()
        report.check("ps2b/status (V-N3): a record of this boot that waits for network time says 'wait'", s1.waiting && !s1.lost)
        val earlier = World(FakeSource(network = false, canCome = true))
        earlier.controller.start("p", "Focus", 5)
        earlier.source.advance(6 * min)
        earlier.controller.tick()
        val later = earlier.rebootAndStart(Duration.ofMinutes(1), networkAfter = true)
        val s2 = later.sendStatus()
        report.check("ps2b/status (V-N3): a record from an earlier boot says it can't be sent, not 'wait'", !s2.waiting && s2.lost)
        val fine = World()
        fine.controller.start("p", "Focus", 5)
        report.check("ps2b/status (V-N3): a checked session says nothing", !fine.controller.sendStatus().waiting && !fine.controller.sendStatus().lost)
    }

    // --- S2: the file's time ----------------------------------------------------------------------------------------------------

    private fun stamp(report: CheckReport) {
        val running = record("r1", at(40), null)
        val noSession = PhoneSessionSyncWriter.forFile(input(null, listOf(record("e1", at(0), at(25))), now = at(60 + 400)), at(60))
        report.check("ps2b/stamp (S2): with no session running the file is stamped with the phone's trusted time, not the session clock (a clock fixed after the app started leaves no stamp ahead)",
            noSession.writtenAt == at(60) && noSession.sessions.isNotEmpty())
        val withSession = PhoneSessionSyncWriter.forFile(input(running, now = at(60 + 400)), at(60))
        report.check("ps2b/stamp (S2): while a real session runs it is the session clock's (B4)", withSession.writtenAt == at(60 + 400))
        val test = PhoneSessionSyncWriter.forFile(input(record("t1", at(40), null, isTest = true), now = at(60 + 400)), at(60))
        report.check("ps2b/stamp (S2): a test session does not make the file use the session clock", test.writtenAt == at(60))
        // Through the controller: below 13, no session, the wall clock fixed after the process started.
        val w = World(FakeSource(network = false, canCome = false))
        w.source.wall = w.source.wall.plus(Duration.ofMinutes(5))
        val snapshot = w.controller.syncInput()
        report.check("ps2b/stamp (S2): a controller with no session stamps with the fallback however far its own clock is from it", PhoneSessionSyncWriter.forFile(snapshot, at(1)).writtenAt == at(1) && snapshot.running == null)
    }

    // N4: the network time arrived since the last refresh: the file is not silent a few minutes longer.
    private fun lateTime(report: CheckReport) {
        val w = World(FakeSource(network = false, canCome = true))
        val before = w.controller.syncInput()
        w.source.advance(min)
        w.source.network = true
        val after = w.controller.syncInput()
        report.check("ps2b/stamp (N4): with no session, the network time arriving makes the clock stamp at the next look at the file, not at the next refresh", !before.stampable && after.stampable)
    }

    // --- S5: the QR file --------------------------------------------------------------------------------------------------------

    private fun qr(report: CheckReport) {
        val written = at(120) // 14:00 local, 2026-11-16
        val dayStart = written.atZone(zone).toLocalDate().atStartOfDay(zone).toInstant()
        val yesterday = SyncSession(dayStart.minusSeconds(3 * 3600), dayStart.minusSeconds(2 * 3600), "Deep work", false)
        val today = SyncSession(at(10), at(40), "Deep work", false)
        val open = SyncSession(at(100), null, "Deep work", false)
        val file = SyncFile(1, card(writtenAt = written), listOf(open, yesterday, today), emptyList(), null, emptyList(), written.minus(Duration.ofDays(90)))
        val qr = com.accountabilibuddy.shared.sync.SyncFileStrip.forQr(file, zone)
        report.check("ps2b/qr (S5): the QR file carries only today's sessions, oldest first (the open one ends when the file was written)", qr.sessions == listOf(today, open), "${qr.sessions.map { it.start }}")
        report.check("ps2b/qr (S5): and says it speaks from the end of the newest session left out, so a receiver keeps its older copies", qr.coversFrom == yesterday.end!!.let { maxOf(it, written.minus(Duration.ofDays(90))) })
        val allToday = com.accountabilibuddy.shared.sync.SyncFileStrip.forQr(file.copy(sessions = listOf(today)), zone)
        report.check("ps2b/qr (S5): a file with today's sessions only keeps its coversFrom", allToday.coversFrom == written.minus(Duration.ofDays(90)) && allToday.sessions == listOf(today))
    }

    // --- the write gate ---------------------------------------------------------------------------------------------------------

    private fun gate(report: CheckReport) {
        val file = SyncFile(1, card(), emptyList(), emptyList(), null, emptyList())
        val gate = OwnFileWriteGate()
        report.check("ps2b/gate (B13): the first write is due", gate.due(file, at(0), true))
        gate.written(at(0))
        report.check("ps2b/gate (B13): an unchanged file is not written again within the heartbeat", !gate.due(file, at(1), true))
        report.check("ps2b/gate (B13): while a session runs the heartbeat is at most 5 minutes (an unchanged file is rewritten)", OwnFileWriteGate.SESSION_HEARTBEAT <= Duration.ofMinutes(5) && gate.due(file, at(5), true))
        report.check("ps2b/gate: with no session the 30-minute rule stands", !gate.due(file, at(20), false) && gate.due(file, at(30), false))
    }

    // --- sources ----------------------------------------------------------------------------------------------------------------

    private fun sources(report: CheckReport) {
        fun code(path: String) = Sync1cCheck.appSource(path)?.let(Sync1cCheck::code)
        val sync = code("sync/SyncCoordinator.kt")
        report.check("ps2b/sources (B4): this phone's file takes its time, sessions, running and coversFrom from the session part (one snapshot)",
            sync != null && sync.contains("fun ownFile(): SyncFile = ownFileWith(runCatching { ownSessions() }.getOrNull())") && sync.contains("sessions?.writtenAt ?: now()") &&
                sync.contains("sessions?.sessions.orEmpty()") && sync.contains("coversFrom = sessions?.coversFrom") && sync.contains("running = sessions?.running"))
        val pickups = sync?.substringAfter("fun pickups()", "")?.substringBefore("fun join(")
        report.check("ps2b/sources (R14): pickups() still skips phones: a phone session is never in the pickups the PC counts", pickups != null && pickups.contains("kind != SyncDeviceCard.KIND_PHONE"), pickups?.take(200) ?: "no pickups()")
        val app = code("CompanionApp.kt")
        report.check("ps2b/sources: the app hands the coordinator the session part and writes the file at each state change",
            app != null && app.contains("sync.ownSessions = { phoneSession.syncPart(") && app.contains("phoneSession.onSyncWrite = ::sendOwnFileNow"))
        report.check("ps2b/sources (B13): a phone session rewrites the file every 2 minutes and keeps Drive and the relay at their busy pace",
            app != null && app.contains("sync.inFocusSession = { placementStore.load()?.active == true || phoneSession.realSessionRunning() }") &&
                app.substringAfter("private fun driveBusy()").substringBefore("return sync.received.value.any").contains("phoneSession.realSessionRunning()"))
        report.check("ps2b/sources (B13): the ask reaches every way: the folder, Drive and the relay",
            app != null && app.substringAfter("private suspend fun runOwnFilePasses()").substringBefore("\n    }").let { it.contains("folderSource.syncNow()") && it.contains("driveSource.syncNow()") && it.contains("relaySource.syncNow()") })

        // R12, B1: the writer and the counting never read a wall clock or a caller's clock for the session.
        val shared = listOf("session/PhoneSessionSyncWriter.kt", "session/PhoneSessionFiles.kt").map { it to (code(it) ?: sharedSource(it)) }
        report.check("ps2b/sources (R14): nothing that names a lock, Curbox, an app package, a pickup or a reason is in the writer or the own-device file",
            shared.all { (_, text) -> text != null && !Regex("""(?i)lmp|curbox|packageName|distract|pickup|reason|profileId""").containsMatchIn(Sync1cCheck.code(text)) }, shared.map { it.first to (it.second != null) }.toString())

        // B9: every reader of `allReceived`, classified. Device lists keep to the received files; whatever counts focus goes through focusDevices.
        val readers = listOf(
            "CompanionApp.kt" to "list (goal history's device list) and the focus door itself",
            "ui/CompanionViewModel.kt" to "list (a device's name) and the focus flow's source",
            "ui/RemoveDeviceFlow.kt" to "list (a removed device's snapshot)",
            "ui/tasks/ScopeViews.kt" to "list (a device's name)",
            "ui/tasks/archive/ArchiveData.kt" to "list (every device's task archive)",
        )
        val dir = listOf(java.io.File("app/src/main/java/com/accountabilibuddy/companion"), java.io.File("../app/src/main/java/com/accountabilibuddy/companion")).firstOrNull { it.isDirectory }
        val reading = Regex("""\bsync\.allReceived\b""")
        val found = dir?.walkTopDown()?.filter { it.isFile && it.extension == "kt" }?.filter { reading.containsMatchIn(Sync1cCheck.code(it.readText())) }
            ?.map { it.relativeTo(dir).path.replace('\\', '/') }?.toSortedSet().orEmpty()
        report.check("ps2b/sources (B9): `sync.allReceived` is read only by the classified device-list readers (anything else that counts focus uses focusDevices)", found == readers.map { it.first }.toSortedSet(), "found $found")
        val viaDoor = listOf("notify/HeaderSource.kt", "notify/GoalReminders.kt").all { code(it)?.contains("app.focusDevices()") == true }
        report.check("ps2b/sources (B9): the day header's and the reminders' focus, and the work-event credit, read the focus door", viaDoor && app != null && app.contains("focusDevices().flatMap { com.accountabilibuddy.shared.sync.GroupSessions.closeOpenPieces(it.file, it.receivedAt, now).sessions }"))
        val vm = code("ui/CompanionViewModel.kt")
        val main = code("ui/MainScreen.kt")
        report.check("ps2b/sources (B9): Today, Goals, Stats and the Devices day line are fed the focus flow (own sessions included), not the received list",
            vm != null && vm.contains("val syncFocusReceived") && !vm.contains("val syncAllReceived") && main != null && main.contains("viewModel.syncFocusReceived") && !main.contains("viewModel.syncAllReceived"))
        val bridge = code("watch/WatchBridge.kt")
        report.check("ps2b/sources (B9): the watch's focus total reads the focus door and the arrival times", bridge != null && bridge.contains("val counted = focusOf(allReceived.value)") && bridge.contains("receivedAtOf = counted.arrivalOf()") && app != null && app.contains("focusOf = { focusDevices(it) }"))
        val goalSources = listOf("ui/goals/GoalPageState.kt", "ui/goals/GoalsScreen.kt", "ui/stats/StatsGoalsModel.kt", "ui/today/TodayGoalsCard.kt", "notify/GoalReminders.kt", "ui/goals/GoalTargetStep.kt")
        report.check("ps2b/sources (PS1b known limit): every goal reader passes the arrival time of each file", goalSources.all { code(it)?.let { text -> text.contains("arrivalOf()") || text.contains("receivedAtOf = {") } == true } && listOf("notify/HeaderSource.kt").all { code(it)?.contains("receivedAtOf = counted.arrivalOf()") == true })
        val page = code("goals/PhoneGoalPage.kt")
        val overview = code("goals/PhoneGoalsOverview.kt")
        val snapshot = code("watch/PhoneSnapshot.kt")
        report.check("ps2b/sources (PS1b known limit): the goal page, the goals overview and the watch snapshot pass the arrival time on to what judges a phone's open session",
            page != null && page.contains("taskLinks = taskLinks, receivedAtOf = receivedAtOf)") && overview != null && overview.contains("taskLinks = taskLinks, receivedAtOf = receivedAtOf)") &&
                snapshot != null && snapshot.contains("FocusDayCombine.combine(totals, today, zone, now, receivedAtOf = receivedAtOf, planParts ="))
        val receiver = code("session/PhoneSessionAlarmReceiver.kt")
        report.check("ps2b/sources (S3): the heartbeat receiver stays awake (goAsync) until the passes are done, bounded; a refresh never pushes the alarm back",
            receiver != null && receiver.contains("goAsync()") && receiver.contains("intent.action == PhoneSessionAlarm.ACTION_HEARTBEAT") && receiver.contains("app.sendOwnFileAndWait(WAIT_FOR_FILE_MS)") && receiver.contains("8_000L") &&
                app != null && app.contains("suspend fun sendOwnFileAndWait(timeoutMs: Long)") && app.contains("PhoneSessionBoundedWait.run(timeoutMs)") && !app.contains("withTimeoutOrNull(timeoutMs) { runOwnFilePasses() }"))
        val controllerSrc = code("session/PhoneSessionController.kt")
        report.check("ps2b/sources (S3): the heartbeat alarm is set from the last ask, not from the refresh", controllerSrc != null && controllerSrc.contains("ports.scheduleAlarm(PhoneAlarmKind.HEARTBEAT, nextHeartbeatInMs())") && controllerSrc.contains("last.plus(HEARTBEAT_ALARM_EVERY)"))
        report.check("ps2b/sources (Q1183): requests for the file are debounced (a burst of changes is one round of passes)",
            app != null && app.substringAfter("fun sendOwnFileNow()").substringBefore("suspend fun sendOwnFileAndWait").let { it.contains("delay(OWN_FILE_DEBOUNCE_MS)") && it.contains("ownFilePending") })
        report.check("ps2b/sources (S4): the focus flow is built off the main thread", vm != null && vm.contains(".flowOn(Dispatchers.Default)") && vm.indexOf(".flowOn(Dispatchers.Default)") < vm.indexOf(".stateIn(viewModelScope, SharingStarted.Eagerly, sync.allReceived.value)"))
        val troubleshoot = code("sync/SyncTroubleshoot.kt")
        val runtimeSrc = code("session/PhoneSessionRuntime.kt")
        report.check("ps2b/sources (Q1181): Troubleshoot's report says the phone's sessions wait for network time",
            app != null && app.contains("notes = { phoneSession.troubleshootLines() }") && troubleshoot != null && troubleshoot.contains("sync.syncLogger?.lines().orEmpty() + runCatching { notes() }") &&
                runtimeSrc != null && runtimeSrc.contains("This phone's sessions wait for network time before they're sent."))
        report.check("ps2b/sources (N5): the Work-event credit runs an open piece to what the receiver heard, like every other focus reader", app != null && app.contains("GroupSessions.closeOpenPieces(it.file, it.receivedAt, now).sessions"))
        report.check("ps2b/sources (B1): a lost log is told from a new phone by the set-aside copy, and 'history unknown' is saved",
            controllerSrc != null && controllerSrc.contains("if (ports.store.hasAsideCopy())") && controllerSrc.contains("PhoneSessionStoreData(historyStartsAt = start, historyUnknown = start == null)") && controllerSrc.contains("logUnreadable = read.data.historyUnknown && read.data.historyStartsAt == null"))
        val controller = code("session/PhoneSessionController.kt")
        report.check("ps2b/sources (R12): the sync snapshot and the write ask use the session clock and no wall clock",
            controller != null && controller.contains("sessionNow = clock.now()") && controller.contains("lastSyncWrite = clock.now()") && !Regex("""Instant\.now\(""").containsMatchIn(controller))
    }

    private fun sharedSource(path: String): String? =
        listOf(java.io.File("shared/src/main/java/com/accountabilibuddy/shared/$path"), java.io.File("../shared/src/main/java/com/accountabilibuddy/shared/$path")).firstOrNull { it.isFile }?.readText()
}
