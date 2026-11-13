package com.accountabilibuddy.checks

import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.companion.sync.SyncCarry
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.RelayBusyException
import com.accountabilibuddy.shared.sync.RelayRequestPacer
import com.accountabilibuddy.shared.sync.RelayRereadDownload
import com.accountabilibuddy.shared.sync.RelayRereadFile
import com.accountabilibuddy.shared.sync.RelayRereadPort
import com.accountabilibuddy.shared.sync.RelayRereader
import com.accountabilibuddy.shared.sync.SyncBoard
import com.accountabilibuddy.shared.sync.SyncBoardBuilder
import com.accountabilibuddy.shared.sync.SyncBoardWords
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncDiagnosticsReport
import com.accountabilibuddy.shared.sync.SyncDotColor
import com.accountabilibuddy.shared.sync.SyncDotReason
import com.accountabilibuddy.shared.sync.SyncDayPlan
import com.accountabilibuddy.shared.sync.SyncDocument
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncFileCodec
import com.accountabilibuddy.shared.sync.SyncFolder
import com.accountabilibuddy.shared.sync.SyncGoalParts
import com.accountabilibuddy.shared.sync.SyncKey
import com.accountabilibuddy.shared.sync.SyncLog
import com.accountabilibuddy.shared.sync.SyncLogDirection
import com.accountabilibuddy.shared.sync.SyncNextTask
import com.accountabilibuddy.shared.sync.SyncReceiptParts
import com.accountabilibuddy.shared.sync.SyncReceiptsLocal
import com.accountabilibuddy.shared.sync.SyncReceipt
import com.accountabilibuddy.shared.sync.SyncReceiveHooks
import com.accountabilibuddy.shared.sync.SyncReceiveParts
import com.accountabilibuddy.shared.sync.SyncSendLog
import com.accountabilibuddy.shared.sync.SyncStalePolicy
import com.accountabilibuddy.shared.sync.SyncSteps
import com.accountabilibuddy.shared.sync.SyncUnreadableFile
import com.accountabilibuddy.shared.sync.SyncUnreadableFiles
import com.accountabilibuddy.shared.tasks.SyncTaskItem
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskConflict
import com.accountabilibuddy.shared.tasks.TaskConflictPick
import com.accountabilibuddy.shared.tasks.TaskConflictPicker
import com.accountabilibuddy.shared.tasks.TaskFieldStamps
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskListRules
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskNumber
import java.io.File
import java.io.IOException
import java.time.Duration
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset
import java.util.UUID

/**
 * SYNC1c (phone sync troubleshooting, ADR 86), the checks that mirror the PC's `Sync1bChecks`, over the pure code the phone's screen and sources
 * are built on: the confirmation board, the send log, what each part of a received file came to, the unreadable list and Ignore, the stale-file
 * rule (own names only, never the relay), the relay's paced re-read (30 requests a minute, ended by a 429, idempotent), the sync log and the
 * report (no private text), the conflict picker. The picker's parity with the PC's `TaskActions` is checked by [Sync1aCheck] over the same
 * fixtures (`sync1a/keep-both.json`), through [TaskConflictPicker].
 */
object Sync1cCheck {
    private val T0: OffsetDateTime = OffsetDateTime.parse("2026-11-13T10:00:00Z")
    private val SELF: UUID = UUID.fromString("aaaaaaaa-0000-0000-0000-000000000001")
    private val PC: UUID = UUID.fromString("bbbbbbbb-0000-0000-0000-000000000002")
    private val PHONE2: UUID = UUID.fromString("cccccccc-0000-0000-0000-000000000003")

    // Text that must never reach the log, the report or an exported line.
    private const val SECRET_TITLE = "Quarterly-tax-return-SECRET"
    private const val SECRET_NOTE = "ask-the-accountant-NOTE"
    private const val SECRET_DEVICE = "Fabs-Pixel-9-Pro"
    private const val SECRET_RELAY = "https://relay.example.invalid:8443"
    private const val SECRET_FOLDER = "C:/Users/fabsuser/SyncStuff"
    private const val SECRET_EMAIL = "fab@example.invalid"
    private const val SECRET_FILE = "0a1b2c3d4e5f.absync"

    private fun card(id: UUID, name: String, kind: String, at: OffsetDateTime = T0, seen: Map<UUID, Instant> = emptyMap()) =
        SyncDeviceCard(id, name, kind, "0.1.78", at.toInstant(), emptyList(), false, seen)

    private fun file(card: SyncDeviceCard, receipts: List<SyncReceipt>? = null, steps: SyncSteps? = null, nextTasks: List<SyncNextTask>? = null) =
        SyncFile(1, card, emptyList(), emptyList(), null, emptyList(), steps = steps, nextTasks = nextTasks, receipts = receipts)

    private fun task(n: Int) = SyncTaskItem(
        TaskItem("9b9b9b9b-0000-0000-0000-%012d".format(n), "Task $n", TaskBin.SORT, n.toDouble(), T0, T0, T0, number = TaskNumber(100 + n)),
        TaskFieldStamps.allAt(FieldStamp(T0, SELF.toString())),
    )

    fun run(report: CheckReport) {
        board(report)
        sendLog(report)
        receiveParts(report)
        unreadable(report)
        stalePolicy(report)
        relayReread(report)
        log(report)
        picker(report)
        structure(report)
        Sync1cFixCheck.run(report)
    }

    // ---- the confirmation board

