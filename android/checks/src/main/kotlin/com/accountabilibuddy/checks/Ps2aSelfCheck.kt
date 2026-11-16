package com.accountabilibuddy.checks

import com.accountabilibuddy.companion.session.PhoneAlarmKind
import com.accountabilibuddy.companion.session.PhoneSessionController
import com.accountabilibuddy.companion.session.PhoneSessionPorts
import com.accountabilibuddy.companion.session.PhoneSessionSealer
import com.accountabilibuddy.companion.session.PhoneSessionStart
import com.accountabilibuddy.companion.session.PhoneSessionStore
import com.accountabilibuddy.shared.session.PhoneAway
import com.accountabilibuddy.shared.session.PhoneAwayKind
import com.accountabilibuddy.shared.session.PhoneArmedEnd
import com.accountabilibuddy.shared.session.PhoneClockSource
import com.accountabilibuddy.shared.session.PhoneEndSettings
import com.accountabilibuddy.shared.session.PhoneSessionAnchor
import com.accountabilibuddy.shared.session.PhoneSessionChecklist
import com.accountabilibuddy.shared.session.PhoneSessionCheckpoint
import com.accountabilibuddy.shared.session.PhoneSessionClock
import com.accountabilibuddy.shared.session.PhoneSessionCodec
import com.accountabilibuddy.shared.session.PhoneSessionEndKind
import com.accountabilibuddy.shared.session.PhoneSessionEngine
import com.accountabilibuddy.shared.session.PhoneSessionMode
import com.accountabilibuddy.shared.session.PhoneSessionNotice
import com.accountabilibuddy.shared.session.PhoneSessionOffGap
import com.accountabilibuddy.shared.session.PhoneSessionRead
import com.accountabilibuddy.shared.session.PhoneSessionReason
import com.accountabilibuddy.shared.session.PhoneSessionRecord
import com.accountabilibuddy.shared.session.PhoneSessionRefusal
import com.accountabilibuddy.shared.session.PhoneSessionResult
import com.accountabilibuddy.shared.session.PhoneSessionStoreData
import com.accountabilibuddy.shared.session.PhoneTimeReading
import com.accountabilibuddy.shared.sync.SessionTaskItem
import java.io.File
import java.nio.file.Files
import java.security.SecureRandom
import java.time.Duration
import java.time.Instant
import java.time.ZoneId
import java.time.ZoneOffset
import java.util.Base64
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * PS2a (phone only): the phone session's runtime, driven on the JVM with fakes: the clock (R12, B3b), the sealed codec and store (B5),
 * the controller's transitions (alarms, effects, persistence, process death), the lunch observer (B2, Q1159, Q1165) and the reboot rules
 * (R13, B3a to B3c, Q1163b, V-B1), plus source scans for trusted time and the wiring. Each line names the rule it pins.
 */
object Ps2aSelfCheck {
    internal val zone: ZoneId = ZoneOffset.ofHours(-5)
    internal val t0: Instant = Instant.parse("2026-11-16T17:00:00Z") // 12:00 local
    internal fun at(minutes: Long): Instant = t0.plusSeconds(minutes * 60)
    internal val min = 60_000L
    private const val GUID_A = "aaaaaaaa-0000-0000-0000-000000000001"
    private const val GUID_B = "aaaaaaaa-0000-0000-0000-000000000002"
    private const val SECRET_TITLE = "Secret task title xyzzy"
    private const val SECRET_REASON = "my private reason text plugh"

    internal fun task(id: String, title: String = "Task $id", doneAt: Instant? = null) = SessionTaskItem(id, title, doneAt, emptyList())

    fun run(report: CheckReport) {
        clock(report)
        codec(report)
        store(report)
        starting(report)
        transitions(report)
        ending(report)
        processDeath(report)
        heartbeat(report)
        lunch(report)
        reboot(report)
        untrustedReboot(report)
        fixRound(report)
        endAcrossReboot(report)
        unreadable(report)
        engineHeartbeat(report)
        sources(report)
    }

    // --- Fakes ------------------------------------------------------------------------------------------------------------------

    /** The device: an elapsed clock and a trusted reading. [advance] lets real time pass on both; moving [wall] alone moves the phone's clock. */
    internal class FakeSource(var elapsed: Long = 5_000_000L, var boot: Int = 7, var wall: Instant = t0, var network: Boolean = true, var canCome: Boolean = true) : PhoneClockSource {
        override fun elapsedRealtimeMs(): Long = elapsed
        override fun bootCount(): Int = boot
        override fun trustedReading(): PhoneTimeReading = PhoneTimeReading(wall, network)
        override fun trustCanCome(): Boolean = canCome
        fun advance(ms: Long) {
            elapsed += ms
            wall = wall.plusMillis(ms)
        }

        /** A reboot: the elapsed clock starts again, the boot count goes up, [off] of real time passed. */
        fun reboot(off: Duration, networkAfter: Boolean) {
            boot += 1
            elapsed = 3_000L
            wall = wall.plus(off)
            network = networkAfter
        }
    }

    internal class AesSealer(private val key: ByteArray = ByteArray(16) { it.toByte() }) : PhoneSessionSealer {
        override fun seal(text: String): String {
            val iv = ByteArray(12).also { SecureRandom().nextBytes(it) }
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(128, iv))
            return Base64.getEncoder().encodeToString(iv + cipher.doFinal(text.toByteArray(Charsets.UTF_8)))
        }

