package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalHistoryDeviceInfo
import com.accountabilibuddy.shared.goals.GoalHistoryFileKey
import com.accountabilibuddy.shared.goals.GoalHistoryGroupFullException
import com.accountabilibuddy.shared.goals.GoalHistoryMonth
import com.accountabilibuddy.shared.goals.GoalHistoryNames
import com.accountabilibuddy.shared.goals.GoalHistoryRemoteFile
import com.accountabilibuddy.shared.goals.GoalHistoryRun
import com.accountabilibuddy.shared.goals.GoalHistoryStateJson
import com.accountabilibuddy.shared.goals.GoalHistoryStore
import com.accountabilibuddy.shared.goals.GoalHistorySync
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalSeeds
import java.time.Duration
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID
import kotlinx.serialization.json.Json

/**
 * GS3b Kotlin relay mirror (phone): the phone's goal history pass on a relay by the PC's review-fix rules, against a fake relay. A non-roller
 * phone keeps only its open months there; never more than its quota; a 429 ends the pass; no new file when fewer than 4 of the 64 places
 * would stay free; nothing is written while an active device is on an older app; own files are put again every 25 days (the same bytes);
 * a pass deletes nothing; a late joiner marks the years of goals nobody has any more; nothing is downloaded before a goals part merged.
 * The rules themselves are checked against the PC's fixtures (`Gs3bkFixtureCheck`).
 */
object Gs3bkSelfCheck {
    private val ME = UUID.fromString("aaaaaaaa-0000-0000-0000-000000000001")
    private val PC = UUID.fromString("bbbbbbbb-0000-0000-0000-000000000002")
    private const val GROUP = "0a1b2c3d"
    private val T0 = OffsetDateTime.of(2026, 11, 14, 9, 0, 0, 0, ZoneOffset.UTC)

    /** A relay of 64 files with the PC relay's behaviours: a 429 on a new file when full, [failPutAt]-th put refused, a request cap. */
    private class RelayFake(
        override val maxFiles: Int? = 64,
        override val maxRequestsPerPass: Int? = 40,
        var failPutAt: Int? = null,
    ) : GoalHistoryStore {
        override val transport = "relay"
        override val extension = ".absync"
        override val maxFileBytes = 4L * 1024 * 1024
        override val expires = true
        override val canDelete = false
        val files = LinkedHashMap<String, ByteArray>()
        val times = HashMap<String, OffsetDateTime>()
        val putNames = ArrayList<String>()
        val putBytes = LinkedHashMap<String, ByteArray>()
        var attempts = 0
        var gets = 0
        var deletes = 0
        var at: OffsetDateTime = T0

        override fun list() = files.map { (name, bytes) -> GoalHistoryRemoteFile(name, times[name] ?: at, bytes.size.toLong(), null) }

        override fun get(name: String): ByteArray? {
            gets++
            return files[name]
        }

        override fun put(name: String, bytes: ByteArray) {
            attempts++
            if (failPutAt == attempts) throw GoalHistoryGroupFullException()
            if (name !in files && maxFiles != null && files.size >= maxFiles) throw GoalHistoryGroupFullException()
            files[name] = bytes
            putNames.add(name)
            putBytes[name] = bytes
            times[name] = at
        }

        override fun delete(name: String): Boolean {
            deletes++
            return false
        }

        fun filler(count: Int) = repeat(count) { files["%012x.absync".format(it + 1)] = ByteArray(3) }
    }

    private class Rig(var devices: List<GoalHistoryDeviceInfo>, entries: List<GoalEntry>) {
        var now = T0
        var lists = GoalLists(listOf(GoalSeeds.workArea(T0)), listOf(GoalSeeds.deepWork(T0), Gs4SelfCheck.goal(1)), entries)
        val holder = Gs4SelfCheck.Holder()
        val sync = GoalHistorySync(
            held = { lists }, applySynced = { merge -> Gs4SelfCheck.apply({ lists }, { lists = it }, merge) }, states = holder, now = { now }, self = { ME },
            key = { GoalHistorySync.GoalHistoryKey(GROUP, "fp") }, codec = Gs4SelfCheck.FakeCodec, devices = { devices }, enabled = { true }, ready = { true },
        )

        fun run(store: GoalHistoryStore, relayAt: OffsetDateTime = now): GoalHistoryRun {
            (store as? RelayFake)?.at = relayAt
            return sync.run(store)
        }
    }