    private fun board(report: CheckReport) {
        val now = T0.plusMinutes(11)
        val sent = SyncSendLog().record(SyncReceiptParts.DRIVE, T0, T0.plusSeconds(1))
        // The PC confirms this phone's newest file by Drive; its receipt has no folder entry, and it has never read the folder either.
        val receipt = SyncReceipt(SELF, T0, byMethod = linkedMapOf(SyncReceiptParts.DRIVE to T0))
        val pcFile = file(card(PC, "The PC", SyncDeviceCard.KIND_PC, T0.plusMinutes(10)), receipts = listOf(receipt))
        val board = SyncBoardBuilder.build(
            now, SELF, listOf(SyncDeviceCard.METHOD_DRIVE, SyncDeviceCard.METHOD_FOLDER, SyncDeviceCard.METHOD_RELAY), listOf(pcFile), SyncReceiptsLocal(), sent,
        ) { null }
        report.check(
            "sync1c/board: the methods in use come in the board's order (folder, Drive, relay) with a row for the other device",
            board.methods.map { it.method } == listOf(SyncReceiptParts.FOLDER, SyncReceiptParts.DRIVE, SyncReceiptParts.RELAY) && board.methods.all { it.devices.size == 1 },
            board.methods.map { it.method }.toString(),
        )
        val drive = board.methods.first { it.method == SyncReceiptParts.DRIVE }.devices.single()
        report.check("sync1c/board: a device that confirms my newest file by Drive is green", drive.dot.color == SyncDotColor.GREEN && drive.dot.reason == SyncDotReason.CONFIRMED)
        report.check("sync1c/board: the time of my file it confirmed is shown, and its ordinal is \"PC 1\"", drive.confirmedFile == T0 && drive.ordinal == "PC 1")
        val folder = board.methods.first { it.method == SyncReceiptParts.FOLDER }.devices.single()
        report.check("sync1c/board: a way the device has no record of is grey \"not used\", never red", folder.dot.color == SyncDotColor.GREY, folder.dot.reason.toString())
        report.check("sync1c/board: every device has the eight items under it", drive.parts.map { it.code } == SyncBoardBuilder.SHOWN_PARTS)

        // Red: the receiver wrote 10 minutes after my file reached Drive and still confirms an older file only.
        val stale = file(card(PC, "The PC", SyncDeviceCard.KIND_PC, T0.plusMinutes(10)), receipts = listOf(SyncReceipt(SELF, T0.minusHours(1), byMethod = linkedMapOf(SyncReceiptParts.DRIVE to T0.minusHours(1)))))
        val red = SyncBoardBuilder.build(now, SELF, listOf(SyncDeviceCard.METHOD_DRIVE), listOf(stale), SyncReceiptsLocal(), sent) { Duration.ofMinutes(15) }
            .methods.single().devices.single()
        report.check("sync1c/board: red only when the device wrote after my file went out and did not confirm it", red.dot.color == SyncDotColor.RED && red.dot.reason == SyncDotReason.NOT_CONFIRMED, red.dot.toString())
        // Yellow: it has not written since my file reached Drive.
        val quiet = file(card(PC, "The PC", SyncDeviceCard.KIND_PC, T0.minusMinutes(5)), receipts = listOf(SyncReceipt(SELF, T0.minusHours(1), byMethod = linkedMapOf(SyncReceiptParts.DRIVE to T0.minusHours(1)))))
        val yellow = SyncBoardBuilder.build(now, SELF, listOf(SyncDeviceCard.METHOD_DRIVE), listOf(quiet), SyncReceiptsLocal(), sent) { Duration.ofMinutes(15) }
            .methods.single().devices.single()
        report.check("sync1c/board: a device that has not written since my file went out is yellow \"waiting\", never red", yellow.dot.color == SyncDotColor.YELLOW && yellow.dot.reason == SyncDotReason.WAITING)

        // Works alone: no method, and methods with nobody else, are never red.
        val none = SyncBoardBuilder.build(now, SELF, emptyList(), emptyList(), SyncReceiptsLocal(), SyncSendLog()) { null }
        report.check("sync1c/board: with no way of syncing it is \"Not syncing\" with no rows", none.notSyncing && none.methods.isEmpty() && !none.noOtherDevices)
        val alone = SyncBoardBuilder.build(now, SELF, listOf(SyncDeviceCard.METHOD_RELAY), emptyList(), SyncReceiptsLocal(), SyncSendLog()) { null }
        report.check("sync1c/board: with a way on and nobody else it says \"No other devices yet\", no red", alone.noOtherDevices && alone.methods.single().devices.isEmpty())
        // A watch is reached through its phone: grey, not red, even though it never writes receipts.
        val watch = file(card(UUID.fromString("dddddddd-0000-0000-0000-000000000004"), "Watch", SyncDeviceCard.KIND_WATCH, T0.plusMinutes(10)))
        val watchRow = SyncBoardBuilder.build(now, SELF, listOf(SyncDeviceCard.METHOD_DRIVE), listOf(watch), SyncReceiptsLocal(), sent) { null }.methods.single().devices.single()
        report.check("sync1c/board: a watch is grey (reached through its phone), never red", watchRow.dot.color == SyncDotColor.GREY && watchRow.dot.reason == SyncDotReason.WATCH)
        // Ordinals: by kind, then a stable order; the device's own name is never an ordinal.
        val ordinals = SyncBoardBuilder.ordinals(listOf(card(PC, "n1", SyncDeviceCard.KIND_PC), card(PHONE2, "n2", SyncDeviceCard.KIND_PHONE), card(SELF, "n3", SyncDeviceCard.KIND_PHONE)))
        report.check("sync1c/board: devices are \"PC 1\", \"Phone 1\", \"Phone 2\" (a stable order)", ordinals[PC] == "PC 1" && ordinals.values.toSet() == setOf("PC 1", "Phone 1", "Phone 2"), ordinals.toString())
        // Colour is never the only signal: every reason has words, every colour a word.
        report.check(
            "sync1c/board: every reason has a sentence and every colour a word (colour is never the only signal)",
            SyncDotReason.values().all { SyncBoardWords.words(com.accountabilibuddy.shared.sync.SyncDot(SyncDotColor.GREY, it)).isNotBlank() } && SyncDotColor.values().all { SyncBoardWords.colorWord(it).isNotBlank() },
        )
        report.check("sync1c/board: times read in 12 hours (4:05 PM)", SyncBoardWords.clock(OffsetDateTime.parse("2026-11-13T16:05:00Z"), OffsetDateTime.parse("2026-11-13T17:00:00Z"), ZoneId.of("UTC")) == "4:05 PM")
    }

