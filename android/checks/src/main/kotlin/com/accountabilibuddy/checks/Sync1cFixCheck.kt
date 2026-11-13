package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalHistoryDeviceInfo
import com.accountabilibuddy.shared.goals.GoalHistoryFileKey
import com.accountabilibuddy.shared.goals.GoalHistoryNames
import com.accountabilibuddy.shared.goals.GoalHistoryPiece
import com.accountabilibuddy.shared.goals.GoalHistoryStateJson
import com.accountabilibuddy.shared.goals.GoalHistorySync
import com.accountabilibuddy.shared.goals.GoalHistoryYear
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalSeeds
import com.accountabilibuddy.shared.sync.RelayRequestPacer
import com.accountabilibuddy.shared.sync.RelayRereadDownload
import com.accountabilibuddy.shared.sync.RelayRereadFile
import com.accountabilibuddy.shared.sync.RelayRereadPort
import com.accountabilibuddy.shared.sync.RelayRereader
import com.accountabilibuddy.shared.sync.SyncBoardBuilder
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncFolder
import com.accountabilibuddy.shared.sync.SyncGoalYearMerged
import com.accountabilibuddy.shared.sync.SyncLog
import com.accountabilibuddy.shared.sync.SyncLogDirection
import com.accountabilibuddy.shared.sync.SyncPartReceipt
import com.accountabilibuddy.shared.sync.SyncPassGate
import com.accountabilibuddy.shared.sync.SyncReceipt
import com.accountabilibuddy.shared.sync.SyncReceiptParts
import com.accountabilibuddy.shared.sync.SyncReceiptsLocal
import com.accountabilibuddy.shared.sync.SyncSendLog
import com.accountabilibuddy.shared.sync.SyncStalePolicy
import com.accountabilibuddy.shared.sync.SyncTroubleshootRules
import java.io.File
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID
import kotlin.coroutines.Continuation
import kotlin.coroutines.EmptyCoroutineContext
import kotlin.coroutines.startCoroutine

/**
 * SYNC1c review fixes (the PC's sync1b fix round, same rules): Drive "send again" keeps the month tracking (S1), no crash from a sync action (S2),
 * the relay's forced passes are paced (S3), a refused part is shown and never folded as quiet (S5), the current group and an unknown Drive id are
 * never stale (S6), and the pure parts of the troubleshooting gates. The structural checks read the app's source like [Sync1cCheck.structure].
 */
object Sync1cFixCheck {
    private val ME: UUID = UUID.fromString("aaaaaaaa-0000-0000-0000-000000000001")
    private val PC: UUID = UUID.fromString("bbbbbbbb-0000-0000-0000-000000000002")
    private val N0: OffsetDateTime = OffsetDateTime.of(2026, 11, 14, 9, 0, 0, 0, ZoneOffset.UTC)
    private const val GROUP = "0a1b2c3d"

    fun run(report: CheckReport) {
        driveResend(report)
        historyMarks(report)
        safely(report)
        gates(report)
        rereadFailures(report)
        refusedParts(report)
        stale(report)
        structure(report)
    }

    private fun lists(): GoalLists {
        val goal = Gs4SelfCheck.goal(1)
        // An entry in September: finished by mid November (this month and last month are open).
        return GoalLists(
            listOf(GoalSeeds.workArea(N0)), listOf(GoalSeeds.deepWork(N0), goal),
            listOf(GoalEntry(UUID.fromString("77777777-0000-0000-0000-000000000011"), goal.id, LocalDate.of(2026, 9, 10), null, 1.0, GoalEntrySource.Hand, N0.minusDays(60))),
        )
    }

    private fun sync(holder: Gs4SelfCheck.Holder, now: () -> OffsetDateTime, devices: List<GoalHistoryDeviceInfo>): GoalHistorySync {
        var held = lists()
        return GoalHistorySync(
            held = { held }, applySynced = { merge -> Gs4SelfCheck.apply({ held }, { held = it }, merge) }, states = holder, now = now, self = { ME },
            key = { GoalHistorySync.GoalHistoryKey(GROUP, "fp") }, codec = Gs4SelfCheck.FakeCodec, devices = { devices }, enabled = { true }, ready = { true },
        )
    }

    // ---- S1: Drive "send again" re-puts the same version and leaves the tracking alone