    private fun pc(version: String? = "0.1.134", removed: Boolean = false, id: UUID = PC) = GoalHistoryDeviceInfo(id, true, removed, null, "pc", version)

    private fun phone(n: Int, version: String? = "0.1.80", removed: Boolean = false) =
        GoalHistoryDeviceInfo(UUID.fromString("dddddddd-0000-0000-0000-%012d".format(n)), false, removed, null, "phone", version)

    private fun entryIn(n: Int, year: Int, month: Int): GoalEntry {
        val at = OffsetDateTime.of(year, month, 5, 12, 0, 0, 0, ZoneOffset.UTC)
        return GoalEntry(UUID.fromString("77777777-0000-0000-0000-%012d".format(n)), Gs4SelfCheck.goal(1).id, LocalDate.of(year, month, 5), null, 1.0, GoalEntrySource.Hand, at)
    }

    // Own entries in the open months (November and October 2026) and in two finished ones.
    private fun fourMonths() = listOf(entryIn(1, 2026, 11), entryIn(2, 2026, 10), entryIn(3, 2026, 3), entryIn(4, 2026, 1))

    private fun twoOpen() = listOf(entryIn(1, 2026, 11), entryIn(2, 2026, 10))

    private fun own(year: Int, month: Int) = GoalHistoryNames.nameFor(GROUP, GoalHistoryFileKey.forMonth(ME, year, month), ".absync")

    private val relays = ArrayList<RelayFake>()

    private fun relay(maxFiles: Int? = 64, cap: Int? = 40, failPutAt: Int? = null) = RelayFake(maxFiles, cap, failPutAt).also { relays.add(it) }

    /** A rig with the PC's goals part merged (the device is no longer waiting for it). */
    private fun rigWithPc(entries: List<GoalEntry>, devices: List<GoalHistoryDeviceInfo> = listOf(pc())): Rig =
        Rig(devices, entries).also { it.sync.noteRegularFile(PC, null) }

    fun run(report: CheckReport) {
        relays.clear()
        openMonthsOnly(report)
        quota(report)
        stops(report)
        freePlaces(report)
        gate(report)
        reupload(report)
        cap(report)
        lateJoiner(report)
        newToGroup(report)
        state(report)
        report.check("gs3bk/relay: no pass of any scenario deleted anything on the relay", relays.all { it.deletes == 0 }, relays.map { it.deletes }.toString())
    }

    private fun openMonthsOnly(report: CheckReport) {
        val rig = rigWithPc(fourMonths())
        val relay = relay()
        val first = rig.run(relay)
        report.check(
            "gs3bk/keep: a phone that is not the roller keeps only its open months on the relay (this month and the last)",
            relay.putNames.toSet() == setOf(own(2026, 11), own(2026, 10)) && first.written == 2,
            relay.putNames.toString(),
        )
        val drive = Gs4SelfCheck.Fake("drive", ".abgh", expires = false, canDelete = true)
        val onDrive = rig.run(drive)
        report.check("gs3bk/keep: Drive keeps every month (no quota, no retention rule)", onDrive.written == 4 && drive.puts.size == 4, "${onDrive.written}")
    }

    private fun quota(report: CheckReport) {
        // 46 phones + the PC + this phone = 48 devices: quota 1 (48 / 48), the newest open month only.
        val devices48 = listOf(pc()) + (1..46).map { phone(it) } + (47..52).map { phone(it, "0.1.50", removed = true) }
        val one = rigWithPc(twoOpen(), devices48)
        val relay = relay()
        one.run(relay)
        report.check("gs3bk/quota: 48 devices share the 48 places: one month file, the newest; removed devices are not counted", relay.putNames == listOf(own(2026, 11)), relay.putNames.toString())
        val devices49 = devices48 + phone(60)
        val none = rigWithPc(twoOpen(), devices49)
        val empty = relay()
        val run = none.run(empty)
        report.check("gs3bk/quota: 49 devices leave no place: nothing written, never more than the quota", empty.putNames.isEmpty() && run.written == 0, empty.putNames.toString())
        // Year files take places from the quota: 3 years x 8 rolling PCs = 24 year files, and 13 devices -> (48 - 24) / 13 = 1.
        val years = relay()
        val rollers = (1..8).map { UUID.fromString("bbbbbbbb-0000-0000-0000-%012d".format(it)) }
        for (year in 2025..2027) for (roller in rollers) years.files[GoalHistoryNames.nameFor(GROUP, GoalHistoryFileKey.forYear(year, roller), ".absync")] = ByteArray(3)
        val devices13 = rollers.map { pc(id = it) } + (1..4).map { phone(it) }
        val withYears = rigWithPc(twoOpen(), devices13)
        withYears.run(years)
        report.check("gs3bk/quota: 24 year files and 13 devices give a quota of one", years.putNames == listOf(own(2026, 11)), years.putNames.toString())
        // With plenty of room both open months go.
        val roomy = relay()
        rigWithPc(twoOpen(), listOf(pc(), phone(1))).run(roomy)
        report.check("gs3bk/quota: with room both open months are written", roomy.putNames.toSet() == setOf(own(2026, 11), own(2026, 10)), roomy.putNames.toString())
    }

