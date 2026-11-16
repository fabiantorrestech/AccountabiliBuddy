package com.accountabilibuddy.checks

import com.accountabilibuddy.companion.session.PhoneSessionStart
import com.accountabilibuddy.shared.session.EndSheetAction
import com.accountabilibuddy.shared.session.OwnSessionEnforcement
import com.accountabilibuddy.shared.session.PhoneEndSettings
import com.accountabilibuddy.shared.session.PhoneEndSheet
import com.accountabilibuddy.shared.session.PhoneSessionCodec
import com.accountabilibuddy.shared.session.PhoneSessionEndKind
import com.accountabilibuddy.shared.session.PhoneSessionEngine
import com.accountabilibuddy.shared.session.PhoneSessionMode
import com.accountabilibuddy.shared.session.PhoneSessionProfile
import com.accountabilibuddy.shared.session.PhoneSessionProfileSnapshot
import com.accountabilibuddy.shared.session.PhoneSessionRecord
import com.accountabilibuddy.shared.session.PhoneSessionRefusal
import com.accountabilibuddy.shared.session.PhoneSessionResult
import com.accountabilibuddy.shared.session.PhoneSessionStoreData
import com.accountabilibuddy.shared.session.PhoneSessionTexts
import com.accountabilibuddy.shared.session.PhoneStartRules
import com.accountabilibuddy.shared.session.SessionEndCostRules
import com.accountabilibuddy.shared.session.SessionTasksFromModel
import com.accountabilibuddy.shared.session.StartBlock
import com.accountabilibuddy.shared.session.StartPc
import com.accountabilibuddy.shared.session.StartTarget
import com.accountabilibuddy.shared.session.CurboxBlockRef
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.GroupSessions
import com.accountabilibuddy.shared.sync.ReceivedSyncFile
import com.accountabilibuddy.shared.sync.SessionPieces
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.tasks.ShelfEdits
import com.accountabilibuddy.shared.tasks.SyncTaskItem
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskFieldStamps
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskNumber
import java.time.Duration
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.util.Locale
import java.util.UUID

/**
 * PS3a (ADR 85, plan R1, R2, R3, R8, R9, R15, R16, B6, B15; PS2c review S3, S4, N4; PS6a review Q1193): the phone's Today session UI. The pure rules run on tables;
 * the Compose files that can't run on the JVM are pinned by source scans (the wiring: who may open Start, what ticks go through, 12-hour times, trusted time).
 */
object Ps3aSelfCheck {
    private val AB = PhoneSessionMode.ACCOUNTABILI_BUDDY
    private val PB = PhoneSessionMode.PRODUCTA_BUDDY
    private val t0 = Ps2aSelfCheck.t0
    private fun at(minutes: Long): Instant = Ps2aSelfCheck.at(minutes)
    private fun sec(seconds: Long): Instant = t0.plusSeconds(seconds)
    private val localTime = DateTimeFormatter.ofPattern("h:mm a", Locale.US).withZone(Ps2aSelfCheck.zone)
    private val time: (Instant) -> String = { localTime.format(it) }
    private const val REASON = "I need to leave now ok"
    private const val GUID_A = "aaaaaaaa-0000-0000-0000-00000000000a"
    private const val GUID_B = "bbbbbbbb-0000-0000-0000-00000000000b"
    private const val GUID_C = "cccccccc-0000-0000-0000-00000000000c"
    private const val GUID_D = "dddddddd-0000-0000-0000-00000000000d"

    fun run(report: CheckReport) {
        target(report)
        blocks(report)
        endSheet(report)
        endSheetParity(report)
        endSheetThroughController(report)
        snapshot(report)
        key(report)
        allDone(report)
        texts(report)
        sources(report)
    }

    // --- R16, R15: where Start runs ---------------------------------------------------------------------------------------------

    private fun pc(online: Boolean = true, heard: Instant? = sec(-5 * 60)) = StartPc("pc-1", "Work PC", online, heard)

    private fun target(report: CheckReport) {
        val pcOn = StartTarget.Pc("pc-1")
        report.check("ps3a/target (R16): the first time with the PC online it is the PC", PhoneStartRules.defaultTarget(null, pc()) == pcOn)
        report.check("ps3a/target (R16): the first time with the PC not online it is this phone", PhoneStartRules.defaultTarget(null, pc(online = false)) == StartTarget.Phone)
        report.check("ps3a/target (R15): with no PC in the group it is always this phone, whatever was remembered", listOf(null, "phone", "pc:pc-1", "nonsense").all { PhoneStartRules.defaultTarget(it, null) == StartTarget.Phone })
        report.check("ps3a/target (R16): a remembered 'phone' wins over an online PC", PhoneStartRules.defaultTarget("phone", pc()) == StartTarget.Phone)
        report.check("ps3a/target (R16): a remembered PC wins while it is online", PhoneStartRules.defaultTarget("pc:pc-1", pc()) == pcOn)
        report.check("ps3a/target (R16): a remembered PC that is not online falls back to this phone", PhoneStartRules.defaultTarget("pc:pc-1", pc(online = false)) == StartTarget.Phone)
        report.check("ps3a/target (R16): a remembered PC that is no longer the controlled one gives way to the controlled PC when it is online", PhoneStartRules.defaultTarget("pc:gone", pc()) == pcOn)
        report.check("ps3a/target (R16): text this version doesn't know is ignored", PhoneStartRules.defaultTarget("tablet:7", pc()) == pcOn && PhoneStartRules.parse("tablet:7") == null && PhoneStartRules.parse("pc:") == null && PhoneStartRules.parse(null) == null)
        report.check("ps3a/target (R16): the saved key is 'pc:<deviceId>' or 'phone' and reads back", PhoneStartRules.keyOf(pcOn) == "pc:pc-1" && PhoneStartRules.keyOf(StartTarget.Phone) == "phone" &&
            PhoneStartRules.parse("pc:pc-1") == pcOn && PhoneStartRules.parse("phone") == StartTarget.Phone)

        val now = sec(0)
        report.check("ps3a/target (R16): online = heard within 15 minutes (14:59 yes, 15:00 yes, 15:01 no)",
            PhoneStartRules.pcOnline(now.minusSeconds(14 * 60 + 59), now, true, false) && PhoneStartRules.pcOnline(now.minusSeconds(15 * 60), now, true, false) && !PhoneStartRules.pcOnline(now.minusSeconds(15 * 60 + 1), now, true, false))
        report.check("ps3a/target (R16): ... and its app answers control", !PhoneStartRules.pcOnline(now.minusSeconds(60), now, supportsControl = false, liveSession = false))
        report.check("ps3a/target (R16): ... and it has no live session", !PhoneStartRules.pcOnline(now.minusSeconds(60), now, supportsControl = true, liveSession = true))
        report.check("ps3a/target (R16): the line under the PC option is \"<PC> isn't online (last heard 12:00 PM)\"", PhoneStartRules.pcNote(pc(online = false, heard = t0), time) == "Work PC isn't online (last heard 12:00 PM)")
        report.check("ps3a/target (R16): ... without a time when none is known, and nothing when the PC is online or there is none",
            PhoneStartRules.pcNote(pc(online = false, heard = null), time) == "Work PC isn't online" && PhoneStartRules.pcNote(pc(), time) == null && PhoneStartRules.pcNote(null, time) == null)
        report.check("ps3a/target (R15): the PC option is offered only with a PC", PhoneStartRules.offersPc(pc()) && PhoneStartRules.offersPc(pc(online = false)) && !PhoneStartRules.offersPc(null))
    }