    private fun driveResend(report: CheckReport) {
        var now = N0
        val holder = Gs4SelfCheck.Holder()
        val sync = sync(holder, { now }, listOf(GoalHistoryDeviceInfo(PC, true, false, now)))
        val drive = Gs4SelfCheck.Fake("drive", ".abgh", expires = false, canDelete = true)
        val monthName = GoalHistoryNames.nameFor(GROUP, GoalHistoryFileKey.forMonth(ME, 2026, 9), ".abgh")
        val monthText = GoalHistoryNames.monthText(2026, 9)
        sync.run(drive)
        val written = holder.value.transport("drive").written[monthText]
        report.check("sync1c/fix S1: the first Drive pass writes the finished month", written != null && drive.files.containsKey(monthName))
        if (written == null) return
        val firstBytes = drive.files.getValue(monthName).copyOf()

        // The year file rolled by a PC holds this month at this version; the PC has not merged it yet, so the month waits for its removal.
        val year = GoalHistoryYear(GoalHistoryYear.CURRENT_FORMAT_VERSION, 2026, PC, N0.plusHours(1), listOf(GoalHistoryPiece(ME, 9, written.writtenAt)), GoalLists.EMPTY)
        drive.files[GoalHistoryNames.nameFor(GROUP, GoalHistoryFileKey.forYear(2026, PC), ".abgh")] = Gs4SelfCheck.FakeCodec.seal(year)
        sync.noteRegularFile(PC, null)
        now = N0.plusHours(2)
        sync.run(drive)
        report.check("sync1c/fix S1: the year file is read and the month still waits (the PC has not merged it)", holder.value.ownMarker.isNotEmpty() && drive.files.containsKey(monthName) && monthText !in holder.value.transport("drive").removed)

        // Send my files again now, between the roll-up and the removal.
        sync.resendKept("drive")
        val tracked = holder.value.transport("drive")
        report.check("sync1c/fix S1: send again marks the kept month and leaves what was written recorded", monthText in tracked.resendMonths && tracked.written[monthText] == written)
        drive.puts.clear()
        now = N0.plusHours(3)
        sync.run(drive)
        report.check("sync1c/fix S1: the month is put again as the very same version (same bytes, same writtenAt)", drive.puts == listOf(monthName) && drive.files.getValue(monthName).contentEquals(firstBytes) && tracked.written[monthText]?.writtenAt == written.writtenAt)
        report.check("sync1c/fix S1: the mark is spent by the put", tracked.resendMonths.isEmpty())
        report.check("sync1c/fix S1: the month is still not removed early", drive.files.containsKey(monthName) && monthText !in tracked.removed)

        // Once the PC merges the year file the month goes, as it would have without the send again.
        sync.noteRegularFile(PC, listOf(SyncGoalYearMerged(2026, PC, year.rolledAt)))
        now = N0.plusHours(4)
        val last = sync.run(drive)
        report.check("sync1c/fix S1: a finished month awaiting removal is still removed later (WrittenAt unchanged)", last.removed == 1 && !drive.files.containsKey(monthName) && monthText in tracked.removed && tracked.written[monthText]?.writtenAt == written.writtenAt)

        // A file that went missing is put again by send again.
        val second = Gs4SelfCheck.Holder()
        var now2 = N0
        val other = sync(second, { now2 }, listOf(GoalHistoryDeviceInfo(PC, true, false, now2)))
        val store = Gs4SelfCheck.Fake("drive", ".abgh", expires = false, canDelete = true)
        other.run(store)
        store.files.remove(monthName)
        other.resendKept("drive")
        store.puts.clear()
        now2 = N0.plusMinutes(30)
        other.run(store)
        report.check("sync1c/fix S1: a month missing on Drive is put again after send again", store.puts == listOf(monthName) && second.value.transport("drive").resendMonths.isEmpty())
    }

    // ---- GoalHistorySync.resendKept / forceReread

    private fun historyMarks(report: CheckReport) {
        val holder = Gs4SelfCheck.Holder()
        val sync = sync(holder, { N0 }, emptyList())
        // The group's state is only created once a pass or a call asks for it: forceReread and resendKept do that.
        sync.forceReread("relay")
        val relayState = holder.value.transport("relay")
        relayState.read[GoalHistoryNames.keyText(GoalHistoryFileKey.forMonth(PC, 2026, 9))] = N0
        relayState.readTag["x"] = "y"
        relayState.newToGroup = false
        sync.forceReread("relay")
        report.check("sync1c/fix history: forceReread makes this device new to the group again and forgets what was read", relayState.newToGroup && relayState.read.isEmpty() && relayState.readTag.isEmpty())
        relayState.uploaded["a.absync"] = N0
        sync.resendKept("relay")
        report.check("sync1c/fix history: on the relay send again forgets the upload times (the files go again as the same bytes)", relayState.uploaded.isEmpty() && relayState.resendMonths.isEmpty())
        val drive = holder.value.transport("drive")
        drive.written["2026-09"] = Gs4SelfCheck.FakeCodec.openMonth(Gs4SelfCheck.FakeCodec.seal(com.accountabilibuddy.shared.goals.GoalHistoryMonth(1, ME, 2026, 9, N0, lists())))!!
        drive.written["2026-08"] = drive.written.getValue("2026-09")
        drive.removed.add("2026-08")
        sync.resendKept("drive")
        report.check("sync1c/fix history: on Drive send again marks the kept months only (a removed month is not), written stays", drive.resendMonths == setOf("2026-09") && drive.written.size == 2)
        val saved = GoalHistoryStateJson.read(kotlinx.serialization.json.Json.parseToJsonElement(GoalHistoryStateJson.write(holder.value).toString()))
        report.check("sync1c/fix history: the marks survive a save and a read", saved != null && saved.transport("drive").resendMonths == setOf("2026-09"))
    }