    private fun stops(report: CheckReport) {
        val rig = rigWithPc(twoOpen())
        val relay = relay(failPutAt = 1)
        val run = rig.run(relay)
        report.check(
            "gs3bk/429: a 429 on a put ends the pass at once, quietly: one attempt, nothing written, no retry in the same pass",
            relay.attempts == 1 && run.groupFull == 1 && run.written == 0 && relay.files.isEmpty(), "attempts=${relay.attempts} full=${run.groupFull}",
        )
        rig.now = rig.now.plusMinutes(20)
        relay.failPutAt = null
        val next = rig.run(relay)
        report.check("gs3bk/429: the next pass tries again", next.written == 2 && relay.files.size == 2, "${next.written}")
    }

    private fun freePlaces(report: CheckReport) {
        val nearly = relay().also { it.filler(59) }
        val run = rigWithPc(twoOpen()).run(nearly)
        report.check("gs3bk/places: with 59 of 64 used one new file goes (it leaves 4 free), the second would leave 3 and is not written", run.written == 1 && run.groupFull == 1 && nearly.putNames.size == 1, "written=${run.written} full=${run.groupFull}")
        val full = relay().also { it.filler(60) }
        val blocked = rigWithPc(twoOpen()).run(full)
        report.check("gs3bk/places: with 60 of 64 used no new file is written", blocked.written == 0 && full.putNames.isEmpty() && full.attempts == 0, "attempts=${full.attempts}")
    }

    private fun gate(report: CheckReport) {
        val older = rigWithPc(twoOpen(), listOf(pc("0.1.133")))
        val relay = relay()
        val held = older.run(relay)
        report.check("gs3bk/gate: an active device on a PC app older than 0.1.134 holds every relay write", relay.attempts == 0 && held.gated > 0 && held.written == 0, "attempts=${relay.attempts} gated=${held.gated}")
        val drive = Gs4SelfCheck.Fake("drive", ".abgh", expires = false, canDelete = true)
        report.check("gs3bk/gate: Drive is not held by it", older.run(drive).written == 2 && drive.puts.size == 2)
        older.devices = listOf(pc("0.1.134"))
        older.now = older.now.plusMinutes(20)
        report.check("gs3bk/gate: the writes go once that device is up to date", older.run(relay).written == 2 && relay.putNames.size == 2)
        for ((label, devices) in listOf(
            "a phone older than 0.1.78" to listOf(pc(), phone(1, "0.1.77")),
            "a device whose version is unknown" to listOf(pc(), phone(1, null)),
            "an unreadable version" to listOf(pc("beta"), phone(1)),
        )) {
            val rig = rigWithPc(twoOpen(), devices)
            val store = relay()
            rig.run(store)
            report.check("gs3bk/gate: $label holds the writes", store.attempts == 0, "attempts=${store.attempts}")
        }

        val removedOld = rigWithPc(twoOpen(), listOf(pc(), pc("0.1.100", removed = true, id = UUID.fromString("bbbbbbbb-0000-0000-0000-0000000000ff"))))
        val store = relay()
        removedOld.run(store)
        report.check("gs3bk/gate: a removed device never holds the writes", store.putNames.size == 2, "${store.putNames.size}")
        val watch = rigWithPc(twoOpen(), listOf(pc(), GoalHistoryDeviceInfo(UUID.fromString("eeeeeeee-0000-0000-0000-000000000001"), false, false, null, "watch", null)))
        val withWatch = relay()
        watch.run(withWatch)
        report.check("gs3bk/gate: a watch never holds the writes", withWatch.putNames.size == 2, "${withWatch.putNames.size}")
    }