    // --- R1, R2, R3, B15: what blocks Start, and what it says -------------------------------------------------------------------

    private fun phoneFile(record: PhoneSessionRecord, name: String = "Pixel", writtenAt: Instant): ReceivedSyncFile {
        val card = SyncDeviceCard(UUID.fromString("11111111-0000-0000-0000-000000000001"), name, SyncDeviceCard.KIND_PHONE, "0.1.80", writtenAt, emptyList(), false, emptyMap())
        val file = SyncFile(SyncFile.CURRENT_FORMAT_VERSION, card, emptyList(), emptyList(), null, emptyList(), coversFrom = null, running = SessionPieces.runningOf(record))
        return ReceivedSyncFile(file, writtenAt)
    }

    private fun blocks(report: CheckReport) {
        val record = PhoneSessionEngine.start("s-1", "p", "Focus", 50, t0).let { (it as PhoneSessionResult.Done).record }
        val now = at(18)
        val live = GroupSessions.liveElsewhere(listOf(phoneFile(record, writtenAt = now)), now) { null }
        report.check("ps3a/blocks (R2): another phone's live session is found from its file", live != null && live.name == "Pixel" && !live.paused && !live.noSetEnd)
        report.check("ps3a/blocks (R2): its text is \"Running on Pixel · 32 min left\" (whole minutes, as the card says them)", live != null && PhoneStartRules.runningOnText(live, now) == "Running on Pixel · 32 min left")
        report.check("ps3a/blocks (R2): the last minute says so, and a time already past reads the same, never a negative",
            live != null && PhoneStartRules.runningOnText(live, at(49).plusSeconds(30)) == "Running on Pixel · less than a minute left" && PhoneStartRules.runningOnText(live, at(70)) == "Running on Pixel · less than a minute left")
        val paused = PhoneSessionEngine.pause(record, at(10)).let { (it as PhoneSessionResult.Done).record }
        val pausedLive = GroupSessions.liveElsewhere(listOf(phoneFile(paused, writtenAt = now)), now) { null }
        report.check("ps3a/blocks (R2): a paused session says \"· paused\"", pausedLive != null && PhoneStartRules.runningOnText(pausedLive, now) == "Running on Pixel · paused")
        val noEnd = PhoneSessionEngine.start("s-2", "p", "Focus", null, t0).let { (it as PhoneSessionResult.Done).record }
        val noEndLive = GroupSessions.liveElsewhere(listOf(phoneFile(noEnd, writtenAt = now)), now) { null }
        report.check("ps3a/blocks (R2): a session with no set end says \"· no set end\"", noEndLive != null && PhoneStartRules.runningOnText(noEndLive, now) == "Running on Pixel · no set end")

        report.check("ps3a/blocks (R3): a phone quiet for 20 minutes is not live, and Start comes back with \"Pixel's session last heard from 12:18 PM\"",
            GroupSessions.liveElsewhere(listOf(phoneFile(record, writtenAt = at(18))), at(38)) { null } == null &&
                GroupSessions.staleElsewhere(listOf(phoneFile(record, writtenAt = at(18))), at(38))?.let { PhoneStartRules.staleText(it, time) } == "Pixel's session last heard from 12:18 PM")

        val liveText = live?.let { PhoneStartRules.runningOnText(it, now) }.orEmpty()
        val running = StartBlock.RunningElsewhere(live!!, liveText)
        report.check("ps3a/blocks (R1): nothing blocks a free phone", PhoneStartRules.block(false, null, now, null, false) == null)
        report.check("ps3a/blocks (R1): a live session elsewhere blocks, with the \"Running on\" text", PhoneStartRules.block(false, live, now, null, false) == running && running.text == "Running on Pixel · 32 min left")
        report.check("ps3a/blocks (B15): a start request waiting for the PC blocks, with its words", PhoneStartRules.block(false, null, now, "Work PC", false) == StartBlock.WaitingForPc("Work PC") && StartBlock.WaitingForPc("Work PC").text == "Waiting for Work PC's answer")
        report.check("ps3a/blocks (ADR 60): the settings resting for a PC session with a stale file blocks", PhoneStartRules.block(false, null, now, null, true) == StartBlock.SettingsRest)
        report.check("ps3a/blocks: the phone's own running session comes first", PhoneStartRules.block(true, live, now, "Work PC", true) == StartBlock.OwnRunning)
        report.check("ps3a/blocks: a live session wins over a waiting request and the rest", PhoneStartRules.block(false, live, now, "Work PC", true) is StartBlock.RunningElsewhere && PhoneStartRules.block(false, null, now, "Work PC", true) is StartBlock.WaitingForPc)

        // R1 at the controller: a start over a live session is refused, a test never is.
        val world = Ps2aSelfCheck.World()
        world.ports.live = true
        val refused = world.controller.start("p", "Focus", 25)
        world.ports.live = false
        val started = world.controller.start("p", "Focus", 25)
        report.check("ps3a/blocks (R1): the controller refuses a start over a live session and starts once it is gone", refused == PhoneSessionStart.LiveElsewhere && started is PhoneSessionStart.Started)
    }