    // ---- S2: an action that throws is one fixed line

    private fun <T> runSuspend(block: suspend () -> T): Result<T> {
        var out: Result<T>? = null
        block.startCoroutine(Continuation(EmptyCoroutineContext) { out = it })
        return out ?: Result.failure(IllegalStateException("did not finish"))
    }

    private fun safely(report: CheckReport) {
        val thrown = runSuspend { SyncTroubleshootRules.safely { throw IllegalStateException("database is locked: /private/path") } }.getOrNull()
        report.check("sync1c/fix S2: an action that throws ends in \"Couldn't finish · try again\" with no detail", thrown == SyncTroubleshootRules.FAILED_TEXT && SyncTroubleshootRules.FAILED_TEXT == "Couldn't finish · try again")
        val ioThrown = runSuspend { SyncTroubleshootRules.safely { throw java.io.IOException("disk full") } }.getOrNull()
        val sqlLike = runSuspend { SyncTroubleshootRules.safely { throw RuntimeException(IllegalArgumentException("x")) } }.getOrNull()
        report.check("sync1c/fix S2: an I/O error and a wrapped runtime error are the same line", ioThrown == SyncTroubleshootRules.FAILED_TEXT && sqlLike == SyncTroubleshootRules.FAILED_TEXT)
        report.check("sync1c/fix S2: a normal result passes through", runSuspend { SyncTroubleshootRules.safely { "Re-read 4 files" } }.getOrNull() == "Re-read 4 files")
        val cancelled = runSuspend { SyncTroubleshootRules.safely { throw kotlin.coroutines.cancellation.CancellationException("stop") } }
        report.check("sync1c/fix S2: a cancelled action is not swallowed", cancelled.exceptionOrNull() is kotlin.coroutines.cancellation.CancellationException)
    }

    // ---- S3: the relay's forced passes obey the 5-minute gate

    private fun gates(report: CheckReport) {
        val gate = SyncPassGate(Duration.ofMinutes(5))
        var t = Instant.parse("2026-11-14T09:00:00Z")
        var passes = 0
        repeat(3) {
            if (gate.isOpen(t)) {
                gate.mark(t)
                passes++
            }

            t = t.plusSeconds(20)
        }

        report.check("sync1c/fix S3: three taps within a minute make one relay pass", passes == 1)
        report.check("sync1c/fix S3: still shut after 4 min 59 s, open at 5 min", !gate.isOpen(Instant.parse("2026-11-14T09:04:59Z")) && gate.isOpen(Instant.parse("2026-11-14T09:05:00Z")))
        report.check("sync1c/fix S3: a gate nothing has used is open", SyncPassGate(Duration.ofMinutes(5)).isOpen(Instant.parse("2026-11-14T09:00:00Z")))
        report.check("sync1c/fix S3: the button's words", SyncTroubleshootRules.WAIT_TEXT == "Wait a few minutes")
    }

    // ---- S2 on the relay's re-read: any throw is a problem, never a crash; an unknown history name is noted

    private class FlakyPort(val failList: Boolean, val failGet: Set<String> = emptySet(), val failAccept: Set<String> = emptySet(), val history: Set<String> = emptySet()) : RelayRereadPort {
        val noted = ArrayList<String>()
        override fun list(): List<RelayRereadFile> {
            if (failList) throw IllegalStateException("listing failed")
            return listOf("a.absync", "b.absync", "c.absync", "h.absync").map { RelayRereadFile(it, 5, "e") }
        }

        override fun get(name: String): RelayRereadDownload? {
            if (name in failGet) throw IllegalStateException("closed")
            return RelayRereadDownload(ByteArray(3), "e")
        }

        override fun isHistoryFile(name: String, bytes: ByteArray): Boolean {
            if (name in history) noted.add(name)
            return name in history
        }