    // ---- the send log

    private fun sendLog(report: CheckReport) {
        var log = SyncSendLog()
        for (i in 0 until 30) log = log.record(SyncReceiptParts.RELAY, T0.plusMinutes(i.toLong()), T0.plusMinutes(i.toLong()).plusSeconds(1))
        report.check("sync1c/sendlog: only the newest 24 puts per way are kept", log.entries.getValue(SyncReceiptParts.RELAY).size == SyncSendLog.MAX_PER_METHOD)
        report.check("sync1c/sendlog: an unknown way is ignored", log.record("carrier-pigeon", T0, T0) == log)
        val back = SyncSendLog.fromJson(kotlinx.serialization.json.Json.parseToJsonElement(log.toJson().toString()))
        report.check("sync1c/sendlog: it round-trips through its file text", back == log)
        val facts = log.facts(T0.plusMinutes(10).plusSeconds(30))
        report.check(
            "sync1c/sendlog: the facts for a receiver give my newest file and, judged against the receiver's own write time, the newest that had gone out by then",
            facts != null && facts.newest == T0.plusMinutes(29) && facts.previousSentAt?.get(SyncReceiptParts.RELAY) == T0.plusMinutes(10), facts?.previousSentAt.toString(),
        )
        report.check("sync1c/sendlog: nothing sent yet gives no facts", SyncSendLog().facts(T0) == null)
        report.check("sync1c/sendlog: garbage in the file reads as an empty log", SyncSendLog.fromJson(kotlinx.serialization.json.JsonPrimitive("x")).entries.isEmpty())
    }

    // ---- what each part of a received file came to

    private fun receiveParts(report: CheckReport) {
        val tasks = TaskListRules.fileParts(TaskLists(listOf(task(1)), emptyList(), emptyList(), emptyList()), T0, ZoneId.of("UTC")).second
        val full = SyncFile(
            1, card(PC, "The PC", SyncDeviceCard.KIND_PC), emptyList(), emptyList(), SyncDayPlan("{}", "v1", T0.toInstant(), null), listOf(SyncDocument("sleep", "{}", T0.toInstant())),
            steps = SyncSteps(8000, T0.toInstant(), emptyList()), nextTasks = emptyList(), taskParts = tasks, goalParts = SyncGoalParts(listOf(Gs4SelfCheck.goal(1)), null, null, null, null),
        )
        var calls = 0
        val ready = SyncReceiveHooks(true, false, onSharedLists = { calls++ }, onTaskExtras = { calls++ }, onTaskParts = { calls++ }, onGoalParts = { calls++ })
        val all = SyncReceiveParts.outcome(full, ready)
        report.check(
            "sync1c/parts: every part in the file is taken when the stores are ready (each merge ran once)",
            all["tasks"] == "taken" && all["oldLists"] == "taken" && all["goals"] == "taken" && all["goalHistory"] == "taken" && all["steps"] == "taken" && all["dayPlan"] == "taken" &&
                all["settings"] == "taken" && all["sessions"] == "taken" && calls == 4,
            "$all calls=$calls",
        )
        report.check("sync1c/parts: a part the file does not carry is absent", all["lunch"] == "absent" && all["actions"] == "absent")

        calls = 0
        val off = SyncReceiveParts.outcome(full, SyncReceiveHooks(false, false, onSharedLists = { calls++ }, onTaskExtras = { calls++ }, onTaskParts = { calls++ }, onGoalParts = { calls++ }))
        report.check(
            "sync1c/parts: with this phone's task-list switch off the lists are \"off\" and nothing merged",
            off["tasks"] == "off" && off["oldLists"] == "off" && off["goals"] == "off" && off["goalHistory"] == "off" && calls == 0, "$off calls=$calls",
        )
        val ahead = SyncReceiveParts.outcome(full, SyncReceiveHooks(true, true, onTaskParts = { calls++ }, onGoalParts = { calls++ }))
        report.check("sync1c/parts: a file from a clock more than a minute ahead is refused for its lists and nothing merged", ahead["tasks"] == "refused" && ahead["goals"] == "refused" && calls == 0)

        // Q904: a merge that throws is "refused" in the receipt, the others still run.
        val broken = SyncReceiveParts.outcome(full, SyncReceiveHooks(true, false, onTaskParts = { throw IOException("the store broke") }))
        report.check("sync1c/parts: a task merge that threw is refused, the goals part is still taken", broken["tasks"] == "refused" && broken["goals"] == "taken", broken.toString())
        val closed = SyncReceiveParts.outcome(full, SyncReceiveHooks(true, false, tasksReady = { false }, oldListsReady = { false }, goalsReady = { false }))
        report.check("sync1c/parts: a closed task list or goal store refuses its parts", closed["tasks"] == "refused" && closed["oldLists"] == "refused" && closed["goals"] == "refused" && closed["goalHistory"] == "refused")
        val held = SyncReceiveParts.outcome(full, SyncReceiveHooks(true, false, dayPlanHeld = { true }))
        report.check("sync1c/parts: a day plan question open here makes the plan part \"held\"", held["dayPlan"] == "held")
        val bare = SyncReceiveParts.outcome(file(card(PC, "The PC", SyncDeviceCard.KIND_PC).copy(historyOff = true)), SyncReceiveHooks(true, false))
        report.check("sync1c/parts: a bare file (a QR file, an older app) leaves every list part absent and sessions absent when history is off", bare["tasks"] == "absent" && bare["goals"] == "absent" && bare["oldLists"] == "absent" && bare["sessions"] == "absent")

        // Receiving: the record only moves forward, and the same file by the same way twice changes nothing.
        var local = SyncReceiptsLocal().recordArrival(PC, SyncReceiptParts.DRIVE, T0.plusMinutes(5)).recordParts(PC, T0.plusMinutes(5), all)
        val again = local.recordArrival(PC, SyncReceiptParts.DRIVE, T0.plusMinutes(5)).recordParts(PC, T0.plusMinutes(5), all)
        report.check("sync1c/parts: the same file read twice leaves the receipts exactly as they were", again == local)
        // A late copy by another way (an older file) still proves that way works, and never moves the held time back.
        local = local.recordArrival(PC, SyncReceiptParts.FOLDER, T0.plusMinutes(1))
        val row = local.senders.single()
        report.check(
            "sync1c/parts: a late copy by another way is recorded for that way and the held time does not go back",
            row.byMethod?.get(SyncReceiptParts.FOLDER) == T0.plusMinutes(1) && row.held == T0.plusMinutes(5),
        )
        report.check("sync1c/parts: the file's list is null alone and an empty list with another device and nothing held", SyncReceiptsLocal().forFile(SELF, setOf(SELF)) == null && SyncReceiptsLocal().forFile(SELF, setOf(SELF, PC)) == emptyList<SyncReceipt>())
    }