    // --- R8, R9: the End sheet ---------------------------------------------------------------------------------------------------

    private fun running(length: Int? = 50, tasks: List<SessionTaskItem> = emptyList()): PhoneSessionRecord =
        (PhoneSessionEngine.start("s-1", "p", "Focus", length, t0, tasks) as PhoneSessionResult.Done).record

    private fun opened(record: PhoneSessionRecord, at: Instant) = (PhoneSessionEngine.openEnd(record, at) as PhoneSessionResult.Done).record

    private fun sheet(record: PhoneSessionRecord, mode: PhoneSessionMode, now: Instant, reason: String = "", settings: PhoneEndSettings = PhoneEndSettings.DEFAULT, lmp: Instant? = null) =
        PhoneEndSheet.state(record, mode, settings, now, reason, lmp, time)

    private fun endSheet(report: CheckReport) {
        val timed = opened(running(), at(5))
        val start = at(5)
        // AccountabiliBuddy, a timed session.
        val ab0 = sheet(timed, AB, start)
        report.check("ps3a/end (R8): AccountabiliBuddy asks a wait and a reason: title, body and the ring's length", ab0.title == "End the session early?" && ab0.asksReason && ab0.reasonEnabled && !ab0.costFree &&
            ab0.waitSeconds == 60 && ab0.waitLeftSeconds == 60 && ab0.minReason == 15 && ab0.body == "Ending early takes a 1 minute wait and a reason of at least 15 characters.")
        report.check("ps3a/end (R8): at the start of the wait the one button is \"Confirm · ends in 1:00\", off until the reason is long enough",
            ab0.primary?.text == "Confirm · ends in 1:00" && ab0.primary.action == EndSheetAction.CONFIRM && !ab0.primary.enabled && ab0.allDone == null)
        val typed = sheet(timed, AB, start.plusSeconds(18), REASON)
        report.check("ps3a/end (R8): with the reason typed, Confirm is on and counts down (\"ends in 0:42\"); the characters are counted by trimmed length",
            typed.primary?.text == "Confirm · ends in 0:42" && typed.primary.enabled && typed.reasonCount == REASON.length && sheet(timed, AB, start, "   short   ").reasonCount == 5)
        report.check("ps3a/end (R8): 14 characters are not enough, 15 are", !sheet(timed, AB, start, "x".repeat(14)).primary!!.enabled && sheet(timed, AB, start, "x".repeat(15)).primary!!.enabled)
        val after = sheet(timed, AB, start.plusSeconds(60), REASON)
        report.check("ps3a/end (R8): once the wait is over the button is \"End session\" and ends it", after.primary?.text == "End session" && after.primary.action == EndSheetAction.END && after.primary.enabled && after.waitLeftSeconds == 0)
        report.check("ps3a/end (R8): ... and still needs the reason", !sheet(timed, AB, start.plusSeconds(61), "").primary!!.enabled)
        report.check("ps3a/end: Cancel is always there, free, and says \"Keep going\"", ab0.cancel.text == "Keep going" && ab0.cancel.action == EndSheetAction.CANCEL && ab0.cancel.enabled)

        // The wait text follows the setting.
        report.check("ps3a/end (R8): the wait and the reason length are the setting's (45 seconds, 20 characters)",
            sheet(timed, AB, start, settings = PhoneEndSettings(45, 20)).body == "Ending early takes a 45 second wait and a reason of at least 20 characters.")
        report.check("ps3a/end (R8): ... 90 seconds reads \"1 minute 30 second\", 2 minutes \"2 minute\" (adjectives)", PhoneEndSheet.waitText(Duration.ofSeconds(90)) == "a 1 minute 30 second wait" && PhoneEndSheet.waitText(Duration.ofSeconds(120)) == "a 2 minute wait" && PhoneEndSheet.waitText(Duration.ofSeconds(1)) == "a 1 second wait")
        report.check("ps3a/end: the countdown reads m:ss", PhoneEndSheet.clock(65) == "1:05" && PhoneEndSheet.clock(0) == "0:00" && PhoneEndSheet.clock(600) == "10:00")

        // Confirmed during the wait: armed.
        val armed = (PhoneSessionEngine.endEarly(timed, start.plusSeconds(10), AB, PhoneEndSettings.DEFAULT, REASON) as PhoneSessionResult.Done).record
        val armedSheet = sheet(armed, AB, start.plusSeconds(20), REASON)
        report.check("ps3a/end (R8, ADR 66 twin): a confirmed end shows \"ends by itself at\" its time, offers only Cancel the end, and the reason is no longer editable",
            armed.armedEnd != null && armedSheet.armedLine == "Confirmed. It ends by itself at 12:06 PM, even if you lock the screen. Cancel to keep going." && armedSheet.primary == null &&
                armedSheet.cancel.text == "Cancel the end" && !armedSheet.reasonEnabled && armedSheet.title == "Ending at 12:06 PM")

        // ProductaBuddy and the sessions that end on command.
        val pb = sheet(running(), PB, at(5))
        report.check("ps3a/end (R8): ProductaBuddy ends at once: \"End now\", no wait, no reason, free", pb.primary?.text == "End now" && pb.primary.action == EndSheetAction.END_NOW && pb.primary.enabled && !pb.asksReason && pb.costFree && pb.waitSeconds == 0 &&
            pb.body == "ProductaBuddy ends it at once: no wait, no reason.")
        val noEnd = sheet(running(null), AB, at(5))
        report.check("ps3a/end (R8): a session with no set end ends on command even in AccountabiliBuddy", noEnd.primary?.action == EndSheetAction.END_NOW && !noEnd.asksReason && noEnd.costFree && noEnd.body == "A session with no set end ends when you end it.")

        // Cost per mode is the same cost the mode switch and the pause gate use.
        val costAb = SessionEndCostRules.own(AB, running(), PhoneEndSettings.DEFAULT)
        val costPb = SessionEndCostRules.own(PB, running(), PhoneEndSettings.DEFAULT)
        val costNone = SessionEndCostRules.own(AB, running(null), PhoneEndSettings.DEFAULT)
        report.check("ps3a/end (R8): the sheet's cost is the cost the mode switch asks: AB timed pays (60 s, 15 characters), PB and no-set-end are free",
            !costAb.free && costAb.waitSeconds == ab0.waitSeconds && costAb.minReasonLength == ab0.minReason && costPb.free && pb.costFree && costNone.free && noEnd.costFree)

        // R9: All tasks done.
        val tasks = listOf(SessionTaskItem(GUID_A, "A", null, emptyList()), SessionTaskItem(GUID_B, "B", null, emptyList()))
        val done = running(tasks = tasks).let { r -> r.copy(checklist = r.checklist.copy(tasks = r.checklist.tasks.map { it.copy(doneAt = at(5)) })) }
        val abAll = sheet(opened(done, at(12)), AB, at(12))
        report.check("ps3a/end (R9): AccountabiliBuddy with every task ticked and 10 minutes in: \"All tasks done · End now\" above the paid way",
            abAll.allDone?.text == "All tasks done · End now" && abAll.allDone.action == EndSheetAction.ALL_DONE && abAll.allDone.enabled && abAll.primary?.action == EndSheetAction.CONFIRM)
        report.check("ps3a/end (R9): ... not at 9 minutes", sheet(opened(done, at(9)), AB, at(9)).allDone == null && sheet(done, PB, at(9)).primary?.action == EndSheetAction.END_NOW)
        report.check("ps3a/end (R9): ProductaBuddy: the one button reads \"All tasks done · End now\"", sheet(done, PB, at(12)).primary?.text == "All tasks done · End now" && sheet(done, PB, at(12)).primary?.action == EndSheetAction.ALL_DONE && sheet(done, PB, at(12)).allDone == null)
        report.check("ps3a/end (R9): a session that was made with no tasks never offers it", sheet(opened(running(), at(12)), AB, at(12)).allDone == null)

        // Q1193.
        val withLmp = sheet(timed, AB, start, lmp = at(50))
        report.check("ps3a/end (Q1193): an older LockMyPhone-Extended says \"keeps its lock until 12:50 PM\"; none, nothing", withLmp.lmpLine == "LockMyPhone-Extended keeps its lock until 12:50 PM." && ab0.lmpLine == null && PhoneEndSheet.lmpLine(at(50), time).endsWith("12:50 PM."))
    }