        override fun open(sealed: String): String? = runCatching {
            val bytes = Base64.getDecoder().decode(sealed.trim())
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(128, bytes, 0, 12))
            String(cipher.doFinal(bytes, 12, bytes.size - 12), Charsets.UTF_8)
        }.getOrNull()
    }

    internal class FakePorts(val dir: File, override val clockSource: FakeSource, sealer: PhoneSessionSealer = AesSealer(), storeDir: File = dir) : PhoneSessionPorts {
        override val store = PhoneSessionStore(File(storeDir, "session/${PhoneSessionStore.SESSIONS_FILE}"), File(storeDir, "session/${PhoneSessionStore.REASONS_FILE}"), sealer)
        var mode = PhoneSessionMode.ACCOUNTABILI_BUDDY
        var settings = PhoneEndSettings.DEFAULT
        var lunchRunning = false
        var lunchStart: Instant? = null
        var live = false
        val alarms = mutableMapOf<PhoneAlarmKind, Long?>()
        val enforce = mutableListOf<Pair<PhoneSessionRecord, Instant?>>()
        val released = mutableListOf<PhoneSessionEndKind>()
        var syncWrites = 0
        /** The elapsed clock (ms) at each request for the sync file (PS2b: the 5-minute rewrite, B13). */
        val syncWriteAt = mutableListOf<Long>()
        var logSizeAtLastSyncWrite = -1
        var runningAtLastSyncWrite: PhoneSessionRecord? = null
        var changes = 0
        private var next = 0
        lateinit var controller: PhoneSessionController

        override fun mode() = mode
        override fun endSettings() = settings
        var zoneValue: ZoneId = zone
        override fun zone(): ZoneId = zoneValue
        override fun lunchRunning() = lunchRunning
        override fun lunchStart() = lunchStart
        override fun liveElsewhere(now: Instant) = live
        override fun newId() = "session-${++next}"
        override fun scheduleAlarm(kind: PhoneAlarmKind, afterMs: Long?) {
            alarms[kind] = afterMs
        }

        override fun requestSyncWrite() {
            syncWrites++
            syncWriteAt += clockSource.elapsed
            // What the sync file would be written from: the SAVED store (the effect must come after the save).
            val saved = (store.read() as? PhoneSessionRead.Readable)?.data
            logSizeAtLastSyncWrite = saved?.log?.size ?: -1
            runningAtLastSyncWrite = saved?.checkpoint?.record
        }

        override fun enforceUntil(record: PhoneSessionRecord, endsAt: Instant?) {
            enforce += record to endsAt
        }

        override fun releaseEnforcement(record: PhoneSessionRecord, kind: PhoneSessionEndKind) {
            released += kind
        }

        override fun changed() {
            changes++
        }
    }

    /** One phone: files in a temp folder, a device, and a controller that can be thrown away and made again (the process dying). */
    internal class World(val source: FakeSource = FakeSource()) {
        val dir: File = Files.createTempDirectory("ps2a").toFile()
        val ports = FakePorts(dir, source)
        var controller = PhoneSessionController(ports).also { ports.controller = it }

        /** The process dies and starts again (alarms stay: AlarmManager keeps them); [PhoneSessionController.onStart] runs. */
        fun restart(): PhoneSessionController {
            controller = PhoneSessionController(ports).also { ports.controller = it }
            controller.onStart()
            return controller
        }

        /** The phone goes off for [off] and starts again (alarms are dropped by Android) and the app starts. */
        fun rebootAndStart(off: Duration, networkAfter: Boolean): PhoneSessionController {
            source.reboot(off, networkAfter)
            ports.alarms.clear()
            return restart()
        }
    }

    private fun done(result: PhoneSessionResult): PhoneSessionResult.Done = result as? PhoneSessionResult.Done ?: error("refused: $result")
    private fun refusal(result: PhoneSessionResult): PhoneSessionRefusal? = (result as? PhoneSessionResult.Refused)?.reason

    // --- R12: the clock ---------------------------------------------------------------------------------------------------------

    private fun clock(report: CheckReport) {
        val source = FakeSource()
        val clock = PhoneSessionClock(source)
        report.check("ps2a/clock (R12): the session time is the anchor's trusted time", clock.now() == t0 && clock.isTrusted)
        source.advance(10 * min)
        report.check("ps2a/clock (R12): it moves on with the elapsed clock", clock.now() == at(10))
        source.wall = source.wall.plus(Duration.ofHours(3))
        report.check("ps2a/clock (R12): moving the phone's own clock changes nothing within the boot", clock.now() == at(10))
        report.check("ps2a/clock: the same boot is not a reboot", !clock.rebooted())
        source.boot += 1
        report.check("ps2a/clock: a new boot count is a reboot", clock.rebooted())
        source.boot -= 1
        val unknown = FakeSource(boot = -1)
        val unknownClock = PhoneSessionClock(unknown)
        unknown.advance(5 * min)
        unknown.elapsed = 100
        report.check("ps2a/clock: with no boot count the elapsed clock going back is a reboot", unknownClock.rebooted())

        val again = PhoneSessionClock(source, clock.anchor)
        report.check("ps2a/clock: a saved anchor carries on in the same boot (the app was restarted, not the phone)", again.now() == at(10))

        val reanchored = FakeSource(wall = at(100), elapsed = 9_000)
        val moved = PhoneSessionClock(reanchored, PhoneSessionAnchor(at(0), 1_000, 4, true))
        moved.reanchor()
        report.check("ps2a/clock (R12): after a reboot the anchor is taken on the trusted time again", moved.now() == at(100) && moved.anchor.bootCount == 7)

        val offline = FakeSource(network = false)
        val untrusted = PhoneSessionClock(offline)
        report.check("ps2a/clock (B3b): a clock on the phone's own time says it is not trusted", !untrusted.isTrusted)
        report.check("ps2a/clock (B3b): it does not become trusted while there is no network time", untrusted.upgradeIfTrusted() == null && !untrusted.isTrusted)
        offline.network = true
        report.check("ps2a/clock (B3b): the network time arriving anchors it again, once", untrusted.upgradeIfTrusted() != null && untrusted.isTrusted && untrusted.upgradeIfTrusted() == null)
    }

    // --- The codec and the store ------------------------------------------------------------------------------------------------

    private fun richRecord(): PhoneSessionRecord {
        val steps = listOf(SessionTaskItem("aaaaaaaa-0000-0000-0000-0000000000a1", "Step one", at(3), emptyList()))
        val tasks = listOf(
            SessionTaskItem(GUID_A, SECRET_TITLE, null, steps, "cat-1", "Work", "#fff", listOf("l1", "l2"), null),
            SessionTaskItem(GUID_B, "", null, emptyList(), localTo = "device-x"),
        )
        return PhoneSessionRecord(
            id = "s-1", profileId = "p-1", profileName = "Deep work", startedAt = at(0), phaseStartedAt = at(7), phaseEndsAt = at(57), isTest = false,
            away = listOf(
                PhoneAway(PhoneAwayKind.STEP_AWAY, at(1), at(2)), PhoneAway(PhoneAwayKind.PAUSED, at(3), at(4)),
                PhoneAway(PhoneAwayKind.LUNCH, at(5), at(6)), PhoneAway(PhoneAwayKind.DEVICE_OFF, at(8), null),
            ),
            checklist = PhoneSessionChecklist(tasks, at(0), true, 2, listOf(GUID_A, GUID_B)),
            currentTask = GUID_A, extensions = 2, endRequestedAt = at(9), armedEnd = PhoneArmedEnd(at(9), at(10), SECRET_REASON),
            heartbeatAt = at(11), offAt = at(12), endedAt = at(13), endKind = PhoneSessionEndKind.EARLY_END, heldAway = PhoneAwayKind.STEP_AWAY,
        )
    }

    private fun codec(report: CheckReport) {
        val record = richRecord()
        val running = record.copy(endedAt = null, endKind = null)
        val data = PhoneSessionStoreData(
            PhoneSessionCheckpoint(running, PhoneSessionAnchor(at(11), 123_456L, 9, true)),
            listOf(record, record.copy(id = "s-0", isTest = false, armedEnd = null)),
            PhoneSessionNotice("s-1", at(1), at(5), closed = true, untrusted = true),
            at(-60),
        )
        val text = PhoneSessionCodec.write(data)
        report.check("ps2a/codec: the store round trips every field (away kinds, checklist and its drops, armed end, hold, notice, history start)", PhoneSessionCodec.read(text) == data, "read ${PhoneSessionCodec.read(text)}")
        val empty = PhoneSessionStoreData()
        report.check("ps2a/codec: an empty store round trips", PhoneSessionCodec.read(PhoneSessionCodec.write(empty)) == empty)
        report.check("ps2a/codec (B5): text that is not the format reads as unreadable, never as empty", PhoneSessionCodec.read("nonsense") == null && PhoneSessionCodec.read("{}") == null && PhoneSessionCodec.read("[]") == null)
        report.check("ps2a/codec (B5): a newer version reads as unreadable (it is never overwritten as if empty)", PhoneSessionCodec.read(text.replace("\"version\":1", "\"version\":2")) == null)
        report.check("ps2a/codec (B5): a half-written file reads as unreadable", PhoneSessionCodec.read(text.substring(0, text.length / 2)) == null)
        val reasons = listOf(PhoneSessionReason("s-1", at(1), SECRET_REASON))
        report.check("ps2a/codec: reasons round trip, and the reason text is not in the text of a reason's toString", PhoneSessionCodec.readReasons(PhoneSessionCodec.writeReasons(reasons)) == reasons && !reasons[0].toString().contains("plugh"))
        report.check("ps2a/codec: unreadable reasons read as null", PhoneSessionCodec.readReasons("x") == null)
    }

    private fun store(report: CheckReport) {
        val dir = Files.createTempDirectory("ps2a-store").toFile()
        val files = File(dir, "session/${PhoneSessionStore.SESSIONS_FILE}") to File(dir, "session/${PhoneSessionStore.REASONS_FILE}")
        val sealer = AesSealer()
        val store = PhoneSessionStore(files.first, files.second, sealer)
        report.check("ps2a/store: no file reads as missing (an empty phone)", store.read() == PhoneSessionRead.Missing)
        val running = richRecord().copy(endedAt = null, endKind = null)
        val data = PhoneSessionStoreData(PhoneSessionCheckpoint(running, PhoneSessionAnchor(at(11), 1L, 3, false)), emptyList())
        report.check("ps2a/store: a write is kept and read back", store.write(data) && store.read() == PhoneSessionRead.Readable(data))
        val raw = files.first.readText()
        report.check("ps2a/store: the file is sealed: neither a task title nor an end reason is in it", !raw.contains("xyzzy") && !raw.contains("plugh") && !raw.contains("Deep work") && !raw.contains("startedAt"))
        report.check("ps2a/store: no temporary file is left after a write", File(files.first.parentFile, files.first.name + ".tmp").exists().not())
        val otherKey = PhoneSessionStore(files.first, files.second, AesSealer(ByteArray(16) { 99 }))
        report.check("ps2a/store (B5): a file the key can't open reads as unreadable, not empty", otherKey.read() == PhoneSessionRead.Unreadable)
        files.first.writeText("garbage")
        report.check("ps2a/store (B5): a damaged file reads as unreadable", store.read() == PhoneSessionRead.Unreadable)
        report.check("ps2a/store (B5): a copy of the unreadable file is set aside", store.setAsideUnreadable() && File(files.first.parentFile, files.first.name + ".unreadable").readText() == "garbage")
        files.first.writeText("garbage two")
        report.check("ps2a/store (S5): a second unreadable file gets its OWN copy name: the first copy stays and the new one is kept", store.setAsideUnreadable() &&
            File(files.first.parentFile, files.first.name + ".unreadable").readText() == "garbage" && File(files.first.parentFile, files.first.name + ".unreadable.1").readText() == "garbage two")

        val old = PhoneSessionReason("s-old", at(0), "old reason")
        val fresh = PhoneSessionReason("s-new", at(0).plus(Duration.ofDays(89)), SECRET_REASON)
        store.addReason(old, at(0), true, false)
        store.addReason(fresh, at(0).plus(Duration.ofDays(89)), true, false)
        report.check("ps2a/store: a reason of 89 days ago is kept", store.readReasons().map { it.sessionId } == listOf("s-old", "s-new"))
        store.addReason(PhoneSessionReason("s-3", at(0).plus(Duration.ofDays(91)), "later"), at(0).plus(Duration.ofDays(91)), true, false)
        report.check("ps2a/store: reasons older than 90 days are dropped", store.readReasons().map { it.sessionId } == listOf("s-new", "s-3"))
        report.check("ps2a/store: the reasons file is sealed too", !files.second.readText().contains("plugh"))
        val log = listOf(richRecord().copy(id = "a", endedAt = at(0)), richRecord().copy(id = "b", endedAt = at(0).plus(Duration.ofDays(80))))
        val pruned = PhoneSessionStoreData(log = log).withEnded(richRecord().copy(id = "c", endedAt = at(0).plus(Duration.ofDays(91))), at(0).plus(Duration.ofDays(91)), prune = true, capAtNewest = false)
        report.check("ps2a/store: the log keeps 90 days (an ended session is added, older ones go)", pruned.log.map { it.id } == listOf("b", "c") && pruned.checkpoint == null)
    }

    // --- Start ------------------------------------------------------------------------------------------------------------------

    private fun starting(report: CheckReport) {
        val w = World()
        val c = w.controller
        val result = c.start("p1", "Deep work", 50, listOf(task(GUID_A), task(GUID_B)))
        val record = (result as? PhoneSessionStart.Started)?.record
        report.check("ps2a/start: a session starts at the trusted time with its length", record != null && record.startedAt == t0 && record.phaseEndsAt == at(50) && c.running == record)
        report.check("ps2a/start (R12): an anchor is taken at the start and saved with it", (w.ports.store.read() as? PhoneSessionRead.Readable)?.data?.checkpoint?.anchor == c.clock.anchor && c.clock.anchor.wall == t0)
        report.check("ps2a/start: the end alarm is set for the set end, in elapsed milliseconds", w.ports.alarms[PhoneAlarmKind.END] == 50 * min && w.ports.alarms[PhoneAlarmKind.ARMED] == null)
        report.check("ps2a/start: the enforcement is asked to hold until the end, the sync file is asked for, the UI is told",
            w.ports.enforce.size == 1 && w.ports.enforce[0].second == at(50) && w.ports.syncWrites == 1 && w.ports.changes >= 1)
        report.check("ps2a/start: a second start while one runs is refused", c.start("p1", "x", 10) == PhoneSessionStart.AlreadyRunning)
        report.check("ps2a/start: the checklist keeps the start's tasks (R9)", c.running!!.checklist.tasks.size == 2 && c.running!!.checklist.madeBeforeStart)

        val b = World()
        b.ports.live = true
        report.check("ps2a/start (R1): a real session can't start while another device of the group is live", b.controller.start("p", "x", 25) == PhoneSessionStart.LiveElsewhere && b.controller.running == null)
        val test = b.controller.start("p", "Test", 1, isTest = true)
        report.check("ps2a/start (R1): a test session is never blocked, and is never synced or put in the log",
            test is PhoneSessionStart.Started && b.ports.syncWrites == 0)
        b.source.advance(2 * min)
        b.controller.refresh()
        b.controller.endOnCommand()
        report.check("ps2a/start: an ended test session leaves nothing in the log and writes no sync file", b.controller.log.isEmpty() && b.controller.running == null && b.ports.syncWrites == 0)

        val offline = World(FakeSource(network = false))
        offline.source.advance(30 * min)
        offline.source.network = true
        offline.controller.start("p", "x", 25)
        report.check("ps2a/start (R12): the anchor is taken again at the start: one that was not network-backed is now", offline.controller.clock.isTrusted && offline.controller.clock.anchor.wall == at(30))

        val bad = World()
        report.check("ps2a/start: a length the engine refuses is passed on", bad.controller.start("p", "x", 0) == PhoneSessionStart.Refused(PhoneSessionRefusal.BAD_LENGTH))

        // A session that can't be saved is not started: the checkpoint is what survives the process dying.
        val failDir = Files.createTempDirectory("ps2a-fail").toFile()
        val blocker = File(failDir, "blocked").also { it.writeText("not a folder") }
        val failing = FakePorts(failDir, FakeSource(), storeDir = blocker)
        val failController = PhoneSessionController(failing).also { failing.controller = it }
        report.check("ps2a/start: a checkpoint that can't be saved means no session starts", failController.start("p", "x", 25) == PhoneSessionStart.StoreFailed && failController.running == null && failing.syncWrites == 0)

        val noEnd = World()
        noEnd.controller.start("p", "x", null)
        report.check("ps2a/start: no set end means no end alarm", noEnd.ports.alarms[PhoneAlarmKind.END] == null && noEnd.controller.running!!.noSetEnd)
    }

    // --- Away, pause, +10 -------------------------------------------------------------------------------------------------------

    private fun transitions(report: CheckReport) {
        val w = World()
        val c = w.controller
        c.start("p1", "Deep work", 50)
        w.source.advance(10 * min)
        report.check("ps2a/away: step away keeps the end alarm and the timer", refusal(c.stepAway()) == null && c.running!!.openAway?.kind == PhoneAwayKind.STEP_AWAY && w.ports.alarms[PhoneAlarmKind.END] == 50 * min)
        report.check("ps2a/away: a second away is refused", refusal(c.pause()) == PhoneSessionRefusal.ALREADY_AWAY)
        report.check("ps2a/away: I'm back closes it and moves nothing", refusal(c.back()) == null && c.running!!.phaseEndsAt == at(50) && !c.running!!.isAway)
        w.source.advance(1 * min)
        c.pause()
        report.check("ps2a/pause: Pause timer cancels the end alarm (the countdown is frozen)", c.running!!.isTimerPaused && w.ports.alarms[PhoneAlarmKind.END] == null)
        w.source.advance(5 * min)
        val syncs = w.ports.syncWrites
        c.resume()
        report.check("ps2a/pause: Resume moves start and end later by the pause and sets the alarm again", c.running!!.phaseEndsAt == at(55) && w.ports.alarms[PhoneAlarmKind.END] == 39 * min && w.ports.syncWrites > syncs)
        report.check("ps2a/pause: Resume asks the enforcement again for the new end", w.ports.enforce.last().second == at(55))
        c.add10()
        report.check("ps2a/add10: +10 minutes moves the end, the alarm and the enforcement", c.running!!.phaseEndsAt == at(65) && w.ports.alarms[PhoneAlarmKind.END] == 49 * min && w.ports.enforce.last().second == at(65) && c.running!!.extensions == 1)
        c.pause()
        report.check("ps2a/add10 (R10): +10 is refused while paused", refusal(c.add10()) == PhoneSessionRefusal.ALREADY_AWAY)
        c.resume()
        val saved = (w.ports.store.read() as PhoneSessionRead.Readable).data.checkpoint!!.record
        report.check("ps2a/transitions: every change is saved at once", saved == c.running)
    }

    // --- Ending -----------------------------------------------------------------------------------------------------------------

    private fun ending(report: CheckReport) {
        // Accountabilibuddy: a wait and a reason, armed in the background, applied by the alarm.
        val w = World()
        val c = w.controller
        c.start("p1", "Deep work", 60, listOf(task(GUID_A, SECRET_TITLE)))
        w.source.advance(5 * min)
        report.check("ps2a/end (R8): a reason that is too short is refused and nothing is armed", refusal(c.endEarly("short")) == PhoneSessionRefusal.REASON_TOO_SHORT && c.running!!.armedEnd == null)
        c.openEnd()
        w.source.advance(10_000)
        c.endEarly(SECRET_REASON)
        val armed = c.running!!.armedEnd
        report.check("ps2a/end (R8): AB arms the end for the wait counted from opening End and sets the armed alarm", armed != null && armed.readyAt == at(5).plusSeconds(60) && w.ports.alarms[PhoneAlarmKind.ARMED] == 50_000L && c.running!!.isRunning)

        val c2 = w.restart()
        report.check("ps2a/end (process death): the armed end and its reason survive, and the alarm is set again at the app start", c2.running!!.armedEnd?.reason == SECRET_REASON && w.ports.alarms[PhoneAlarmKind.ARMED] == 50_000L)
        w.source.advance(49_000)
        c2.tick()
        report.check("ps2a/end (R8): the armed end does not apply before the wait is over", c2.running != null)
        w.source.advance(2_000)
        c2.tick()
        val ended = c2.log.singleOrNull()
        report.check("ps2a/end (R8): when the wait is over the alarm ends it as an early end", ended != null && ended.endKind == PhoneSessionEndKind.EARLY_END && c2.running == null)
        report.check("ps2a/end: the end releases the enforcement, cancels both alarms and asks for the sync file after the log has the session",
            w.ports.released == listOf(PhoneSessionEndKind.EARLY_END) && w.ports.alarms[PhoneAlarmKind.END] == null && w.ports.alarms[PhoneAlarmKind.ARMED] == null &&
                w.ports.logSizeAtLastSyncWrite == 1 && w.ports.runningAtLastSyncWrite == null)
        report.check("ps2a/end: the reason is kept sealed, 90 days", w.ports.store.readReasons().singleOrNull()?.let { it.sessionId == ended!!.id && it.text == SECRET_REASON } == true)
        val sessionsRaw = File(w.dir, "session/${PhoneSessionStore.SESSIONS_FILE}").readText()
        val reasonsRaw = File(w.dir, "session/${PhoneSessionStore.REASONS_FILE}").readText()
        report.check("ps2a/end: neither the task text nor the reason is in a file in the clear", !sessionsRaw.contains("xyzzy") && !sessionsRaw.contains("plugh") && !reasonsRaw.contains("plugh"))
        report.check("ps2a/end: the ended session in the log carries no armed end and no reason", ended!!.armedEnd == null && !PhoneSessionCodec.write(PhoneSessionStoreData(log = c2.log)).contains("plugh"))

        // Cancel is free.
        val k = World()
        k.controller.start("p", "x", 60)
        k.source.advance(min)
        k.controller.endEarly(SECRET_REASON)
        k.controller.cancelEnd()
        report.check("ps2a/end (R8): Cancel drops the wait and the armed alarm", k.controller.running!!.armedEnd == null && k.controller.running!!.endRequestedAt == null && k.ports.alarms[PhoneAlarmKind.ARMED] == null)

        // ProductaBuddy: at once, no reason.
        val pb = World()
        pb.ports.mode = PhoneSessionMode.PRODUCTA_BUDDY
        pb.controller.start("p", "x", 60)
        pb.source.advance(2 * min)
        pb.controller.endEarly(null)
        report.check("ps2a/end (R8): ProductaBuddy's End ends at once with no reason and keeps none", pb.controller.running == null && pb.controller.log.single().endKind == PhoneSessionEndKind.ON_COMMAND && pb.ports.store.readReasons().isEmpty())

        // The mode is read live (S3): a session started in PB, the app switched to AB, End needs the wait.
        val live = World()
        live.ports.mode = PhoneSessionMode.PRODUCTA_BUDDY
        live.controller.start("p", "x", 60)
        live.ports.mode = PhoneSessionMode.ACCOUNTABILI_BUDDY
        live.source.advance(2 * min)
        live.controller.endEarly(SECRET_REASON)
        report.check("ps2a/end (S3): the mode is read at the End, so AB's wait applies from the switch", live.controller.running != null && live.controller.running!!.armedEnd != null)

        // Time up: the end moment, not the late tick.
        val t = World()
        t.controller.start("p", "x", 25)
        t.source.advance(26 * min)
        t.controller.tick()
        report.check("ps2a/end: the set end passing ends it at the END moment (a late alarm counts no minute past it)", t.controller.log.single().endedAt == at(25) && t.controller.log.single().endKind == PhoneSessionEndKind.TIME_UP)

        // The phone's own clock moved forward: nothing ends (B1, R12).
        val m = World()
        m.source.network = false
        m.controller.start("p", "x", 25)
        m.source.wall = m.source.wall.plus(Duration.ofHours(2))
        m.controller.tick()
        m.controller.refresh()
        report.check("ps2a/end (B1, R12): the phone's clock moved 2 hours forward ends nothing and moves nothing", m.controller.running != null && m.controller.now() == t0)
        m.source.wall = m.source.wall.minus(Duration.ofHours(5))
        m.source.advance(26 * min)
        m.controller.tick()
        report.check("ps2a/end (R12): and the session still ends by its own elapsed time", m.controller.running == null && m.controller.log.single().endKind == PhoneSessionEndKind.TIME_UP)

        // All tasks done (R9) and the dropped-task tracking (B6).
        val d = World()
        d.controller.start("p", "x", 30, listOf(task(GUID_A), task(GUID_B)))
        d.source.advance(11 * min)
        d.controller.thisSessionChanged(listOf(task(GUID_A, doneAt = at(11)), task(GUID_B, doneAt = at(11))))
        val c3 = d.restart()
        report.check("ps2a/tasks (R9): the ticks are kept across a restart", c3.running!!.checklist.allDone && c3.running!!.checklist.droppedOpen == 0)
        val e = World()
        e.controller.start("p", "x", 30, listOf(task(GUID_A), task(GUID_B)))
        e.source.advance(11 * min)
        e.controller.thisSessionChanged(listOf(task(GUID_A, doneAt = at(11))))
        val e2 = e.restart()
        report.check("ps2a/tasks (B6): an open task that left This session is counted dropped, and that survives a restart", e2.running!!.checklist.droppedOpen == 1 && e2.running!!.checklist.droppedOpenIds == listOf(GUID_B))
        report.check("ps2a/tasks (B6): so 'All tasks done' is refused", refusal(e2.endAtOnce()) == PhoneSessionRefusal.NOT_ALL_DONE && e2.running != null)
        e2.thisSessionUndone(listOf(task(GUID_A, doneAt = at(11)), task(GUID_B)))
        report.check("ps2a/tasks (V-N1): Undo gives the drop back only for a task that was dropped open", e2.running!!.checklist.droppedOpen == 0)
        e2.thisSessionChanged(listOf(task(GUID_A, doneAt = at(11)), task(GUID_B, doneAt = at(11))))
        report.check("ps2a/tasks (R9): all ticked, 10 minutes in: ends at once with the reason 'All tasks done'", refusal(e2.endAtOnce()) == null && e2.log.single().endKind == PhoneSessionEndKind.ALL_TASKS_DONE &&
            e.ports.store.readReasons().single().text == "All tasks done")
    }

    // --- Process death (B3a) ----------------------------------------------------------------------------------------------------

    private fun processDeath(report: CheckReport) {
        val w = World()
        w.controller.start("p", "x", 480)
        w.source.advance(30 * min)
        w.controller.refresh()
        w.source.advance((4 * 60 + 30) * min) // killed, force-stopped or crashed for 4.5 hours, the phone stayed on
        val c = w.restart()
        report.check("ps2a/restart (B3a): an app restart in the same boot is not a gap: nothing is cut out, nothing closes, no notice, the end stands",
            c.running != null && c.running!!.away.isEmpty() && c.running!!.phaseEndsAt == at(480) && c.notice == null && c.now() == at(5 * 60))
        report.check("ps2a/restart: the alarms are set again, to the same end", w.ports.alarms[PhoneAlarmKind.END] == (480 - 300) * min)
        report.check("ps2a/restart: the enforcement is asked again", w.ports.enforce.last().second == at(480))
        c.refresh()
        report.check("ps2a/restart: and the heartbeat moves on", c.running!!.heartbeatAt == at(5 * 60))
    }

    // --- Heartbeat (R13, V-B1) --------------------------------------------------------------------------------------------------

    private fun heartbeat(report: CheckReport) {
        val w = World()
        w.controller.start("p", "x", 120)
        w.source.advance(7 * min)
        w.controller.refresh()
        report.check("ps2a/heartbeat (R13): a refresh on a trusted clock saves the heartbeat", w.controller.running!!.heartbeatAt == at(7))
        report.check("ps2a/heartbeat: a heartbeat alone tells nobody (no redraw loop)", w.ports.changes.let { before ->
            w.source.advance(min)
            w.controller.refresh()
            w.ports.changes == before
        })
        val u = World(FakeSource(network = false))
        u.controller.start("p", "x", 120)
        u.source.advance(7 * min)
        u.controller.refresh()
        u.controller.stepAway()
        u.controller.back()
        report.check("ps2a/heartbeat (V-B1): an untrusted clock's time is never saved as a heartbeat, not by a refresh and not by a state change", u.controller.running!!.heartbeatAt == t0)
        u.controller.onShutdown()
        report.check("ps2a/heartbeat (V-B1): nor as the moment the phone went off", u.controller.running!!.offAt == null)
        w.controller.onShutdown()
        report.check("ps2a/heartbeat (R13): a shutdown on a trusted clock saves offAt", w.controller.running!!.offAt == at(8))
    }

    // --- Lunch (B2, Q1159, Q1165) -----------------------------------------------------------------------------------------------

    private fun lunch(report: CheckReport) {
        val w = World()
        val c = w.controller
        c.start("p", "x", 60)
        w.source.advance(10 * min)
        w.ports.lunchRunning = true
        w.ports.lunchStart = at(8)
        c.refresh()
        report.check("ps2a/lunch (Q1159): a running lunch pauses the countdown from its recorded start", c.running!!.openAway?.kind == PhoneAwayKind.LUNCH && c.running!!.openAway!!.from == at(8) && c.running!!.isTimerPaused && w.ports.alarms[PhoneAlarmKind.END] == null)
        w.source.advance(40 * min)
        w.ports.lunchRunning = false // Discard it: the record is zero-length now
        w.ports.lunchStart = at(8)
        c.refresh()
        val lunch = c.running!!.away.single()
        report.check("ps2a/lunch (B2): when the lunch is seen not running the stretch ends then, so a Discard never shrinks the pause", lunch.kind == PhoneAwayKind.LUNCH && lunch.from == at(8) && lunch.to == at(50))
        report.check("ps2a/lunch: the end moves on by the whole stretch and the alarm is set again", c.running!!.phaseEndsAt == at(60 + 42) && w.ports.alarms[PhoneAlarmKind.END] == (102 - 50) * min)
        c.refresh()
        report.check("ps2a/lunch: looking again changes nothing", c.running!!.away.size == 1)

        // A lunch before the session started: clamped to the start.
        val b = World()
        b.ports.lunchRunning = true
        b.ports.lunchStart = at(-30)
        b.controller.start("p", "x", 60)
        report.check("ps2a/lunch: a lunch already running at the start pauses it from the start, not before", b.controller.running!!.openAway?.from == t0)

        // A lunch turns a Step away into its own.
        val s = World()
        s.controller.start("p", "x", 60)
        s.source.advance(5 * min)
        s.controller.stepAway()
        s.source.advance(5 * min)
        s.ports.lunchRunning = true
        s.ports.lunchStart = at(10)
        s.controller.refresh()
        report.check("ps2a/lunch: lunch turns a running-timer Step away into its own stretch", s.controller.running!!.away.map { it.kind } == listOf(PhoneAwayKind.STEP_AWAY, PhoneAwayKind.LUNCH) && s.controller.running!!.away[0].to == at(10))

        // Resume while a lunch runs keeps the lunch's pause (Q1165).
        val r = World()
        r.controller.start("p", "x", 60)
        r.source.advance(5 * min)
        r.controller.pause()
        r.source.advance(5 * min)
        r.ports.lunchRunning = true
        r.ports.lunchStart = at(8)
        r.controller.refresh()
        report.check("ps2a/lunch: a Pause timer already freezes the countdown, the lunch changes nothing", r.controller.running!!.openAway?.kind == PhoneAwayKind.PAUSED)
        r.controller.resume()
        report.check("ps2a/lunch (Q1165): Resume while a lunch runs hands the pause to the lunch", r.controller.running!!.openAway?.kind == PhoneAwayKind.LUNCH && r.controller.running!!.isTimerPaused)
        r.source.advance(30 * min)
        r.ports.lunchRunning = false
        r.controller.refresh()
        report.check("ps2a/lunch: the lunch ending closes it and the countdown goes on", !r.controller.running!!.isAway && r.controller.running!!.phaseEndsAt == at(60 + 35))
    }

    // --- The phone off (R13, B3) ------------------------------------------------------------------------------------------------

    private fun longSession(source: FakeSource = FakeSource()): World {
        val w = World(source)
        w.controller.start("p", "x", 480)
        w.source.advance(20 * min)
        w.controller.refresh() // heartbeat at +20
        return w
    }

    private fun reboot(report: CheckReport) {
        val a = longSession()
        a.controller.onShutdown() // offAt = +20
        val c = a.rebootAndStart(Duration.ofMinutes(10), networkAfter = true)
        val off = c.running!!.away.singleOrNull()
        report.check("ps2a/reboot (R13): a reboot after 10 minutes off is a DeviceOff stretch from the moment it went off", off != null && off.kind == PhoneAwayKind.DEVICE_OFF && off.from == at(20) && off.to == at(30))
        report.check("ps2a/reboot (R13): the end moves on by the gap, and the clock is anchored on the new boot", c.running!!.phaseEndsAt == at(490) && c.now() == at(30) && c.clock.anchor.bootCount == 8 && c.clock.anchor.networkBacked)
        report.check("ps2a/reboot (R13): a notice says what happened, times only", c.notice == PhoneSessionNotice(c.running!!.id, at(20), at(30), closed = false, untrusted = false) && c.running!!.offAt == null)
        report.check("ps2a/reboot (R13): the alarm is set again for the moved end (a reboot dropped it)", a.ports.alarms[PhoneAlarmKind.END] == (490 - 30) * min)
        report.check("ps2a/reboot: the sync file is asked for (the piece after the gap carries its minutes)", a.ports.syncWrites >= 2)

        // The last heartbeat stands when no shutdown was heard.
        val b = longSession()
        b.source.advance(3 * min) // 3 minutes pass with no heartbeat, then the phone dies without a word (battery)
        val cb = b.rebootAndStart(Duration.ofMinutes(10), networkAfter = true)
        report.check("ps2a/reboot (R13): with no shutdown heard the gap starts at the last heartbeat", cb.running!!.away.single().from == at(20) && cb.running!!.away.single().to == at(33))

        // Thresholds: 1:59, 2:00, 3:59 h, 4 h, a new day.
        val short = longSession().let { it.rebootAndStart(Duration.ofSeconds(119), true) }
        report.check("ps2a/reboot (R13): 1:59 off is nothing: no stretch, no notice", short.running!!.away.isEmpty() && short.notice == null && short.running!!.offAt == null)
        val two = longSession().let { it.rebootAndStart(Duration.ofMinutes(2), true) }
        report.check("ps2a/reboot (R13): 2:00 off is a stretch", two.running!!.away.single().kind == PhoneAwayKind.DEVICE_OFF)
        val long = longSession().let { it.rebootAndStart(Duration.ofMinutes(3 * 60 + 59), true) }
        report.check("ps2a/reboot (R13): 3:59 h off still only pauses", long.running != null && long.running!!.away.single().to == at(20 + 239))
        val w4 = longSession()
        w4.controller.onShutdown() // a shutdown heard: offAt = +20 (without it a gap only pauses, S7)
        val four = w4.rebootAndStart(Duration.ofHours(4), true)
        val closed = four.log.singleOrNull()
        report.check("ps2a/reboot (R13): 4 h off closes the session at the moment the phone went off", four.running == null && closed != null && closed.endedAt == at(20) && closed.endKind == PhoneSessionEndKind.PHONE_OFF)
        report.check("ps2a/reboot (R13): a close tells the user, releases the enforcement and asks for the sync file", four.notice == PhoneSessionNotice(closed!!.id, at(20), at(260), closed = true, untrusted = false) &&
            w4.ports.released == listOf(PhoneSessionEndKind.PHONE_OFF) && w4.ports.alarms[PhoneAlarmKind.END] == null && w4.ports.logSizeAtLastSyncWrite == 1)

        // A new local day after 2 minutes closes.
        val day = World()
        day.controller.start("p", "x", 720)
        day.source.advance(690 * min) // 23:30 local
        day.controller.refresh()
        day.controller.onShutdown()
        val next = day.rebootAndStart(Duration.ofMinutes(60), true)
        report.check("ps2a/reboot (R13): a new local day closes the session at the moment the phone went off", next.running == null && next.log.single().endedAt == at(690) && next.log.single().endKind == PhoneSessionEndKind.PHONE_OFF)

        // The set end passed before the phone went off: closes at the SET END, as a time-up (B3c).
        val s = World()
        s.controller.start("p", "x", 25)
        s.source.advance(20 * min)
        s.controller.refresh()
        s.source.advance(6 * min) // the end alarm was held back; the shutdown comes after the end
        s.controller.onShutdown()
        val sc = s.rebootAndStart(Duration.ofMinutes(5), true)
        report.check("ps2a/reboot (B3c): a set end that passed before the phone went off closes at the SET END, as time up", sc.running == null && sc.log.single().endedAt == at(25) && sc.log.single().endKind == PhoneSessionEndKind.TIME_UP)
    }

    private fun untrustedReboot(report: CheckReport) {
        // B1 (Android 13+): no network time after the reboot: an off gap is ALWAYS a hold, however long, and ends only on trusted time.
        val a = longSession()
        val c = a.rebootAndStart(Duration.ofHours(5), networkAfter = false)
        report.check("ps2a/untrusted (B1): on 13+ a 5 h gap on a clock that can't be checked is a HOLD, not a pause: the timer waits, nothing counts, the end has not moved",
            c.running != null && c.log.isEmpty() && c.running!!.holdsDeviceOff && c.running!!.openAway!!.from == at(20) && c.running!!.phaseEndsAt == at(480))
        report.check("ps2a/untrusted (V-B1): no heartbeat is saved from that time, and the clock says it is not trusted", c.running!!.heartbeatAt == at(20) && !c.clock.isTrusted && !c.clock.anchor.networkBacked)
        a.source.advance(60 * min)
        c.refresh()
        report.check("ps2a/untrusted (B1): and it stays held an hour later, on the same unchecked clock", c.running!!.holdsDeviceOff && c.running!!.phaseEndsAt == at(480))

        // The reviewer's scenario: the clock set back by about the off time, then a boot offline. It must be a hold; the network time then gives the real gap.
        val b = longSession()
        b.controller.onShutdown() // offAt = +20
        val bc = b.rebootAndStart(Duration.ofSeconds(10), networkAfter = false) // the phone's clock says 17:20:10 although 3 hours passed
        report.check("ps2a/untrusted (B1): a clock set back by the off time and an offline boot is a hold, not a 10-second pause", bc.running!!.holdsDeviceOff && bc.running!!.phaseEndsAt == at(480) && bc.running!!.away.single().from == at(20))
        b.source.wall = at(20 + 180)
        b.source.network = true
        bc.refresh()
        report.check("ps2a/untrusted (B1): the network time arriving gives the real gap: a 3 h pause from the off moment, the end 3 h later, the session still running (never over at 12:00:30)",
            bc.running != null && bc.log.isEmpty() && bc.running!!.away.single().from == at(20) && bc.running!!.away.single().to == at(200) && bc.running!!.phaseEndsAt == at(480 + 180))

        // A clock set BACK: a Hold, until the network time arrives.
        val h = longSession()
        h.source.boot += 1
        h.source.elapsed = 2_000
        h.source.wall = at(5) // the phone's clock was set back before the app started
        h.source.network = false
        h.ports.alarms.clear()
        val held = h.restart()
        report.check("ps2a/hold (Q1163b): a clock behind the off moment holds the session: a DeviceOff stretch from the off moment, timer paused, no end alarm",
            held.running!!.holdsDeviceOff && held.running!!.openAway!!.from == at(20) && held.running!!.isTimerPaused && h.ports.alarms[PhoneAlarmKind.END] == null)
        h.source.advance(min)
        held.refresh()
        report.check("ps2a/hold (V-B1): a refresh on the untrusted clock while it is held changes nothing", held.running!!.holdsDeviceOff && held.running!!.heartbeatAt == at(20))
        held.onShutdown()
        report.check("ps2a/hold (V-B1): the phone going off during the hold saves nothing", held.running!!.offAt == at(20))
        // The real time arrives: five hours on. The hold ends as a pause, never as a close.
        h.source.wall = at(20 + 300)
        h.source.network = true
        held.refresh()
        report.check("ps2a/hold (Q1163b, V-B1): the network time arriving ends the hold as a PAUSE, even for a 5 h gap: it never closes",
            held.running != null && held.log.isEmpty() && !held.running!!.holdsDeviceOff)
        report.check("ps2a/hold: the pause runs from the hold's start to the trusted now and the clock is trusted again", held.running!!.away.single().from == at(20) && held.running!!.away.single().to == at(320) && held.clock.isTrusted && held.running!!.phaseEndsAt == at(480 + 300))
        report.check("ps2a/hold: the notice is the pause's", held.notice?.closed == false)

        // B2, held boot: an End confirmed during the hold on the unchecked clock still serves its full wait on trusted time.
        val e = longSession()
        e.source.boot += 1
        e.source.elapsed = 2_000
        e.source.wall = at(5)
        e.source.network = false
        e.ports.alarms.clear()
        val he = e.restart()
        he.openEnd()
        he.endEarly(SECRET_REASON)
        val armedUntrusted = he.running!!.armedEnd
        report.check("ps2a/hold (B2): an End armed during a hold is armed on the unchecked clock (60 s from its time)", armedUntrusted != null && armedUntrusted.readyAt == at(5).plusSeconds(60))
        e.source.advance(20_000L)
        e.source.wall = at(20 + 300)
        e.source.network = true
        he.refresh()
        val armedTrusted = he.running?.armedEnd
        report.check("ps2a/hold (B2): when the network time arrives the armed end moves with it: it does NOT apply at once, its wait is served on trusted time",
            he.running != null && he.log.isEmpty() && armedTrusted != null && armedTrusted.readyAt == at(20 + 300).plusSeconds(40))
        e.source.advance(41_000L)
        he.tick()
        report.check("ps2a/hold (B2): and it applies when that wait is over", he.running == null && he.log.single().endKind == PhoneSessionEndKind.EARLY_END)

        // Below Android 13 (trust never comes): an untrusted clock at or past the off moment is a pause, and the session carries on.
        val old = longSession(FakeSource(network = false, canCome = false))
        old.controller.onShutdown()
        val oc = old.rebootAndStart(Duration.ofMinutes(45), networkAfter = false)
        old.source.advance(10 * min)
        oc.refresh()
        report.check("ps2a/untrusted: below Android 13 a reboot pauses for the gap and the session carries on", oc.running!!.away.single().from == at(20) && oc.running!!.away.single().to == at(65) && oc.running!!.phaseEndsAt == at(480 + 45))
        report.check("ps2a/untrusted (S9): below Android 13 the heartbeat and the shutdown come from the session's own clock, so the gap starts where the phone went off, not at the start",
            oc.running!!.away.single().from == at(20) && oc.running!!.heartbeatAt >= at(65))
        val old5 = longSession(FakeSource(network = false, canCome = false))
        old5.controller.onShutdown()
        val oc5 = old5.rebootAndStart(Duration.ofHours(5), networkAfter = false)
        report.check("ps2a/untrusted (S9): below Android 13 even 5 h off only pauses (nothing there closes on an unchecked time)", oc5.running != null && oc5.running!!.away.single().to == at(20 + 300))
    }

    // --- The PS2a fix round (the Opus review) -----------------------------------------------------------------------------------

    private fun fixRound(report: CheckReport) {
        // B2: a session started on a clock that can't be checked and set back 2 h; the network time arrives: still 2 h long, from the real start.
        val w = World(FakeSource(wall = at(-120), network = false))
        w.controller.start("p", "x", 120, listOf(task(GUID_A)))
        report.check("ps2a/start (B2): a start on an unchecked clock is stored on it and marked", w.controller.running!!.startedAt == at(-120) && w.controller.running!!.phaseEndsAt == at(0) &&
            (w.ports.store.read() as PhoneSessionRead.Readable).data.checkpoint!!.startUntrusted)
        w.source.advance(10_000L)
        w.controller.stepAway()
        w.source.advance(10_000L)
        w.controller.back()
        w.source.advance(40_000L)
        w.source.wall = at(1) // the real time (the phone's clock was 2 hours behind)
        w.source.network = true
        w.controller.refresh()
        val r = w.controller.running
        report.check("ps2a/start (B2): when the network time arrives every time of the record moves by the correction: the session is still 2 hours long from the real start, not over",
            r != null && w.controller.log.isEmpty() && r.startedAt == at(0) && r.phaseEndsAt == at(120) && r.phaseStartedAt == at(0) && r.checklist.sessionStart == at(0) &&
                r.away.single().from == at(0).plusSeconds(10) && r.away.single().to == at(0).plusSeconds(20))
        report.check("ps2a/start (B2): the clock is trusted, the mark is cleared, the saved file is the corrected one, the end alarm is for the real end", w.controller.clock.isTrusted && w.controller.now() == at(1) &&
            (w.ports.store.read() as PhoneSessionRead.Readable).data.checkpoint!!.let { !it.startUntrusted && it.record == r } && w.ports.alarms[PhoneAlarmKind.END] == 119 * min)
        // The other direction: the clock was 1 h ahead; the session shortens by nothing and starts at the real time.
        val f = World(FakeSource(wall = at(60), network = false))
        f.controller.start("p", "x", 30)
        f.source.advance(min)
        f.source.wall = at(1)
        f.source.network = true
        f.controller.refresh()
        report.check("ps2a/start (B2): a clock set ahead gives the same correction the other way", f.controller.running!!.startedAt == at(0) && f.controller.running!!.phaseEndsAt == at(30))
        // Started on a trusted clock: nothing moves at an upgrade.
        val t = World()
        t.controller.start("p", "x", 60)
        report.check("ps2a/start (B2): a start on a checked clock is not marked", !(t.ports.store.read() as PhoneSessionRead.Readable).data.checkpoint!!.startUntrusted)

        // S1: the zone is saved with the checkpoint; a new zone picked before the shutdown does not turn a short gap into a close.
        val z = longSession()
        z.controller.onShutdown() // offAt = +20 (17:20Z)
        z.ports.zoneValue = ZoneOffset.ofHoursMinutes(6, 35) // its midnight is 17:25Z: inside the 10-minute gap
        val zc = z.rebootAndStart(Duration.ofMinutes(10), true)
        report.check("ps2a/zone (S1): the new-day close is judged in the zone the session started in: a zone picked before the shutdown changes nothing", zc.running != null && zc.running!!.away.single().kind == PhoneAwayKind.DEVICE_OFF)
        val zl = longSession()
        zl.controller.onShutdown()
        report.check("ps2a/zone (S1): the zone is kept with the checkpoint", (zl.ports.store.read() as PhoneSessionRead.Readable).data.checkpoint!!.zoneId == zone.id)

        // S2: a refresh that wins the race with onStart after a reboot gives the same record as onStart alone.
        fun racing(refreshFirst: Boolean): PhoneSessionRecord? {
            val x = longSession()
            x.controller.onShutdown()
            x.source.reboot(Duration.ofMinutes(10), true)
            x.ports.alarms.clear()
            val fresh = PhoneSessionController(x.ports).also { x.ports.controller = it }
            if (refreshFirst) fresh.refresh() else fresh.onStart()
            return fresh.running
        }
        val viaRefresh = racing(true)
        val viaStart = racing(false)
        report.check("ps2a/race (S2): a refresh before onStart after a reboot judges the off gap too: the same record as onStart alone", viaRefresh != null && viaRefresh == viaStart && viaRefresh.away.single().kind == PhoneAwayKind.DEVICE_OFF)
        val lunchRace = longSession()
        lunchRace.source.reboot(Duration.ofMinutes(30), true)
        lunchRace.ports.lunchRunning = true
        lunchRace.ports.lunchStart = at(-500)
        val lc = PhoneSessionController(lunchRace.ports).also { lunchRace.ports.controller = it }
        lc.observeLunch()
        report.check("ps2a/race (S2): a lunch seen before onStart never opens a stretch before the session's start", lc.running!!.away.all { !it.from.isBefore(t0) })
        val early = (PhoneSessionEngine.start("s", "p", "x", 60, at(10)) as PhoneSessionResult.Done).record
        val clamped = done(PhoneSessionEngine.lunchHold(early, at(-100), at(-50))).record
        report.check("ps2a/race (S2): the engine clamps a lunch stretch to the session's start even when now is before it", clamped.away.single().from == at(10))

        // S6: an unchecked clock set far ahead must not sweep the log.
        for ((networkOn, expectedKept) in listOf(false to 2, true to 1)) {
            val p = World(FakeSource(network = networkOn))
            val old = richRecord().copy(id = "old", endedAt = t0.minus(Duration.ofDays(100)), startedAt = t0.minus(Duration.ofDays(100)), armedEnd = null, endKind = PhoneSessionEndKind.TIME_UP, isTest = false)
            p.ports.store.write(PhoneSessionStoreData(log = listOf(old)))
            val pc = p.restart()
            pc.start("p", "x", 25)
            p.source.advance(26 * min)
            pc.tick()
            report.check("ps2a/prune (S6): the 90-day log is pruned only on a trusted clock (${if (networkOn) "trusted" else "unchecked"}: ${expectedKept} kept)", pc.log.size == expectedKept, "${pc.log.map { it.id }}")
        }
        val reasonWorld = World(FakeSource(network = false))
        reasonWorld.ports.store.addReason(PhoneSessionReason("old", t0.minus(Duration.ofDays(200)), "old reason"), t0, true, false)
        reasonWorld.restart().start("p", "x", 60)
        reasonWorld.source.advance(min)
        reasonWorld.controller.endEarly(SECRET_REASON)
        reasonWorld.source.advance(61_000)
        reasonWorld.controller.tick()
        report.check("ps2a/prune (S6): reasons are not pruned on an unchecked clock either", reasonWorld.ports.store.readReasons().map { it.sessionId }.contains("old") && reasonWorld.ports.store.readReasons().size == 2)

        // S7: the waking heartbeat alarm runs while a session does, and a gap with no shutdown heard only ever pauses.
        val hb = World()
        hb.controller.start("p", "x", 60)
        report.check("ps2a/heartbeat (S7, PS2b): a waking heartbeat alarm is set at the start, every 5 minutes (B13's write rides on it)", hb.ports.alarms[PhoneAlarmKind.HEARTBEAT] == 5 * min)
        hb.ports.alarms[PhoneAlarmKind.HEARTBEAT] = null
        hb.source.advance(2 * min)
        hb.controller.refresh()
        report.check("ps2a/heartbeat (S7, PS2b S3): and set again at every refresh, 5 minutes after the LAST ask for the file (3 minutes from now), not 5 minutes from the refresh", hb.ports.alarms[PhoneAlarmKind.HEARTBEAT] == 3 * min)
        hb.source.advance(61 * min)
        hb.controller.tick()
        report.check("ps2a/heartbeat (S7): the end cancels it", hb.controller.running == null && hb.ports.alarms[PhoneAlarmKind.HEARTBEAT] == null)
        val killed = longSession() // no shutdown heard: the process was killed, then the phone rebooted 5 h later
        val kc = killed.rebootAndStart(Duration.ofHours(5), true)
        report.check("ps2a/reboot (S7): a gap with no shutdown heard only pauses, even for 5 h: never a close", kc.running != null && kc.log.isEmpty() && kc.running!!.away.single().to == at(20 + 300))
        val newDay = World()
        newDay.controller.start("p", "x", 720)
        newDay.source.advance(690 * min)
        newDay.controller.refresh()
        val nd = newDay.rebootAndStart(Duration.ofMinutes(60), true)
        report.check("ps2a/reboot (S7): nor across midnight", nd.running != null && nd.running!!.away.single().kind == PhoneAwayKind.DEVICE_OFF)

        // N3: the heartbeat is saved at most once a minute.
        val th = World()
        th.controller.start("p", "x", 60)
        th.source.advance(10 * min)
        th.controller.refresh()
        val saved = th.controller.running!!.heartbeatAt
        th.source.advance(30_000)
        th.controller.refresh()
        report.check("ps2a/heartbeat (N3): a refresh 30 seconds after a save saves nothing", th.controller.running!!.heartbeatAt == saved)

        // N6: the mode is read at End: PB when End is opened, AB when it is confirmed.
        val mode = World()
        mode.ports.mode = PhoneSessionMode.PRODUCTA_BUDDY
        mode.controller.start("p", "x", 60)
        mode.source.advance(2 * min)
        mode.controller.openEnd()
        mode.ports.mode = PhoneSessionMode.ACCOUNTABILI_BUDDY
        mode.controller.endEarly(SECRET_REASON)
        report.check("ps2a/end (N6, S3): PB to AB between opening End and confirming arms the wait: the session does not end", mode.controller.running != null && mode.controller.running!!.armedEnd != null)

        // N2: a lost log on an unchecked clock stores no history start until a trusted time is known.
        val u = World(FakeSource(network = false))
        val file = File(u.dir, "session/${PhoneSessionStore.SESSIONS_FILE}")
        file.parentFile.mkdirs()
        file.writeText("junk")
        val uc = u.restart()
        report.check("ps2a/unreadable (N2): on an unchecked clock no history start is stored: the log stays 'unreadable' for the sync file", uc.historyStartsAt == null && uc.logUnreadable)
        u.source.network = true
        uc.refresh()
        report.check("ps2a/unreadable (N2): the trusted time arriving stores it", uc.historyStartsAt == u.source.wall && !uc.logUnreadable)

        // S5: a Keystore that errs for a moment is not 'unreadable'.
        val flakyDir = Files.createTempDirectory("ps2a-flaky").toFile()
        val good = AesSealer()
        val sessions = File(flakyDir, "s.sealed")
        sessions.writeText(good.seal(PhoneSessionCodec.write(PhoneSessionStoreData(historyStartsAt = t0))))
        var failures = 2
        val flaky = object : PhoneSessionSealer {
            override fun seal(text: String) = good.seal(text)
            override fun open(sealed: String): String? = if (failures-- > 0) null else good.open(sealed)
        }
        val flakyStore = PhoneSessionStore(sessions, File(flakyDir, "r.sealed"), flaky)
        report.check("ps2a/store (S5): two transient failures to open do not make it unreadable (it is tried three times)", flakyStore.read() is PhoneSessionRead.Readable)
        failures = 3
        report.check("ps2a/store (S5): but three do", flakyStore.read() == PhoneSessionRead.Unreadable)

        // S4: the engine itself refuses an untrusted beat.
        val eng = (PhoneSessionEngine.start("s", "p", "x", 60, t0) as PhoneSessionResult.Done).record
        report.check("ps2a/engine (S4): heartbeat and phoneOff do nothing for an untrusted time and work for a trusted one",
            done(PhoneSessionEngine.heartbeat(eng, at(5), false)).record == eng && done(PhoneSessionEngine.phoneOff(eng, at(5), false)).record == eng &&
                done(PhoneSessionEngine.heartbeat(eng, at(5), true)).record.heartbeatAt == at(5) && done(PhoneSessionEngine.phoneOff(eng, at(5), true)).record.offAt == at(5))
    }

    // --- An End in progress across a reboot (the Opus verify: V-B1) and pruning below Android 13 (V-S1) -------------------------------

    private fun longWait(source: FakeSource = FakeSource()): World {
        val w = World(source)
        w.ports.settings = PhoneEndSettings(waitSeconds = 1800, minReasonLength = 15) // a 30-minute wait
        w.controller.start("p", "x", 240)
        w.source.advance(10 * min)
        w.controller.refresh()
        return w
    }

    private fun endAcrossReboot(report: CheckReport) {
        // 1: 13+, End confirmed on a checked clock (ready at +40), the clock set 2 h ahead and an offline reboot: the End starts again.
        val a = longWait()
        a.controller.endEarly(SECRET_REASON)
        val armed = a.controller.running!!.armedEnd
        a.controller.onShutdown()
        val ac = a.rebootAndStart(Duration.ofHours(2), networkAfter = false)
        report.check("ps2a/end-reboot (V-B1, 1): an End armed on a checked clock does not survive a reboot onto an unchecked clock set 2 h ahead: the session lives and the End is dropped",
            armed != null && armed.readyAt == at(40) && ac.running != null && ac.log.isEmpty() && ac.running!!.armedEnd == null && ac.running!!.endRequestedAt == null && a.ports.alarms[PhoneAlarmKind.ARMED] == null)
        a.source.wall = at(25)
        a.source.network = true
        ac.refresh()
        report.check("ps2a/end-reboot (V-B1, 1): and still running after the network time arrives, until a new confirm has served its full wait",
            ac.running != null && ac.running!!.armedEnd == null)
        ac.endEarly(SECRET_REASON)
        report.check("ps2a/end-reboot (V-B1, 1): the new confirm waits its full 30 minutes from the new clock", ac.running!!.armedEnd!!.readyAt == ac.now().plus(Duration.ofMinutes(30)))

        // 2: 13+, an End confirmed during a hold (written unchecked), then a reboot with network time: dropped, not applied at once.
        val b = longWait()
        b.source.boot += 1
        b.source.elapsed = 2_000
        b.source.wall = at(5) // clock set back
        b.source.network = false
        b.ports.alarms.clear()
        val bc = b.restart()
        bc.endEarly(SECRET_REASON)
        val inHold = bc.running!!.armedEnd
        b.source.boot += 1
        b.source.elapsed = 3_000
        b.source.wall = at(200) // the real time
        b.source.network = true
        b.ports.alarms.clear()
        val bc2 = b.restart()
        report.check("ps2a/end-reboot (V-B1, 2): an End armed during a hold (on the unchecked clock) is not applied at the next boot, even with network time: it is dropped and must be confirmed again",
            inHold != null && bc2.running != null && bc2.log.isEmpty() && bc2.running!!.armedEnd == null && bc2.running!!.endRequestedAt == null)

        // 3: below 13, End armed, clock set 1 h ahead, reboot: not applied.
        val c = longWait(FakeSource(network = false, canCome = false))
        c.controller.endEarly(SECRET_REASON)
        c.controller.onShutdown()
        val cc = c.rebootAndStart(Duration.ofHours(1), networkAfter = false)
        report.check("ps2a/end-reboot (V-B1, 3): below Android 13 an End armed before a reboot with the clock set 1 h ahead is dropped, not applied", cc.running != null && cc.log.isEmpty() && cc.running!!.armedEnd == null)

        // 4: End opened (not confirmed) on a checked clock, an offline reboot, then confirm: the wait counts from the new confirm.
        val d = longWait()
        d.controller.openEnd()
        val opened = d.controller.running!!.endRequestedAt
        val dc = d.rebootAndStart(Duration.ofMinutes(5), networkAfter = false)
        dc.endEarly(SECRET_REASON)
        report.check("ps2a/end-reboot (V-B1, 4): an End opened on a checked clock is not reused in the hold: the wait counts from the new confirm, 30 minutes from there",
            opened == at(10) && dc.running!!.armedEnd != null && dc.running!!.armedEnd!!.requestedAt == dc.now() && dc.running!!.armedEnd!!.readyAt == dc.now().plus(Duration.ofMinutes(30)))

        // Control: an End armed on a checked clock that reboots onto a checked clock keeps its wait (real time passed).
        val e = longWait()
        e.controller.endEarly(SECRET_REASON)
        e.controller.onShutdown()
        val ec = e.rebootAndStart(Duration.ofMinutes(5), networkAfter = true)
        report.check("ps2a/end-reboot: an End armed on a checked clock that reboots onto a checked clock keeps its wait", ec.running != null && ec.running!!.armedEnd?.readyAt == at(40))

        // V-S1: below Android 13 the log and the reasons are pruned by min(now, the newest end kept).
        val ancient = richRecord().copy(id = "ancient", startedAt = t0.minus(Duration.ofDays(300)), endedAt = t0.minus(Duration.ofDays(300)), armedEnd = null, endKind = PhoneSessionEndKind.TIME_UP)
        val older = richRecord().copy(id = "older", startedAt = t0.minus(Duration.ofDays(100)), endedAt = t0.minus(Duration.ofDays(100)), armedEnd = null, endKind = PhoneSessionEndKind.TIME_UP)
        val p = World(FakeSource(network = false, canCome = false))
        p.ports.store.write(PhoneSessionStoreData(log = listOf(ancient, older)))
        val pc = p.restart()
        pc.start("p", "x", 25)
        p.source.advance(26 * min)
        pc.tick()
        report.check("ps2a/prune (V-S1): below Android 13 the log is pruned by min(now, the newest end kept): the ancient one goes, the 100-day-old one stays, the new one is added",
            pc.log.map { it.id }.let { ids -> ids.size == 2 && "ancient" !in ids && "older" in ids }, "${pc.log.map { it.id }}")
        val far = World(FakeSource(wall = t0.plus(Duration.ofDays(400)), network = false, canCome = false))
        far.ports.store.write(PhoneSessionStoreData(log = listOf(older)))
        val fc = far.restart()
        fc.start("p", "x", 25)
        far.source.advance(26 * min)
        fc.tick()
        report.check("ps2a/prune (V-S1): a session ending on a clock set 400 days ahead cannot sweep the log below Android 13", fc.log.size == 2 && fc.log.any { it.id == "older" })
        val rw = World(FakeSource(network = false, canCome = false))
        rw.ports.store.addReason(PhoneSessionReason("ancient", t0.minus(Duration.ofDays(300)), "a"), t0.minus(Duration.ofDays(300)), true, false)
        rw.ports.store.addReason(PhoneSessionReason("older", t0.minus(Duration.ofDays(100)), "b"), t0.minus(Duration.ofDays(100)), true, false)
        rw.restart().start("p", "x", 60)
        rw.source.advance(min)
        rw.controller.endEarly(SECRET_REASON)
        rw.source.advance(61_000)
        rw.controller.tick()
        report.check("ps2a/prune (V-S1): below Android 13 the reasons too: the ancient one goes, the 100-day-old one stays",
            rw.ports.store.readReasons().map { it.sessionId }.let { ids -> "ancient" !in ids && "older" in ids && ids.size == 2 }, "${rw.ports.store.readReasons().map { it.sessionId }}")
    }

    // --- An unreadable log (B5) -------------------------------------------------------------------------------------------------

    private fun unreadable(report: CheckReport) {
        val w = World()
        val file = File(w.dir, "session/${PhoneSessionStore.SESSIONS_FILE}")
        file.parentFile.mkdirs()
        file.writeText("this is not a sealed file")
        val c = w.restart()
        val read = w.ports.store.read()
        report.check("ps2a/unreadable (B5): a log that can't be opened is set aside, not read as empty", File(file.parentFile, file.name + ".unreadable").readText() == "this is not a sealed file" && !c.logUnreadable)
        report.check("ps2a/unreadable (B5): the new log says its history starts now, so the sync file never claims the lost days", c.historyStartsAt == t0 && (read as? PhoneSessionRead.Readable)?.data?.historyStartsAt == t0)
        w.source.advance(5 * min)
        val again = w.restart()
        report.check("ps2a/unreadable (B5): that start is kept across a restart", again.historyStartsAt == t0 && again.running == null)
        report.check("ps2a/unreadable: a phone with no file at all has no history start", World().controller.historyStartsAt == null)
        again.start("p", "x", 25)
        report.check("ps2a/unreadable: a session can start after it", again.running != null && !again.logUnreadable)
    }

    // --- The engine's own change for the controller (an untrusted time is never a heartbeat) ------------------------------------

    private fun engineHeartbeat(report: CheckReport) {
        val record = (PhoneSessionEngine.start("s", "p", "x", 60, t0) as PhoneSessionResult.Done).record.copy(heartbeatAt = at(10))
        val trusted = done(PhoneSessionEngine.applyOffGap(record, PhoneSessionOffGap.Decision.Nothing, at(50), timeIsTrusted = true)).record
        val untrusted = done(PhoneSessionEngine.applyOffGap(record, PhoneSessionOffGap.Decision.Nothing, at(50), timeIsTrusted = false)).record
        report.check("ps2a/engine (V-B1): applying an off gap on a trusted time saves the heartbeat; on an untrusted one it leaves it", trusted.heartbeatAt == at(50) && untrusted.heartbeatAt == at(10))
        val pause = done(PhoneSessionEngine.applyOffGap(record, PhoneSessionOffGap.Decision.Pause(at(10), at(50)), at(50), timeIsTrusted = false)).record
        report.check("ps2a/engine (V-B1): a pause decided on an untrusted time leaves the heartbeat too", pause.heartbeatAt == at(10) && pause.away.single().kind == PhoneAwayKind.DEVICE_OFF)
    }

    // --- Source scans -----------------------------------------------------------------------------------------------------------

    private fun sources(report: CheckReport) {
        val dir = listOf(File("app/src/main/java/com/accountabilibuddy/companion/session"), File("../app/src/main/java/com/accountabilibuddy/companion/session")).firstOrNull { it.isDirectory }
        val files = dir?.listFiles { f -> f.extension == "kt" }.orEmpty().filter { it.name.startsWith("Phone") || it.name.startsWith("Android") || it.name.startsWith("Keystore") }
        report.check("ps2a/sources: the session files were found", files.size >= 10, "${files.size}")
        val clocks = Regex("""Instant\.now\(|System\.currentTimeMillis|LocalDateTime\.now|ZonedDateTime\.now|OffsetDateTime\.now|LocalDate\.now|Clock\.system""")
        val bad = files.filter { clocks.containsMatchIn(Sync1cCheck.code(it.readText())) }.map { it.name }
        report.check("ps2a/sources (R12): no wall clock anywhere in the phone session code (every time goes through PhoneSessionClock)", bad.isEmpty(), "$bad")
        val elapsed = files.filter { Regex("""SystemClock""").containsMatchIn(Sync1cCheck.code(it.readText())) }.map { it.name }.sorted()
        report.check("ps2a/sources (R12): the elapsed clock is read in the clock source and the alarm only", elapsed == listOf("AndroidPhoneClockSource.kt", "PhoneSessionAlarm.kt"), "$elapsed")
        val trustedTime = files.filter { Regex("""TrustedTime""").containsMatchIn(Sync1cCheck.code(it.readText())) }.map { it.name }.sorted()
        report.check("ps2a/sources (R12): TrustedTime is read in the clock source, and the runtime's lunch question, only", trustedTime == listOf("AndroidPhoneClockSource.kt", "PhoneSessionRuntime.kt"), "$trustedTime")
        val androidImports = files.filter { it.name in setOf("PhoneSessionController.kt", "PhoneSessionStore.kt", "PhoneSessionPorts.kt", "PhoneSessionSealer.kt", "PhoneSessionStart.kt") }
            .filter { Regex("""^import (android|androidx)\.""", RegexOption.MULTILINE).containsMatchIn(it.readText()) }.map { it.name }
        report.check("ps2a/sources: the controller, the store and the ports use no Android type", androidImports.isEmpty(), "$androidImports")
        val runtime = Sync1cCheck.appSource("session/PhoneSessionRuntime.kt")?.let(Sync1cCheck::code)
        report.check("ps2a/sources (R1): the start's 'live elsewhere' reads the group rule with the phone's PC rule", runtime != null && runtime.contains("GroupSessions.liveElsewhere(others, now) { PcFileControl.groupLive(it, now) }"))
        report.check("ps2a/sources (R12): the alarms count on the elapsed clock (a wall-clock alarm would move with the phone's clock)", Sync1cCheck.appSource("session/PhoneSessionAlarm.kt")?.let(Sync1cCheck::code)?.let { it.contains("ELAPSED_REALTIME_WAKEUP") && !it.contains("RTC_WAKEUP") } == true)
        val app = Sync1cCheck.appSource("CompanionApp.kt")?.let(Sync1cCheck::code)
        report.check("ps2a/sources: the app starts the phone session with the process (a boot, an update, a kill)", app != null && app.contains("phoneSession.start()") && app.contains("val phoneSession by lazy"))
        val refresher = Sync1cCheck.appSource("notify/Refresher.kt")?.let(Sync1cCheck::code)
        report.check("ps2a/sources (R13): every refresh gives the session its heartbeat", refresher != null && refresher.contains("app.phoneSession.refresh()"))
        val manifest = listOf(File("app/src/main/AndroidManifest.xml"), File("../app/src/main/AndroidManifest.xml")).firstOrNull { it.isFile }?.readText()
        report.check("ps2a/sources: the end alarm's receiver is in the manifest, not exported", manifest != null && Regex("""<receiver\s+android:name="\.session\.PhoneSessionAlarmReceiver"\s+android:exported="false"""").containsMatchIn(manifest))
        val alarmSrc = Sync1cCheck.appSource("session/PhoneSessionAlarm.kt")?.let(Sync1cCheck::code)
        report.check("ps2a/sources (S3): canScheduleExactAlarms() is asked only from Android 12 (a guard, or Android 8-11 would set no alarm)", alarmSrc != null && alarmSrc.contains("Build.VERSION.SDK_INT < Build.VERSION_CODES.S || alarms.canScheduleExactAlarms()"))
        val storeSrc = Sync1cCheck.appSource("session/PhoneSessionStore.kt")?.let(Sync1cCheck::code)
        report.check("ps2a/sources (S5): the file is synced to the disk before the rename", storeSrc != null && storeSrc.indexOf("out.fd.sync()") in 0 until storeSrc.indexOf("Files.move"))
        val trusted = Sync1cCheck.appSource("guidance/TrustedTime.kt")?.let(Sync1cCheck::code)
        report.check("ps2a/sources (B3b): TrustedTime says whether its time is network-backed", trusted != null && trusted.contains("fun reading()") && trusted.contains("Reading(SystemClock.currentNetworkTimeClock().instant(), true)") && trusted.contains("Reading(Instant.now(), false)"))
        val controller = Sync1cCheck.appSource("session/PhoneSessionController.kt")?.let(Sync1cCheck::code)
        report.check("ps2a/sources (V-B1, B1, S1): the controller passes the trust of its clock, whether trust can come and the saved zone to the off gap", controller != null && controller.contains("applyOffGap(record, decision, now, canBeat())") && controller.contains("PhoneSessionOffGap.decide(record, now, rebooted || record.offAt != null, trusted, canCome, zone())"))
        report.check("ps2a/sources: the checkpoint is written before the start's effects (a session that can't be kept is not started)", controller != null &&
            controller.indexOf("if (storeLocked || !ports.store.write(staged)) return PhoneSessionStart.StoreFailed") in 0 until controller.indexOf("dispatch(done.record, done.effects, now)"))
    }
}