    // ---- the unreadable list

    private fun unreadable(report: CheckReport) {
        var clock = T0
        var changes = 0
        val list = SyncUnreadableFiles { clock }
        list.onChanged = { changes++ }
        list.report(SyncReceiptParts.DRIVE, SECRET_FILE, "damaged", PC, ownStale = false)
        val file = list.all().single()
        report.check("sync1c/unreadable: a damaged file is listed with its way, reason and device", file.method == "drive" && file.reason == "damaged" && file.device == PC && file.isProblem)
        clock = T0.plusMinutes(5)
        list.report(SyncReceiptParts.DRIVE, SECRET_FILE, "damaged", PC, ownStale = false)
        report.check("sync1c/unreadable: seen again for the same reason keeps its first time (and says nothing changed)", list.all().single().at == T0 && changes == 1)
        list.ignore(SyncReceiptParts.DRIVE, file.key)
        report.check("sync1c/unreadable: Ignore hides the line and nothing else", list.shown().isEmpty() && list.all().single().ignored && list.all().single().name == SECRET_FILE)
        list.report(SyncReceiptParts.DRIVE, SECRET_FILE, "damaged", PC, ownStale = false)
        report.check("sync1c/unreadable: an ignored file seen again for the same reason stays ignored", list.shown().isEmpty())
        list.report(SyncReceiptParts.DRIVE, SECRET_FILE, "newerFormat", PC, ownStale = false)
        report.check("sync1c/unreadable: a new reason shows the line again", list.shown().single().reason == "newerFormat")
        list.report(SyncReceiptParts.FOLDER, "other-group-file.absync", SyncUnreadableFile.OTHER_GROUP, null, ownStale = true)
        report.check("sync1c/unreadable: another group's leftover is information, not a problem", !list.all().first { it.method == "folder" }.isProblem)
        list.clear(SyncReceiptParts.DRIVE, SECRET_FILE)
        report.check("sync1c/unreadable: a file that reads now leaves the list", list.all().none { it.method == "drive" })
        list.prune(SyncReceiptParts.FOLDER, emptySet())
        report.check("sync1c/unreadable: a file that is gone from its way's listing leaves the list", list.all().isEmpty())
        list.report(SyncReceiptParts.RELAY, "x.absync", "tooBig", null, false)
        list.clearAll()
        report.check("sync1c/unreadable: a new group clears the list", list.all().isEmpty())
    }

    // ---- remove my stale file: own names only