    private fun reupload(report: CheckReport) {
        val rig = Rig(emptyList(), twoOpen())
        val relay = relay()
        rig.run(relay)
        val firstBytes = relay.putBytes.toMap()
        val ownNames = relay.putNames.toSet()
        rig.now = T0.plusDays(24)
        relay.putNames.clear()
        val early = rig.run(relay)
        report.check("gs3bk/25 days: before 25 days nothing is put again", early.reuploaded == 0 && relay.putNames.isEmpty(), relay.putNames.toString())
        rig.now = T0.plusDays(26)
        val late = rig.run(relay)
        // November is still an open month (it is December now, the last month is November); October has finished: not kept, not put again, not deleted.
        report.check(
            "gs3bk/25 days: after 25 days the own file of a still open month is put again as the very same bytes; a finished month's is left to expire",
            late.reuploaded == 1 && relay.putNames == listOf(own(2026, 11)) && relay.putBytes.getValue(own(2026, 11)).contentEquals(firstBytes.getValue(own(2026, 11))) &&
                own(2026, 10) in relay.files,
            "re=${late.reuploaded} ${relay.putNames}",
        )
        report.check("gs3bk/25 days: every put is of an own file", relay.putNames.all { it in ownNames }, relay.putNames.toString())
        // A file that expired is written again, from the kept copy.
        relay.files.remove(own(2026, 11))
        rig.now = rig.now.plusMinutes(20)
        relay.putNames.clear()
        val gone = rig.run(relay)
        report.check("gs3bk/25 days: an own open-month file that went missing is put again", own(2026, 11) in relay.files && gone.reuploaded == 1)
    }

    private fun cap(report: CheckReport) {
        val store = relay(maxFiles = null, cap = 3)
        val run = rigWithPc(fourMonths()).run(store)
        report.check("gs3bk/cap: a pass stops at the store's request cap and says so", run.capped && store.attempts == 2 && run.written == 2, "attempts=${store.attempts} capped=${run.capped}")
        val none = relay(cap = 40)
        report.check("gs3bk/cap: under the cap it is not capped", !rigWithPc(twoOpen()).run(none).capped)
    }

    private fun pcMonthFile(store: RelayFake, goalNumber: Int, stamp: OffsetDateTime) {
        val goal = UUID.fromString("99999999-0000-0000-0000-00000000010$goalNumber")
        val entry = GoalEntry(UUID.fromString("66666666-0000-0000-0000-00000000010$goalNumber"), goal, stamp.toLocalDate(), null, 1.0, GoalEntrySource.Hand, stamp)
        val month = GoalHistoryMonth(GoalHistoryMonth.CURRENT_FORMAT_VERSION, PC, stamp.year, stamp.monthValue, stamp, GoalLists(entries = listOf(entry)))
        val name = GoalHistoryNames.nameFor(GROUP, month.key, ".absync")
        store.files[name] = Gs4SelfCheck.FakeCodec.seal(month)
        store.times[name] = stamp
    }

    private fun pcFileRead(rig: Rig, year: Int, month: Int) =
        GoalHistoryNames.keyText(GoalHistoryFileKey.forMonth(PC, year, month)) in rig.holder.value.transport("relay").read