    // The sheet's button is exactly what the engine does: for every second of the wait and a little after, in both modes.
    private fun endSheetParity(report: CheckReport) {
        val timed = opened(running(), at(5))
        val start = at(5)
        var wrong = ""
        for (s in 0L..75L) {
            val now = start.plusSeconds(s)
            val button = sheet(timed, AB, now, REASON).primary ?: continue
            val result = PhoneSessionEngine.endEarly(timed, now, AB, PhoneEndSettings.DEFAULT, REASON) as PhoneSessionResult.Done
            val ended = !result.record.isRunning
            val armed = result.record.armedEnd != null
            val ok = when (button.action) {
                EndSheetAction.CONFIRM -> !ended && armed
                EndSheetAction.END -> ended && result.record.endKind == PhoneSessionEndKind.EARLY_END
                else -> false
            }
            if (!ok) wrong += " $s:${button.action}"
        }
        report.check("ps3a/end (R8): in AccountabiliBuddy the button never ends before the wait: Confirm arms (record still running) until 60 s, End session ends from 60 s", wrong.isEmpty(), wrong)
        val tooShort = PhoneSessionEngine.endEarly(timed, start.plusSeconds(90), AB, PhoneEndSettings.DEFAULT, "short") as? PhoneSessionResult.Refused
        // An End left open (the process died with the sheet up) is not a wait that stays over: after 10 minutes it starts again, in the sheet as in the engine.
        val old = sheet(timed, AB, start.plusSeconds(11 * 60), REASON)
        val oldResult = PhoneSessionEngine.endEarly(timed, start.plusSeconds(11 * 60), AB, PhoneEndSettings.DEFAULT, REASON) as PhoneSessionResult.Done
        report.check("ps3a/end (R8): an End opened 11 minutes ago starts its wait again (Confirm, not End session), and the engine arms rather than ends",
            old.primary?.action == EndSheetAction.CONFIRM && old.waitLeftSeconds == 60 && oldResult.record.isRunning && oldResult.record.armedEnd != null)
        report.check("ps3a/end (R8): ... and never without the reason, even long after the wait", tooShort?.reason == PhoneSessionRefusal.REASON_TOO_SHORT)
        val pbEnded = PhoneSessionEngine.endEarly(running(), at(5), PB, PhoneEndSettings.DEFAULT, null) as PhoneSessionResult.Done
        report.check("ps3a/end (R8): in ProductaBuddy End now ends at once with no reason", !pbEnded.record.isRunning && pbEnded.record.endKind == PhoneSessionEndKind.ON_COMMAND)
        val noEnd = PhoneSessionEngine.endEarly(running(null), at(5), AB, PhoneEndSettings.DEFAULT, null) as PhoneSessionResult.Done
        report.check("ps3a/end (R8): a session with no set end ends on command in AccountabiliBuddy", !noEnd.record.isRunning && noEnd.record.endKind == PhoneSessionEndKind.ON_COMMAND)
    }

    // The same through the controller (the armed end applies at the wait's end, on the session's clock, and Cancel is free).
    private fun endSheetThroughController(report: CheckReport) {
        val world = Ps2aSelfCheck.World()
        world.controller.start("p", "Focus", 50)
        world.controller.openEnd()
        world.source.advance(10_000)
        val armed = world.controller.endEarly(REASON)
        report.check("ps3a/end (ADR 66): Confirm during the wait arms the end; the session keeps running", armed is PhoneSessionResult.Done && world.controller.running?.armedEnd != null)
        world.source.advance(40_000)
        world.controller.tick()
        report.check("ps3a/end (ADR 66): 50 seconds after opening it is still running", world.controller.running != null)
        world.source.advance(11_000)
        world.controller.tick()
        report.check("ps3a/end (ADR 66): it ends by itself when the wait is over, as an early end", world.controller.running == null && world.controller.log.lastOrNull()?.endKind == PhoneSessionEndKind.EARLY_END)

        val second = Ps2aSelfCheck.World()
        second.controller.start("p", "Focus", 50)
        second.controller.openEnd()
        second.controller.endEarly(REASON)
        val cancelled = second.controller.cancelEnd()
        second.source.advance(120_000)
        second.controller.tick()
        report.check("ps3a/end (R8): Cancel is free and drops the armed end for good", cancelled is PhoneSessionResult.Done && second.controller.running?.armedEnd == null && second.controller.running != null)

        val third = Ps2aSelfCheck.World()
        third.controller.start("p", "Focus", 50)
        third.controller.openEnd()
        third.source.wall = third.source.wall.plus(Duration.ofHours(2))
        val atWallMoved = third.controller.endEarly(REASON)
        report.check("ps3a/end (R12): moving the phone's clock two hours does not make the wait over", atWallMoved is PhoneSessionResult.Done && third.controller.running != null)
    }