    private fun stalePolicy(report: CheckReport) {
        val ownCurrent = SyncFolder.fileNameFor("11111111", SELF)
        val ownOld = SyncFolder.fileNameFor("22222222", SELF)
        val otherDevice = SyncFolder.fileNameFor("11111111", PC)
        val olds = SyncStalePolicy.oldOwnNames(listOf("22222222"), SELF)
        val copy = ownCurrent.substringBeforeLast('.') + " (conflict 2026-11-13).absync"
        report.check("sync1c/stale: the folder removes this phone's old-group file", SyncStalePolicy.mayRemoveFromFolder(ownOld, ownCurrent, olds))
        report.check("sync1c/stale: the folder removes an extra \"(conflict ...)\" copy of this phone's own file", SyncStalePolicy.mayRemoveFromFolder(copy, ownCurrent, olds))
        report.check("sync1c/stale: the folder never removes this phone's current file", !SyncStalePolicy.mayRemoveFromFolder(ownCurrent, ownCurrent, olds) && !SyncStalePolicy.mayRemoveFromFolder(ownCurrent.uppercase(), ownCurrent, olds))
        report.check("sync1c/stale: the folder never removes another device's file or its conflict copy", !SyncStalePolicy.mayRemoveFromFolder(otherDevice, ownCurrent, olds) && !SyncStalePolicy.mayRemoveFromFolder(otherDevice.substringBeforeLast('.') + " (conflict 1).absync", ownCurrent, olds))
        report.check("sync1c/stale: the folder never removes a name that is a path or not a sync file", !SyncStalePolicy.mayRemoveFromFolder("../$ownOld", ownCurrent, olds) && !SyncStalePolicy.mayRemoveFromFolder("notes.txt", ownCurrent, olds))
        report.check("sync1c/stale: Drive removes a duplicate of this phone's current name and its old-group file", SyncStalePolicy.mayRemoveFromDrive(ownCurrent, "id-2", ownCurrent, "id-1", olds) && SyncStalePolicy.mayRemoveFromDrive(ownOld, "id-3", ownCurrent, "id-1", olds))
        report.check("sync1c/stale: Drive never removes the file it lists as the current one, nor another device's", !SyncStalePolicy.mayRemoveFromDrive(ownCurrent, "id-1", ownCurrent, "id-1", olds) && !SyncStalePolicy.mayRemoveFromDrive(otherDevice, "id-9", ownCurrent, "id-1", olds))
        report.check("sync1c/stale: the relay never offers it (ADR 46)", !SyncStalePolicy.mayRemoveFromRelay(ownOld) && !SyncStalePolicy.mayRemoveFromRelay(ownCurrent))
        // Every name of a spread of devices and groups: only this phone's own names ever pass.
        val anyone = (0 until 40).flatMap { n -> listOf("11111111", "22222222", "33333333").map { SyncFolder.fileNameFor(it, UUID.nameUUIDFromBytes("device-$n".toByteArray())) } }
        report.check(
            "sync1c/stale: of 120 names of other devices none may be removed from the folder or Drive",
            anyone.none { SyncStalePolicy.mayRemoveFromFolder(it, ownCurrent, olds) || SyncStalePolicy.mayRemoveFromDrive(it, "x", ownCurrent, "id-1", olds) },
        )
    }

    // ---- the relay's re-read

    private class FakeRelay(val names: List<String>, val sealed: Map<String, ByteArray>, val history: Set<String> = emptySet(), val busyAt: Int = -1, val clock: () -> Long) : RelayRereadPort {
        val requestTimes = ArrayList<Long>()
        val accepted = ArrayList<String>()
        val calls = ArrayList<String>()
        var gets = 0
        var devices: List<ReceivedDevice> = emptyList()
        var key: SyncKey? = null
        var events = 0

        override fun list(): List<RelayRereadFile> {
            requestTimes.add(clock())
            calls.add("list")
            return names.map { RelayRereadFile(it, sealed[it]?.size?.toLong() ?: 10, "e-$it") }
        }

        override fun get(name: String): RelayRereadDownload? {
            requestTimes.add(clock())
            calls.add("get")
            gets++
            if (gets == busyAt) throw RelayBusyException()
            return sealed[name]?.let { RelayRereadDownload(it, "e-$name") }
        }

        override fun isHistoryFile(name: String, bytes: ByteArray) = bytes.isEmpty() || history.any { sealed[it]?.contentEquals(bytes) == true }

        override fun accept(name: String, download: RelayRereadDownload): Boolean {
            accepted.add(name)
            val opened = SyncFileCodec.open(download.bytes, key!!) ?: return true
            val outcome = SyncCarry.accept(devices, opened, download.bytes, SyncDeviceCard.METHOD_RELAY, SELF, Instant.parse("2026-11-13T12:00:00Z"))
            // A change is a file the phone held nothing of, or held an older one of; the same bytes again changes nothing.
            val held = devices.firstOrNull { it.file.device.deviceId == opened.device.deviceId }
            if (outcome.error == null && (held == null || opened.device.writtenAt > held.file.device.writtenAt)) events++
            if (outcome.error == null) devices = outcome.devices
            return false
        }

        override fun sameGroup() = true
    }