        override fun accept(name: String, download: RelayRereadDownload): Boolean {
            if (name in failAccept) throw IllegalStateException("store closed")
            return false
        }

        override fun sameGroup() = true
    }

    private fun rereadFailures(report: CheckReport) {
        val pacer = { RelayRequestPacer({ 0L }, { }) }
        val listing = RelayRereader.run("relay", FlakyPort(failList = true), pacer())
        report.check("sync1c/fix S2: a listing that throws is one problem, not a crash", listing.problems == 1 && listing.files == 0)
        val partial = RelayRereader.run("relay", FlakyPort(false, failGet = setOf("a.absync"), failAccept = setOf("b.absync")), pacer())
        report.check("sync1c/fix S2: a download or a taking in that throws is one problem and the run goes on", partial.problems == 2 && partial.files == 3, "$partial")
        val port = FlakyPort(false, history = setOf("h.absync"))
        RelayRereader.run("relay", port, pacer())
        report.check("sync1c/fix NIT: a goal history file under an unknown name is handed over by name (the phone notes it)", port.noted == listOf("h.absync"))
    }

    // ---- S5: a refused part is logged, shown, never quiet

    private fun refusedParts(report: CheckReport) {
        val dir = File(System.getProperty("java.io.tmpdir"), "sync1cfix-check-" + UUID.randomUUID())
        dir.mkdirs()
        try {
            val log = SyncLog(File(dir, "sync.log")) { N0 }
            log.pass("relay", SyncLogDirection.READ, 3, 0, 0, 0, 0, 20, refused = 1)
            log.pass("drive", SyncLogDirection.READ, 3, 0, 0, 0, 0, 20)
            val lines = log.lines()
            report.check("sync1c/fix S5: a pass that refused a part gets its own line with refused=1", lines.any { it.contains("relay read") && it.contains("refused=1") && !it.contains("quiet") })
            report.check("sync1c/fix S5: a pass that refused nothing is still folded as quiet", lines.any { it.contains("drive read quiet") } && lines.none { it.contains("drive read files=") })
        } finally {
            dir.deleteRecursively()
        }

        val t0 = N0
        val card = SyncDeviceCard(PC, "The PC", SyncDeviceCard.KIND_PC, "0.1.134", t0.toInstant(), emptyList(), false, emptyMap())
        val file = SyncFile(1, card, emptyList(), emptyList(), null, emptyList())
        val local = SyncReceiptsLocal(
            listOf(SyncReceipt(PC, t0, parts = linkedMapOf("dayPlan" to SyncPartReceipt(t0, SyncReceiptParts.REFUSED), "tasks" to SyncPartReceipt(t0, SyncReceiptParts.TAKEN), "settings" to SyncPartReceipt(t0, SyncReceiptParts.HELD)))),
        )
        val board = SyncBoardBuilder.build(t0.plusMinutes(5), ME, listOf(SyncDeviceCard.METHOD_DRIVE, SyncDeviceCard.METHOD_RELAY), listOf(file), local, SyncSendLog()) { null }
        report.check("sync1c/fix S5: the board names the part this phone refused under the device, whatever the method", board.methods.all { it.devices.single().refusedHere == listOf("dayPlan") })
        val clean = SyncBoardBuilder.build(t0.plusMinutes(5), ME, listOf(SyncDeviceCard.METHOD_DRIVE), listOf(file), SyncReceiptsLocal(), SyncSendLog()) { null }
        report.check("sync1c/fix S5: nothing refused, nothing named", clean.methods.single().devices.single().refusedHere.isEmpty())
    }

    // ---- S6: the current group is never stale; an unknown Drive id never makes a duplicate

    private fun stale(report: CheckReport) {
        val current = SyncFolder.fileNameFor("11111111", ME)
        val old = SyncFolder.fileNameFor("22222222", ME)
        val olds = SyncStalePolicy.oldOwnNames(listOf("11111111", "22222222"), ME, "11111111")
        report.check("sync1c/fix S6: the old names leave the current group's name out even when the past keys list it", olds == setOf(old.lowercase()) && current.lowercase() !in olds)
        report.check("sync1c/fix S6: without a current group every past group counts (the old rule)", SyncStalePolicy.oldOwnNames(listOf("11111111"), ME).size == 1)
        report.check("sync1c/fix S6: Drive refuses this phone's current name while its own id is unknown", !SyncStalePolicy.mayRemoveFromDrive(current, "id-2", current, null, olds))
        report.check("sync1c/fix S6: with the id known a duplicate of the current name still goes, the current file never", SyncStalePolicy.mayRemoveFromDrive(current, "id-2", current, "id-1", olds) && !SyncStalePolicy.mayRemoveFromDrive(current, "id-1", current, "id-1", olds))
        report.check("sync1c/fix S6: an old-group file goes even while the id is unknown", SyncStalePolicy.mayRemoveFromDrive(old, "id-3", current, null, olds))
    }