    // --- PS2c review S3: the profile is copied at Start ----------------------------------------------------------------------------

    private fun snapshot(report: CheckReport) {
        val profile = PhoneSessionProfile(
            "prof-1", "Deep", lmpProfile = "Deep lock", curboxFocusGroup = "group-9", curboxBlocks = listOf(CurboxBlockRef("group", "g1")),
            distractingApps = listOf("com.example.secretapp"),
        )
        val record = (PhoneSessionEngine.start("s-1", profile.id, profile.name, 50, t0, snapshot = profile.snapshot()) as PhoneSessionResult.Done).record
        report.check("ps3a/snapshot (S3): Start copies the profile's enforcement fields into the record", record.snapshot == profile.snapshot() &&
            record.snapshot.lmpProfile == "Deep lock" && record.snapshot.curboxFocusGroup == "group-9" && record.snapshot.curboxBlocks.size == 1 && record.snapshot.distractingApps == listOf("com.example.secretapp"))
        val edited = profile.copy(lmpProfile = null, curboxFocusGroup = null, curboxBlocks = emptyList(), distractingApps = emptyList())
        report.check("ps3a/snapshot (S3): a later edit of the saved profile changes nothing in the running record", record.snapshot.enforcement == profile.enforcement && record.snapshot.enforcement != edited.enforcement)
        report.check("ps3a/snapshot (S3): the enforcement the lock seam reads is the snapshot's: LockMyPhone profile, Curbox group, blocks on", record.snapshot.enforcement == OwnSessionEnforcement.Profile("Deep lock", false, "group-9", true))
        report.check("ps3a/snapshot (S3): a session with no copy (the built-in profile) turns nothing on", (PhoneSessionEngine.start("s-2", "b", "Focus", 25, t0) as PhoneSessionResult.Done).record.snapshot.enforcement == OwnSessionEnforcement.Profile.NONE)

        val round = PhoneSessionCodec.read(PhoneSessionCodec.write(PhoneSessionStoreData(log = listOf(record.copy(endedAt = at(50), endKind = PhoneSessionEndKind.TIME_UP)))))
        report.check("ps3a/snapshot (S3): the copy survives the sealed log's round trip", round?.log?.single()?.snapshot == profile.snapshot())
        report.check("ps3a/snapshot (R14): the copy's text never carries a package name into a log line (toString)", !record.snapshot.toString().contains("secretapp") && !record.toString().contains("secretapp"))

        val world = Ps2aSelfCheck.World()
        val started = world.controller.start(profile.id, profile.name, 50, snapshot = profile.snapshot())
        val after = world.restart()
        report.check("ps3a/snapshot (S3): started through the controller, the copy is in the record and survives the process dying", started is PhoneSessionStart.Started && world.controller.running?.snapshot == profile.snapshot() && after.running?.snapshot == profile.snapshot())
        report.check("ps3a/snapshot: without a copy the controller starts a plain timer", Ps2aSelfCheck.World().also { it.controller.start("b", "Focus", 25) }.controller.running?.snapshot == PhoneSessionProfileSnapshot.NONE)
    }

    // --- PS2c review S4 / Q1199: the key is the session's start ---------------------------------------------------------------------

    private fun key(report: CheckReport) {
        val record = running()
        val paused = (PhoneSessionEngine.pause(record, at(10)) as PhoneSessionResult.Done).record
        val resumed = (PhoneSessionEngine.resume(paused, at(20)) as PhoneSessionResult.Done).record
        report.check("ps3a/key (S4): a Resume moves the phase's start but not the session's", resumed.phaseStartedAt != resumed.startedAt && resumed.startedAt == t0)
        report.check("ps3a/key (S4): the key is the session's start, before and after the Resume (no churn)", OwnSessionEnforcement.keyOf(record) == t0 && OwnSessionEnforcement.keyOf(resumed) == t0)
        val extended = (PhoneSessionEngine.add10(resumed, at(25)) as PhoneSessionResult.Done).record
        report.check("ps3a/key (S4): +10 minutes and a step away don't move it either", OwnSessionEnforcement.keyOf(extended) == t0 && OwnSessionEnforcement.keyOf((PhoneSessionEngine.stepAway(extended, at(26)) as PhoneSessionResult.Done).record) == t0)
        val second = (PhoneSessionEngine.start("s-2", "p", "Focus", 50, at(120)) as PhoneSessionResult.Done).record
        report.check("ps3a/key (S4): another session has another key", OwnSessionEnforcement.keyOf(second) != OwnSessionEnforcement.keyOf(record))
    }

    // --- B6, R9: "All tasks done" is wired to the checklist ------------------------------------------------------------------------

    private val created = OffsetDateTime.of(2026, 11, 16, 8, 0, 0, 0, ZoneOffset.UTC)
    private const val SELF = "aaaaaaaa-0000-0000-0000-0000000000f0"

    private fun item(id: String, n: Int, bin: TaskBin = TaskBin.SESSION) =
        SyncTaskItem(TaskItem(id, "Task $n", bin, n.toDouble(), created, created, created, number = TaskNumber(100 + n)), TaskFieldStamps.allAt(FieldStamp(created, SELF)))

    private fun lists(vararg items: SyncTaskItem) = TaskLists(items.toList(), emptyList(), emptyList(), emptyList())

    private fun apply(record: PhoneSessionRecord, lists: TaskLists): PhoneSessionRecord =
        (PhoneSessionEngine.checklistChanged(record, SessionTasksFromModel.whileRunning(lists, record.checklist)) as PhoneSessionResult.Done).record