    private fun relayReread(report: CheckReport) {
        val key = SyncKey.fromSecret(ByteArray(SyncKey.SECRET_BYTES) { (it + 3).toByte() })
        val ids = (0 until 100).map { UUID.nameUUIDFromBytes("peer-$it".toByteArray()) }
        val names = ids.map { SyncFolder.fileNameFor(key.groupId, it) }
        val sealed = ids.mapIndexed { index, id -> names[index] to SyncFileCodec.seal(file(card(id, "Peer $index", SyncDeviceCard.KIND_PC, T0)), key) }.toMap()

        // Pacing: 100 downloads and a listing, every request spread to 30 a minute (a fake clock the waits move).
        var now = 0L
        val pacer = RelayRequestPacer({ now }, { now += it })
        val relay = FakeRelay(names, sealed, clock = { now }).also { it.key = key }
        val result = RelayRereader.run("relay", relay, pacer)
        val worstWindow = relay.requestTimes.indices.maxOf { i -> relay.requestTimes.count { it >= relay.requestTimes[i] && it < relay.requestTimes[i] + 60_000 } }
        report.check("sync1c/reread: a re-read of 100 files asks for each file once and counts every request", result.files == 100 && result.requests == 101 && relay.requestTimes.size == 101, "${result.files}/${result.requests}")
        report.check("sync1c/reread: no 60 seconds hold more than 30 requests to the relay", worstWindow <= RelayRequestPacer.PER_MINUTE, "worst window $worstWindow")
        report.check("sync1c/reread: it takes about as long as the pacing says (101 requests at 30 a minute)", now >= 3 * 60_000L - 1_000, "took ${now / 1000}s")

        // Idempotent: a second re-read of the same bytes changes nothing (0 events, the held copies byte for byte equal).
        val before = relay.devices.map { it.file to it.sealed.toList() }
        val eventsAfterFirst = relay.events
        now += 10 * 60_000L
        RelayRereader.run("relay", relay, RelayRequestPacer({ now }, { now += it }))
        val after = relay.devices.map { it.file to it.sealed.toList() }
        report.check("sync1c/reread: the first re-read took in all 100 files", eventsAfterFirst == 100 && before.size == 100)
        report.check("sync1c/reread: a second re-read changes nothing (0 changes, every held copy equal)", relay.events == eventsAfterFirst && after == before, "events ${relay.events - eventsAfterFirst}")
        report.check("sync1c/reread: it only ever lists and downloads (never a put or a delete)", relay.calls.all { it == "list" || it == "get" })

        // A 429 ends the run at once and nothing is retried.
        var t = 0L
        val busy = FakeRelay(names.take(10), sealed, busyAt = 3, clock = { t }).also { it.key = key }
        val stopped = RelayRereader.run("relay", busy, RelayRequestPacer({ t }, { t += it }))
        report.check("sync1c/reread: a 429 stops the run at once (2 files, no more downloads, marked stopped)", stopped.stopped && stopped.files == 2 && busy.gets == 3, "${stopped.files} files, ${busy.gets} gets")

        // Goal history files are left to their own pass; a too-big file is a problem and never downloaded.
        val historyName = names[0]
        val big = FakeRelay(listOf(names[1]), sealed, clock = { 0L }).also { it.key = key }
        val oversized = object : RelayRereadPort by big {
            override fun list() = listOf(RelayRereadFile("huge.absync", RelayRereader.MAX_FILE_BYTES + 1, "e"), RelayRereadFile(historyName, 5, "e"))
            override fun isHistoryFile(name: String, bytes: ByteArray) = bytes.contentEquals(sealed.getValue(historyName))
        }
        val skipped = RelayRereader.run("relay", oversized, RelayRequestPacer({ 0L }, { }))
        report.check("sync1c/reread: a file over the size limit is a problem and a goal history file is left to its own pass", skipped.problems == 1 && skipped.files == 0 && big.accepted.isEmpty(), "$skipped")
    }

    // ---- the log and the report: no private text

    private fun log(report: CheckReport) {
        val dir = File(System.getProperty("java.io.tmpdir"), "sync1c-check-" + UUID.randomUUID())
        dir.mkdirs()
        try {
            var clock = OffsetDateTime.parse("2026-11-01T00:00:00Z")
            val logFile = File(dir, "sync.log")
            val log = SyncLog(logFile) { clock }
            // 7 days of a busy phone: a read pass every minute on three ways, a put every 5 minutes, some merges.
            var lines = 0
            while (clock.isBefore(OffsetDateTime.parse("2026-11-08T00:00:00Z"))) {
                for (method in listOf("folder", "drive", "relay")) log.pass(method, SyncLogDirection.READ, 12, if (clock.minute % 7 == 0) 3 else 0, 0, 0, 0, 180)
                if (clock.minute % 5 == 0) log.pass("relay", SyncLogDirection.SEND, 1, 0, 0, 0, 0, 40)
                if (clock.minute % 13 == 0) log.pass("drive", SyncLogDirection.HISTORY, 4, 2, 1, 0, 0, 900)
                clock = clock.plusMinutes(1)
                lines++
            }

            log.note("reread")
            log.note("not-a-note-code-$SECRET_TITLE")
            val text = log.lines().joinToString("\n")
            report.check("sync1c/log: the log never grows past 512 KB after a simulated 7 days", logFile.length() <= SyncLog.MAX_BYTES, "${logFile.length()} bytes")
            report.check("sync1c/log: what is kept is only the last 7 days (and not empty)", log.lines().isNotEmpty() && log.lines().all { it.startsWith("2026-11-0") || it.startsWith("2026-11-1") })
            report.check("sync1c/log: a note with a code the log does not know is not written", !text.contains("not-a-note") && text.contains("note reread"))
            val quiet = log.lines().count { it.contains(" quiet ") }
            report.check("sync1c/log: quiet passes are folded into one line per way and hour", quiet in 1..(7 * 24 * 3 + 6), "$quiet quiet lines for ${lines} minutes")
            report.check("sync1c/log: an unknown way is not logged", run { log.pass(SECRET_RELAY, SyncLogDirection.READ, 1, 1, 0, 0, 0, 1); !log.lines().any { it.contains("example") } })
            val shape = Regex("^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}Z (folder|drive|relay) (send|read|history) (files=\\d+ merged=\\d+ held=\\d+ skipped=\\d+ unreadable=\\d+ refused=\\d+ ms=\\d+|quiet passes=\\d+ files=\\d+ ms=\\d+)$|^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}Z note [a-z-]+$")
            report.check("sync1c/log: every line is a time, a way, a direction and numbers (or a fixed note) and nothing else", log.lines().all { shape.matches(it) }, log.lines().firstOrNull { !shape.matches(it) } ?: "")
        } finally {
            dir.deleteRecursively()
        }

        // The report: seeded private text that is everywhere else in the app must not appear in it.
        val now = T0.plusMinutes(11)
        val card = card(PC, SECRET_DEVICE, SyncDeviceCard.KIND_PC, T0.plusMinutes(10))
        val pcFile = file(card, receipts = listOf(SyncReceipt(SELF, T0, byMethod = linkedMapOf(SyncReceiptParts.DRIVE to T0))), nextTasks = listOf())
        val board: SyncBoard = SyncBoardBuilder.build(now, SELF, listOf(SyncDeviceCard.METHOD_DRIVE), listOf(pcFile), SyncReceiptsLocal(), SyncSendLog().record(SyncReceiptParts.DRIVE, T0, T0.plusSeconds(1))) { null }
        val unreadable = SyncUnreadableFiles { T0 }.also {
            it.report(SyncReceiptParts.DRIVE, SECRET_FILE, "damaged", PC, ownStale = false)
            it.report(SyncReceiptParts.FOLDER, "$SECRET_FOLDER/$SECRET_FILE", SyncUnreadableFile.OWN_COPY, SELF, ownStale = true)
        }
        val logLines = listOf("2026-11-13T10:00:00Z drive read files=1 merged=1 held=0 skipped=0 unreadable=0 ms=10")
        val text = SyncDiagnosticsReport.build(now, ZoneId.of("UTC"), "0.1.78", board, unreadable.all(), SyncBoardBuilder.ordinals(listOf(card)), logLines)
        val seeds = listOf(SECRET_TITLE, SECRET_NOTE, SECRET_DEVICE, SECRET_RELAY, SECRET_FOLDER, SECRET_EMAIL, SECRET_FILE, PC.toString(), SELF.toString(), "relay.example", "fabsuser")
        report.check("sync1c/report: no seeded title, note, device name, id, file name, address, folder path or e-mail is in the report", seeds.none { text.contains(it) }, seeds.filter { text.contains(it) }.toString())
        report.check("sync1c/report: devices appear as \"PC 1\" with their kind and version, files by reason only", text.contains("PC 1 (pc, app 0.1.78)") && text.contains("Damaged, or cut off"))
        report.check("sync1c/report: it carries the board, the unreadable files and the log", text.contains("== Confirmation board ==") && text.contains("== Files that aren't taken in ==") && text.contains("== Sync log (last 7 days, UTC) =="))
    }