    // ---- structure: what the sources do (read from the app's source)

    private fun structure(report: CheckReport) {
        val relay = Sync1cCheck.appSource("sync/RelaySyncSource.kt")
        val drive = Sync1cCheck.appSource("sync/DriveSyncSource.kt")
        val troubleshoot = Sync1cCheck.appSource("sync/SyncTroubleshoot.kt")
        val share = Sync1cCheck.appSource("sync/SyncShare.kt")
        val main = Sync1cCheck.appSource("ui/MainScreen.kt")
        val coordinator = Sync1cCheck.appSource("sync/SyncCoordinator.kt")
        report.check("sync1c/fix structure: the sources were found", relay != null && drive != null && troubleshoot != null && share != null && main != null && coordinator != null)
        if (relay == null || drive == null || troubleshoot == null || share == null || main == null || coordinator == null) return

        val rereadAll = Sync1cCheck.body(relay, "    override suspend fun rereadAll()", "    // The relay's side of a re-read")
        report.check("sync1c/fix NIT: the relay re-read does not hold the relay's gate across the pacer's waits (it takes it per request)", rereadAll != null && !rereadAll.contains("running.withLock") && relay.contains("private fun <T> gated(") && relay.contains("runBlocking { running.withLock"))
        val takeIn = Sync1cCheck.body(relay, "    private fun takeIn(", "    private fun write(")
        report.check("sync1c/fix S5: the relay does not mark a file with a refused part as seen (it is read again at the next pass)", takeIn != null && takeIn.contains("acceptCounting") && takeIn.contains("accepted.refused == 0"))
        report.check("sync1c/fix NIT: the relay re-read notes an unknown goal history name", relay.contains("goalHistory?.noteHistoryName(name)"))

        report.check("sync1c/fix NIT: Drive's current own file is the newest by modified time, and a removal resets the write gate", drive.contains("maxByOrNull { it.modifiedAt }") && Sync1cCheck.body(drive, "    override suspend fun removeOwnStale", "    override suspend fun historyNow")?.contains("ownGate.reset()") == true)
        report.check("sync1c/fix S6: the old own names are computed without the current group", coordinator.contains("identity.key.value?.groupId") && Sync1cCheck.code(coordinator).contains("oldOwnNames(pastKeys.forFile().orEmpty().map { it.groupId }, identity.deviceId, identity.key.value?.groupId)"))

        val actions = listOf("suspend fun rereadAll(): String = safely", "suspend fun sendAgain(): String = safely", "suspend fun rereadHistory(): String = safely", "suspend fun tryAgain(file: SyncUnreadableFile): String = safely", "suspend fun removeStale(file: SyncUnreadableFile): String = safely", "suspend fun shareReport(share: suspend (String) -> Boolean): String = safely")
        report.check("sync1c/fix S2: every troubleshooting action runs inside the catch-all", actions.all { troubleshoot.contains(it) }, actions.filterNot { troubleshoot.contains(it) }.toString())
        val shareBody = Sync1cCheck.body(share, "    suspend fun shareReport(", "    /** The files in an intent")
        report.check("sync1c/fix S2: the report file is written off the main thread and a failure is false, not a crash", shareBody != null && shareBody.contains("Dispatchers.IO") && shareBody.contains("catch (e: Exception)"))
        val history = Sync1cCheck.body(troubleshoot, "    private suspend fun resendHistory()", "    suspend fun rereadHistory()")
        val reread = Sync1cCheck.body(troubleshoot, "    private suspend fun rereadHistoryNow()", "    /** \"Try again\"")
        val tryAgain = Sync1cCheck.body(troubleshoot, "    suspend fun tryAgain(", "    /** \"Ignore\"")
        report.check(
            "sync1c/fix S3: the relay's history passes (re-read, send again) and Try again are gated and busy-guarded",
            history != null && history.contains("relayHistoryOpen()") && reread != null && reread.contains("relayHistoryOpen()") && reread.contains("relayHistoryGate.mark") &&
                tryAgain != null && tryAgain.contains("relayTryGate.isOpen") && tryAgain.contains("_busy.value"),
        )
        report.check("sync1c/fix Q913: the day plan question shows only when no session the user started is running", main.contains("planConflict?.takeIf { removalLocked == null }"))
    }
}