    private fun allDone(report: CheckReport) {
        val l0 = lists(item(GUID_A, 1), item(GUID_B, 2), item(GUID_C, 3), item("eeeeeeee-0000-0000-0000-00000000000e", 9, TaskBin.QUEUED))
        val atStart = SessionTasksFromModel.atStart(l0)
        report.check("ps3a/all-done (B6): a session starts with This session's open tasks only (not Queued), in order", atStart.map { it.id } == listOf(GUID_A, GUID_B, GUID_C))
        var record = (PhoneSessionEngine.start("s-1", "p", "Focus", 50, t0, atStart) as PhoneSessionResult.Done).record
        report.check("ps3a/all-done (R9): the checklist was made before the start", record.checklist.madeBeforeStart && record.checklist.tasks.size == 3 && !record.checklist.allDone)

        // Ticks on Today move a task to Finished in the shared model; the list follows.
        var lists = l0
        for (id in listOf(GUID_A, GUID_B)) {
            lists = ShelfEdits.finish(lists, id, SELF, created.plusMinutes(5)).lists
            record = apply(record, lists)
        }
        report.check("ps3a/all-done (B6): ticks reach the checklist: two of three done, none dropped", record.checklist.tasks.count { it.isDone } == 2 && record.checklist.droppedOpen == 0 && !record.checklist.allDone)
        report.check("ps3a/all-done (B6): open tasks first in the model's order, finished ones after as they were ticked", record.checklist.tasks.map { it.id } == listOf(GUID_C, GUID_A, GUID_B))
        lists = ShelfEdits.finish(lists, GUID_C, SELF, created.plusMinutes(6)).lists
        record = apply(record, lists)
        report.check("ps3a/all-done (R9): every task ticked: all done, but not before 10 minutes", record.checklist.allDone && !PhoneSessionEngine.endAtOnce(record, at(9)).let { it is PhoneSessionResult.Done })
        val ended = PhoneSessionEngine.endAtOnce(record, at(10)) as? PhoneSessionResult.Done
        report.check("ps3a/all-done (R9): at 10 minutes it ends at once as All tasks done", ended != null && ended.record.endKind == PhoneSessionEndKind.ALL_TASKS_DONE && !ended.record.isRunning)

        // Taking a task off This session open is a drop: ticking the rest never ends it at once.
        var r2 = (PhoneSessionEngine.start("s-2", "p", "Focus", 50, t0, atStart) as PhoneSessionResult.Done).record
        val removal = ShelfEdits.move(l0, listOf(GUID_B), TaskBin.QUEUED, SELF, created.plusMinutes(2))
        r2 = apply(r2, removal.lists)
        report.check("ps3a/all-done (B6): a task moved out of This session open is counted as dropped", r2.checklist.droppedOpen == 1 && r2.checklist.tasks.map { it.id } == listOf(GUID_A, GUID_C))
        var l2 = removal.lists
        for (id in listOf(GUID_A, GUID_C)) {
            l2 = ShelfEdits.finish(l2, id, SELF, created.plusMinutes(5)).lists
            r2 = apply(r2, l2)
        }
        report.check("ps3a/all-done (B6): ... so ticking what is left never allows the instant end", r2.checklist.tasks.all { it.isDone } && !r2.checklist.allDone && (PhoneSessionEngine.endAtOnce(r2, at(30)) as? PhoneSessionResult.Refused)?.reason == PhoneSessionRefusal.NOT_ALL_DONE)

        // Undo gives the drop back, only through the Undo path.
        var r3 = (PhoneSessionEngine.start("s-3", "p", "Focus", 50, t0, atStart) as PhoneSessionResult.Done).record
        r3 = apply(r3, removal.lists)
        val undone = ShelfEdits.restore(removal.lists, removal.before, ShelfEdits.MOVE_FIELDS, SELF, created.plusMinutes(3)).lists
        val plain = apply(r3, undone)
        report.check("ps3a/all-done (B6): the task coming back by an ordinary change does NOT give the drop back", plain.checklist.droppedOpen == 1 && plain.checklist.tasks.size == 3)
        val viaUndo = (PhoneSessionEngine.checklistUndo(r3, SessionTasksFromModel.whileRunning(undone, r3.checklist)) as PhoneSessionResult.Done).record
        report.check("ps3a/all-done (B6): the Undo path gives it back (Undo of a removed open task)", viaUndo.checklist.droppedOpen == 0 && viaUndo.checklist.tasks.map { it.id } == listOf(GUID_A, GUID_B, GUID_C))

        // A task added during the session joins the list and must be ticked too.
        var r4 = (PhoneSessionEngine.start("s-4", "p", "Focus", 50, t0, atStart) as PhoneSessionResult.Done).record
        var l4 = lists(item(GUID_A, 1), item(GUID_B, 2), item(GUID_C, 3), item(GUID_D, 4))
        r4 = apply(r4, l4)
        report.check("ps3a/all-done (B6): a task added to This session during the session joins the list, at the end", r4.checklist.tasks.map { it.id } == listOf(GUID_A, GUID_B, GUID_C, GUID_D))
        for (id in listOf(GUID_A, GUID_B, GUID_C)) {
            l4 = ShelfEdits.finish(l4, id, SELF, created.plusMinutes(5)).lists
            r4 = apply(r4, l4)
        }
        report.check("ps3a/all-done (R9): ... and it has to be ticked too before the instant end", !r4.checklist.allDone)

        // The list that is not loaded yet is never "everything was removed".
        val unloaded = SessionTasksFromModel.whileRunning(TaskLists.EMPTY, record.checklist)
        report.check("ps3a/all-done (B6): the guard that matters is the bridge's (an empty, unloaded list would read as a drop of everything): see the scan below", unloaded.isEmpty())

        // What a tap on a row means.
        val rows = listOf(SessionTaskItem(GUID_A, "A", null, listOf(SessionTaskItem("aaaaaaaa-1111-0000-0000-000000000001", "step", null, emptyList()))), SessionTaskItem(GUID_B, "B", null, emptyList()))
        report.check("ps3a/all-done: a tap on a task row ticks the task, on a step row the step, on an unknown row nothing",
            SessionTasksFromModel.tickOf(rows, GUID_B) == SessionTasksFromModel.Tick.OfTask(GUID_B) &&
                SessionTasksFromModel.tickOf(rows, "aaaaaaaa-1111-0000-0000-000000000001") == SessionTasksFromModel.Tick.OfStep(GUID_A, "aaaaaaaa-1111-0000-0000-000000000001") &&
                SessionTasksFromModel.tickOf(rows, "nope") == null && SessionTasksFromModel.tickOf(rows, null) == null)
    }

    // --- N4: the mode-switch words -------------------------------------------------------------------------------------------------