    // ---- the conflict picker

    private fun picker(report: CheckReport) {
        val self = SELF.toString()
        val other = PC.toString()
        val titleConflict = TaskConflict(TaskConflict.TITLE, "The PC's title", other, T0.minusHours(1))
        val withConflict = SyncTaskItem(
            TaskItem("9b9b9b9b-0000-0000-0000-000000000001", "The phone's title", TaskBin.QUEUED, 1.0, T0, T0, T0, number = TaskNumber(101), conflicts = listOf(titleConflict)),
            TaskFieldStamps.allAt(FieldStamp(T0, self)),
        )
        val lists = TaskLists(listOf(withConflict, task(2)), emptyList(), emptyList(), emptyList())
        val rows = TaskConflictPicker.rows(lists)
        report.check("sync1c/picker: the list holds the tasks that changed on both devices, and only those", rows.size == 1 && rows[0].count == 1 && rows[0].id == withConflict.id)
        val view = TaskConflictPicker.view(lists, withConflict.id)!!
        report.check("sync1c/picker: the newer version is first (the phone's, written at T0) and marked; the head says what is being kept", view.versions.size == 2 && view.versions[0].shown && view.versions[0].isNewer && !view.versions[1].isNewer && view.head == "Keep one version")
        val thisOne = TaskConflictPicker.answer(lists, withConflict.id, titleConflict, TaskConflictPick.KEEP_THIS, "ffffffff-0000-0000-0000-000000000001", self, T0.plusMinutes(1)) { "s$it" }
        report.check("sync1c/picker: keep this one keeps the shown title and closes the conflict", thisOne.changed && thisOne.lists.tasks.first().task.title == "The phone's title" && thisOne.lists.tasks.first().task.conflicts.isNullOrEmpty())
        val otherOne = TaskConflictPicker.answer(lists, withConflict.id, titleConflict, TaskConflictPick.KEEP_OTHER, "ffffffff-0000-0000-0000-000000000002", self, T0.plusMinutes(1)) { "s$it" }
        report.check("sync1c/picker: keep the other puts the other title on the task", otherOne.changed && otherOne.lists.tasks.first().task.title == "The PC's title" && otherOne.toast == "Kept the other version")
        val both = TaskConflictPicker.answer(lists, withConflict.id, titleConflict, TaskConflictPick.KEEP_BOTH, "ffffffff-0000-0000-0000-000000000003", self, T0.plusMinutes(1)) { "s$it" }
        val copy = both.lists.tasks.firstOrNull { it.id == "ffffffff-0000-0000-0000-000000000003" }
        report.check("sync1c/picker: keep both leaves the shown one and makes the other version a new task in Queued", both.changed && copy?.task?.title == "The PC's title" && copy.task.bin == TaskBin.QUEUED && both.toast == "Kept both · the other copy is in Queued")
        val gone = TaskConflictPicker.answer(both.lists, withConflict.id, titleConflict, TaskConflictPick.KEEP_THIS, "ffffffff-0000-0000-0000-000000000004", self, T0.plusMinutes(2)) { "s$it" }
        report.check("sync1c/picker: answering a conflict that is already closed changes nothing", !gone.changed && gone.lists == both.lists)
        report.check("sync1c/picker: once answered the task is out of the list and the picker has nothing for it", TaskConflictPicker.rows(thisOne.lists).isEmpty() && TaskConflictPicker.view(thisOne.lists, withConflict.id) == null)

        // A task with a title and a note conflict shows them one after the other ("1 of 2", the title first).
        val noteConflict = TaskConflict(TaskConflict.NOTE, "other note", other, T0.minusHours(2))
        val two = lists.copy(tasks = listOf(withConflict.copy(task = withConflict.task.copy(note = "my note", conflicts = listOf(noteConflict, titleConflict))), task(2)))
        val first = TaskConflictPicker.view(two, withConflict.id, 0)!!
        report.check("sync1c/picker: with a title and a note conflict the title comes first, \"1 of 2\"", first.conflict.field == TaskConflict.TITLE && first.countText == "1 of 2")
        val afterFirst = TaskConflictPicker.answer(two, withConflict.id, first.conflict, TaskConflictPick.KEEP_THIS, "ffffffff-0000-0000-0000-000000000005", self, T0.plusMinutes(3)) { "s$it" }
        val second = TaskConflictPicker.view(afterFirst.lists, withConflict.id, 1)!!
        report.check("sync1c/picker: then the note, \"2 of 2\", with its own head", second.conflict.field == TaskConflict.NOTE && second.countText == "2 of 2" && second.head == "Keep one version of the note")
    }