    private fun lateJoiner(report: CheckReport) {
        // A month file of the PC holds rows of a goal this phone never got and nobody has any more (all rows over 30 days old).
        val rig = Rig(listOf(pc()), twoOpen())
        rig.sync.noteRegularFile(PC, null)
        val relay = relay()
        pcMonthFile(relay, 1, OffsetDateTime.of(2026, 9, 10, 12, 0, 0, 0, ZoneOffset.UTC))
        val first = rig.run(relay)
        val transport = rig.holder.value.transport("relay")
        report.check(
            "gs3bk/late joiner: the rows of an unknown goal keep the file unread, and the first drop is noted",
            !pcFileRead(rig, 2026, 9) && rig.holder.value.firstDropAt != null && rig.holder.value.prunedGoals.isEmpty() && transport.newToGroup,
            "read=${first.read} firstDrop=${rig.holder.value.firstDropAt}",
        )
        rig.now = rig.now.plusMinutes(20)
        report.check("gs3bk/late joiner: no other device has merged since: still nothing freed", rig.run(relay).let { !pcFileRead(rig, 2026, 9) && rig.holder.value.prunedGoals.isEmpty() })
        rig.now = rig.now.plusHours(1)
        rig.sync.noteRegularFile(PC, null)
        val second = rig.run(relay)
        val gone = UUID.fromString("99999999-0000-0000-0000-000000000101")
        report.check(
            "gs3bk/late joiner: once the other device's file merged after the first drop, the old goal is pruned, its file counts as read, the phone is no longer new",
            gone in rig.holder.value.prunedGoals && pcFileRead(rig, 2026, 9) && !transport.newToGroup && rig.holder.value.firstDropAt == null,
            "pruned=${rig.holder.value.prunedGoals} read=${second.read} new=${transport.newToGroup} first=${rig.holder.value.firstDropAt}",
        )
        val before = relay.gets
        rig.now = rig.now.plusHours(1)
        rig.run(relay)
        report.check("gs3bk/late joiner: the file is not downloaded again", relay.gets == before, "gets ${relay.gets - before}")

        // A goal with recent rows may still arrive: never freed.
        val recent = Rig(listOf(pc()), twoOpen())
        recent.sync.noteRegularFile(PC, null)
        val store = relay()
        pcMonthFile(store, 2, T0.minusDays(3))
        recent.run(store)
        recent.now = recent.now.plusHours(2)
        recent.sync.noteRegularFile(PC, null)
        recent.run(store)
        report.check(
            "gs3bk/late joiner: a goal with a recent row is not freed (it may still arrive); the phone stays new to the group",
            recent.holder.value.prunedGoals.isEmpty() && !pcFileRead(recent, 2026, 11) && recent.holder.value.transport("relay").newToGroup,
            "pruned=${recent.holder.value.prunedGoals}",
        )
    }

    private fun newToGroup(report: CheckReport) {
        // Another device is known but no goals part has merged yet: nothing is downloaded for rows that would all be dropped.
        val rig = Rig(listOf(pc()), twoOpen())
        val relay = relay()
        pcMonthFile(relay, 3, OffsetDateTime.of(2026, 10, 10, 12, 0, 0, 0, ZoneOffset.UTC))
        val run = rig.run(relay)
        report.check("gs3bk/new to group: before any goals part merged nothing is downloaded", relay.gets == 0 && run.read == 0, "gets=${relay.gets}")
        report.check("gs3bk/new to group: the phone still writes its own months meanwhile", run.written == 2)
        rig.now = rig.now.plusMinutes(20)
        rig.sync.noteRegularFile(PC, null)
        val after = rig.run(relay)
        report.check("gs3bk/new to group: after the goals part merged the files are read", relay.gets > 0 && after.read >= 1, "gets=${relay.gets} read=${after.read}")
        report.check("gs3bk/new to group: a phone alone in its group has nothing to wait for", Rig(emptyList(), twoOpen()).run(relay()).written == 2)
    }

    private fun state(report: CheckReport) {
        val rig = Rig(listOf(pc()), twoOpen())
        rig.sync.noteRegularFile(PC, null)
        val relay = relay()
        pcMonthFile(relay, 1, OffsetDateTime.of(2026, 9, 10, 12, 0, 0, 0, ZoneOffset.UTC))
        rig.run(relay)
        val saved = GoalHistoryStateJson.read(Json.parseToJsonElement(GoalHistoryStateJson.write(rig.holder.value).toString()))
        report.check(
            "gs3bk/state: the first-drop time and the per-transport new-to-group flag survive a save and a read",
            saved != null && saved.firstDropAt == rig.holder.value.firstDropAt && saved.firstDropAt != null && saved.transport("relay").newToGroup == rig.holder.value.transport("relay").newToGroup,
        )
        val older = Json.parseToJsonElement("""{"version":1,"group":"x","goalsPartMerged":true,"transports":{"relay":{"read":{}}}}""")
        report.check("gs3bk/state: a state written before the flag existed is read as not new to the group once a goals part merged", GoalHistoryStateJson.read(older)?.transport("relay")?.newToGroup == false)
        report.check("gs3bk/state: the day count of the re-upload is the PC's 25 days", GoalHistorySync.REUPLOAD_EVERY == Duration.ofDays(25))
    }
}