    private fun texts(report: CheckReport) {
        val pc = PhoneSessionTexts.modeSwitchLocksLine(false)
        val phone = PhoneSessionTexts.modeSwitchLocksLine(true)
        report.check("ps3a/texts (N4): a PC session keeps its locks either way; a phone session's line says what switching now changes", pc.startsWith("The session keeps its locks either way") && !phone.contains("keeps its locks either way") &&
            phone.contains("only what this session's profile turns on") && phone.contains("ending the session becomes free"))
        report.check("ps3a/texts: every refusal has words, none of them a reason text", PhoneSessionRefusal.values().all { PhoneSessionTexts.refusal(it).isNotBlank() })
    }

    // --- The Compose files can't run here: the wiring is pinned by what they say ----------------------------------------------------

    private fun app(path: String): String? =
        listOf(java.io.File("app/src/main/java/com/accountabilibuddy/companion/$path"), java.io.File("../app/src/main/java/com/accountabilibuddy/companion/$path"))
            .firstOrNull { it.isFile }?.readText()?.replace("\r\n", "\n")?.let(Sync1cCheck::code)

    private fun sources(report: CheckReport) {
        val newUi = listOf("ui/today/StartSheet.kt", "ui/today/EndPhoneSessionSheet.kt", "ui/today/OwnSessionView.kt", "ui/today/OwnSessionMenuSheet.kt", "ui/today/GroupLive.kt", "ui/today/TodayActionBar.kt", "ui/PhoneSessionSetupPage.kt")
        val ui = newUi.associateWith { app(it) }
        report.check("ps3a/sources: every file of the new UI was found", ui.values.all { it != null }, ui.filterValues { it == null }.keys.toString())
        report.check("ps3a/sources (12-hour): no 24-hour pattern in the new UI; times come from ClockFormat.time",
            ui.values.filterNotNull().all { text -> !Regex("""HH:mm|"H:mm|is24Hour\s*=\s*true|is24HourFormat|ofPattern\("H""").containsMatchIn(text) } && ui["ui/today/EndPhoneSessionSheet.kt"]!!.contains("clock.time(") && ui["ui/today/StartSheet.kt"]!!.contains("clock.time("))
        report.check("ps3a/sources (R12): no wall clock in the new session UI or the new session files (the sheet ticks on the session's clock)",
            (ui.values.filterNotNull() + listOfNotNull(app("session/PhoneSessionStarter.kt"), app("session/PhoneSessionTasks.kt"))).all { !it.contains("Instant.now()") && !it.contains("System.currentTimeMillis") && !it.contains("LocalDateTime.now") && !it.contains("SystemClock") })
        val hero = app("ui/TodayHero.kt").orEmpty()
        report.check("ps3a/sources (R12): the card's second tick is the session's clock while the phone's own session runs, the phone's clock otherwise",
            hero.contains("fun displayNow(app: CompanionApp): Instant =") && hero.contains("if (app.phoneSession.realSessionRunning()) app.phoneSession.controller.now() else Instant.now()") && hero.contains("now.value = displayNow(app)") &&
                Regex("""Instant\.now\(\)""").findAll(hero).count() == 1)

        val pane = app("ui/TodayPane.kt").orEmpty()
        report.check("ps3a/sources (R1): Today opens the Start sheet only while nothing blocks it and no session of the phone's own runs", pane.contains("if (starting && block == null && ownSession == null) {\n        StartSessionSheetHost(") && pane.contains("LaunchedEffect(block != null) { if (block != null) starting = false }"))
        val phoneToday = app("ui/PhoneToday.kt").orEmpty()
        report.check("ps3a/sources (R1): the Start bar shows only when canStartHere(block); a live session elsewhere takes its place with the Running on bar, which does nothing",
            phoneToday.contains("} else if (canStartHere(block)) {\n                StartActionBar(onStart,") && phoneToday.contains("} else if (block is StartBlock.RunningElsewhere) {") && !app("ui/today/TodayActionBar.kt").orEmpty().substringAfter("fun RunningElsewhereBar").substringBefore("fun MoreButton").contains("onClick"))
        report.check("ps3a/sources (R1, goals): the goal menu's Start opens the same sheet, under the same block; \"Phone sessions are coming\" is gone everywhere",
            app("ui/MainScreen.kt").orEmpty().contains("if (goalStartOpen && goalBlock == null) {\n                        com.accountabilibuddy.companion.ui.today.StartSessionSheetHost(") &&
                app("ui/goals/GoalMenuSheet.kt").orEmpty().contains("GoalMenuItemId.StartSession -> ({ start?.onOpenSheet?.invoke(); onDismiss() })") &&
                listOf("ui/TodayHero.kt", "ui/goals/GoalSessionStart.kt", "ui/goals/GoalMenuSheet.kt", "ui/PhoneToday.kt").all { !app(it).orEmpty().contains("Phone sessions are coming") })
        report.check("ps3a/sources (B15): a waiting start request blocks the phone's start with its Cancel line (the hero's WaitingLine)", pane.contains("waitingFor = pc.waiting?.let { pc.startName }") && hero.contains("block is StartBlock.WaitingForPc && pc.waiting != null -> WaitingLine(pc, clock, onCancelWaiting)"))

        val sheet = ui["ui/today/StartSheet.kt"].orEmpty()
        report.check("ps3a/sources (R16, R15): the sheet opens on defaultTarget(remembered, PC) and remembers each choice; with no PC only 'On this phone' shows",
            sheet.contains("PhoneStartRules.defaultTarget(starter.rememberedTarget(), startPc)") && sheet.contains("starter.remember(target)") && sheet.contains("PhoneStartRules.offersPc(startPc)") && sheet.contains("\"On this phone\""))
        report.check("ps3a/sources (R16): the PC panel starts nothing while the PC is not online (profile chips, Start and both quick starts are off)", sheet.contains("val canStart = startPc.online") && sheet.contains("enabled = canStart && profileId != null") && sheet.contains("enabled = canStart)"))
        val starter = app("session/PhoneSessionStarter.kt").orEmpty()
        report.check("ps3a/sources (S3): the phone start copies the profile, hands over This session's tasks, and is never a test", starter.contains("snapshot = profile.snapshot()") && starter.contains("isTest = false") && starter.contains("SessionTasksFromModel.atStart(app.taskModel.lists.value)"))

        val profiles = app("session/PhoneSessionProfiles.kt").orEmpty()
        val enforcementOf = profiles.substringAfter("fun enforcementOf").substringBefore("\n\n")
        report.check("ps3a/sources (S3, N6): the enforcement of a running session comes from its record's copy, never the live store: enforcementOf, Curbox's blocks and the usage's apps",
            enforcementOf.contains("record.snapshot.enforcement") && !enforcementOf.contains("byId") && !app("curbox/CurboxSessionBlocks.kt").orEmpty().contains("phoneSessionProfiles") && !app("session/PhoneSessionUsage.kt").orEmpty().contains("phoneSessionProfiles") &&
                app("curbox/CurboxSessionBlocks.kt").orEmpty().contains("record?.snapshot?.curboxBlocks") && app("session/PhoneSessionUsage.kt").orEmpty().contains("record.snapshot.distractingApps"))
        val writers = java.io.File("app/src/main/java/com/accountabilibuddy/companion").takeIf { it.isDirectory } ?: java.io.File("../app/src/main/java/com/accountabilibuddy/companion")
        val prefsFiles = writers.walkTopDown().filter { it.isFile && it.extension == "kt" && Sync1cCheck.code(it.readText()).contains("\"phoneSessionProfiles\"") }.map { it.name }.toList()
        report.check("ps3a/sources (S3): the profiles' prefs are named in one file only (PhoneSessionProfiles.replaceAll is the only writer)", prefsFiles == listOf("PhoneSessionProfiles.kt"), prefsFiles.toString())

        val essentials = app("essentials/EssentialsLaunch.kt").orEmpty()
        report.check("ps3a/sources (S4, Q1199): the own session's key in every lock decision is the session's start, never the phase's", essentials.contains("OwnSessionEnforcement.keyOf(it) to it.effectiveEnd(now)") && !essentials.substringAfter("fun ownSessionStart").substringBefore("\n\n").contains("phaseStartedAt"))

        val tasks = app("session/PhoneSessionTasks.kt").orEmpty()
        report.check("ps3a/sources (B6): the bridge follows the task lists only while a real session runs and only once the lists are loaded (an unloaded list is never 'all removed')",
            tasks.contains("if (isReal(record)) app.taskModel.lists else flowOf(null)") && tasks.split("if (!app.taskModel.isActive) return").size == 3 && tasks.contains("controller.thisSessionChanged(list)") && tasks.contains("controller.thisSessionUndone("))
        report.check("ps3a/sources (B6): the runtime starts the bridge, and every Undo of the task list calls it", app("session/PhoneSessionRuntime.kt").orEmpty().contains("tasks.start(scope)") &&
            app("ui/tasks/page/ShelfController.kt").orEmpty().contains("ShelfEdits.restore(lists, result.before, fields, self, now) }\n        app.phoneSession.tasks.undone()"))
        report.check("ps3a/sources (R9): a tick on the phone's own session goes through the shared task model (finish / tickStep), where the checklist follows", ui["ui/today/OwnSessionView.kt"].orEmpty().contains("controller.finish(tick.taskId)") && ui["ui/today/OwnSessionView.kt"].orEmpty().contains("controller.tickStep(tick.taskId, tick.stepId)"))

        val end = ui["ui/today/EndPhoneSessionSheet.kt"].orEmpty()
        report.check("ps3a/sources (R8): the End sheet opens the wait with openEnd, drops an unconfirmed End on close, confirms only through the presenter's buttons, and is never offered a free end in AccountabiliBuddy",
            end.contains("controller.openEnd()") && end.contains("if (controller.running?.armedEnd == null) controller.cancelEnd()") && end.contains("EndSheetAction.CONFIRM, EndSheetAction.END -> controller.endEarly(reason)") && end.contains("EndSheetAction.END_NOW -> controller.endEarly(null)") &&
                end.contains("EndSheetAction.ALL_DONE -> controller.endAtOnce()") && end.contains("PhoneEndSheet.state(shown, mode, app.phoneSession.endSettings(), now, reason, lmpUntil, time)") && !end.contains("endOnCommand"))
        report.check("ps3a/sources (Q1193): the End sheet offers the LockMyPhone-Extended update from the installer", end.contains("EssentialsLaunch.lockOutlivesSession(context, shown, controller.now())") && end.contains("LockMyPhoneInstaller.download(context)"))
        report.check("ps3a/sources (R8): the End sheet is the only way out of the menu: End session opens it, there is no other end call in the Today files",
            ui["ui/today/OwnSessionMenuSheet.kt"].orEmpty().contains("onClick = { onDismiss(); onEnd() }") && listOf("ui/today/OwnSessionMenuSheet.kt", "ui/today/OwnSessionView.kt", "ui/today/StartSheet.kt").all { path -> val text = ui[path].orEmpty(); !text.contains("endEarly(") && !text.contains("endAtOnce(") && !text.contains("endOnCommand(") })

        report.check("ps3a/sources (N4): the mode-switch dialog says the phone session's words", app("ui/GuidanceSection.kt").orEmpty().contains("PhoneSessionTexts.modeSwitchLocksLine(phoneSession)") && !app("ui/GuidanceSection.kt").orEmpty().contains("Note(\"The session keeps its locks either way"))
        report.check("ps3a/sources (R8): the armed end of the phone's own session is a wait Today lists and the pinned header can cancel, free", app("notify/RunningWaits.kt").orEmpty().contains("?.armedEnd?.let { armed ->") && app("notify/RunningWaits.kt").orEmpty().contains("app.phoneSession.controller.cancelEnd()"))
        val features = app("setup/SetupFeatures.kt").orEmpty()
        report.check("ps3a/sources (SetupFeatures): session.onPhone arrives in 0.1.80 with its check and its page", features.contains("const val PHONE_SESSIONS = \"session.onPhone\"") && features.contains("PHONE_SESSIONS, \"0.1.80\"") && features.contains("app.phoneSession.starter.lastProfileId() != null") &&
            app("ui/WhatsNewScreen.kt").orEmpty().contains("SetupFeatures.PHONE_SESSIONS -> PhoneSessionSetupPage()"))
        report.check("ps3a/sources (R6): nothing here ends, pauses or extends another device's session: no new sync action and no PC command in the new UI",
            ui.values.filterNotNull().all { !it.contains("SyncAction") && !it.contains("pcRemote") } )
    }
}