    // ---- structure: what the sources do and never do (read from the app's source, as the receipts check reads DeviceBackups)

    internal fun appSource(path: String): String? =
        listOf(File("app/src/main/java/com/accountabilibuddy/companion/$path"), File("../app/src/main/java/com/accountabilibuddy/companion/$path"), File("../../app/src/main/java/com/accountabilibuddy/companion/$path"))
            .firstOrNull { it.isFile }?.readText()

    // The code of a source: its comment lines left out (the words may be explained there).
    internal fun code(text: String): String = text.lines().filter { line -> line.trim().let { !it.startsWith("*") && !it.startsWith("/*") && !it.startsWith("//") } }.joinToString("\n")

    internal fun body(text: String, start: String, end: String): String? {
        val from = text.indexOf(start)
        if (from < 0) return null
        val to = text.indexOf(end, from + start.length)
        return if (to < 0) null else text.substring(from, to)
    }

    private fun structure(report: CheckReport) {
        val coordinator = appSource("sync/SyncCoordinator.kt")
        val accept = coordinator?.let { body(it, "    fun acceptSealed(", "    // ADR 86: what each part of this file came to here") }
        report.check("sync1c/structure: the coordinator's source was found", coordinator != null && accept != null)
        if (accept != null) {
            report.check(
                "sync1c/structure: receiving a file never writes (no send, no write gate, no settings save, no carry-on call but the one for a NEWER file)",
                listOf("writeNow", "syncSoon", "syncNow", "saveSettings", "filesForFolder", "ownGate", "ownSealed", "RelayNetwork").none { accept.contains(it) } &&
                    Regex("onCarryChanged\\?\\.invoke").findAll(accept).count() == 1 && accept.contains("file.device.writtenAt > held.file.device.writtenAt"),
            )
            report.check("sync1c/structure: the arrival is recorded before the \"already has newer data\" decision", accept.indexOf("recordArrival") in 1 until accept.indexOf("SyncCarry.accept"))
        }

        report.check("sync1c/structure: receiving does not touch the repoint guard (no hasProblem, no repointWaits in the troubleshooting code)", listOf("sync/SyncTroubleshoot.kt", "ui/SyncTroubleshootSection.kt").all { path -> appSource(path)?.let { code(it).let { c -> !c.contains("hasProblem") && !c.contains("repointWaits") && !c.contains("saveSettings") && !c.contains("turnedOff") } } == true })

        val relay = appSource("sync/RelaySyncSource.kt")
        report.check("sync1c/structure: the relay's source was found", relay != null)
        if (relay != null) {
            val deletes = Regex("RelayNetwork\\.delete\\(").findAll(relay).map { it.range.first }.toList()
            val leaving = body(relay, "    fun leaveGroup()", "    override suspend fun publishRekey")
            val clearing = body(relay, "    override suspend fun clearOldGroup", "    private suspend fun pollLoop")
            val inside = deletes.count { at -> (leaving != null && at in relay.indexOf(leaving) until relay.indexOf(leaving) + leaving.length) || (clearing != null && at in relay.indexOf(clearing) until relay.indexOf(clearing) + clearing.length) }
            report.check("sync1c/structure: the relay deletes only when the phone leaves or a removal clears the old group (ADR 46), never from troubleshooting", deletes.isNotEmpty() && inside == deletes.size, "$inside of ${deletes.size}")
            val stale = body(relay, "    override suspend fun removeOwnStale", "    override suspend fun historyNow")
            report.check("sync1c/structure: the relay never removes a stale file", stale != null && stale.contains("= false"))
            report.check("sync1c/structure: the relay re-read goes through the pacer", relay.contains("RelayRereader.run(") && relay.contains("pacerFactory"))
        }

        val folder = appSource("sync/DriveFolderSource.kt")
        val drive = appSource("sync/DriveSyncSource.kt")
        report.check(
            "sync1c/structure: folder and Drive remove a file only through the stale policy",
            folder != null && drive != null && folder.contains("SyncStalePolicy.mayRemoveFromFolder") && drive.contains("SyncStalePolicy.mayRemoveFromDrive") &&
                body(folder, "    override suspend fun removeOwnStale", "    override suspend fun historyNow")?.let { it.indexOf("mayRemoveFromFolder") in 0 until it.indexOf("deleteDocument") } == true &&
                body(drive, "    override suspend fun removeOwnStale", "    override suspend fun historyNow")?.let { it.indexOf("mayRemoveFromDrive") in 0 until it.indexOf("files.delete") } == true,
        )
        val shared = appSource("sync/SyncTroubleshoot.kt")
        report.check("sync1c/structure: Remove my stale file is never asked of the relay", shared != null && shared.contains("it.methodCode != SyncReceiptParts.RELAY"))
    }
}
