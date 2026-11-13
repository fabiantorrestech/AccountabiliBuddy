package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalArea
import com.accountabilibuddy.shared.goals.GoalDeepWorkTargets
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeekly
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklyResult
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklySync
import com.accountabilibuddy.shared.goals.GoalDerivedWriter
import com.accountabilibuddy.shared.goals.GoalEdits
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalField
import com.accountabilibuddy.shared.goals.GoalFieldStamps
import com.accountabilibuddy.shared.goals.GoalHistoryCodec
import com.accountabilibuddy.shared.goals.GoalHistoryDeviceInfo
import com.accountabilibuddy.shared.goals.GoalHistoryFileKey
import com.accountabilibuddy.shared.goals.GoalHistoryJson
import com.accountabilibuddy.shared.goals.GoalHistoryMonth
import com.accountabilibuddy.shared.goals.GoalHistoryNames
import com.accountabilibuddy.shared.goals.GoalHistoryRemoteFile
import com.accountabilibuddy.shared.goals.GoalHistoryState
import com.accountabilibuddy.shared.goals.GoalHistoryStateHolder
import com.accountabilibuddy.shared.goals.GoalHistoryStateJson
import com.accountabilibuddy.shared.goals.GoalHistoryStore
import com.accountabilibuddy.shared.goals.GoalHistorySync
import com.accountabilibuddy.shared.goals.GoalHistoryYear
import com.accountabilibuddy.shared.goals.GoalKind
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalPeriod
import com.accountabilibuddy.shared.goals.GoalPendingChange
import com.accountabilibuddy.shared.goals.GoalPhoneDerived
import com.accountabilibuddy.shared.goals.GoalPhoneSync
import com.accountabilibuddy.shared.goals.GoalSeeds
import com.accountabilibuddy.shared.goals.GoalSourceRule
import com.accountabilibuddy.shared.goals.GoalSourceType
import com.accountabilibuddy.shared.goals.GoalStoreRules
import com.accountabilibuddy.shared.goals.GoalTarget
import com.accountabilibuddy.shared.goals.GoalUnit
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.sync.SyncGoalParts
import java.time.Duration
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID
import kotlinx.serialization.json.Json

/**
 * GS4 (phone goals sync wiring): the decisions the phone's store, sync file and history transport make, as pure functions (no device).
 * Deep work's weekly receive rule, the store's stamping, derived ownership, what goes out, the history files' transport (a fake store and
 * codec), the PC -> phone -> PC round trip of the goal parts on the PC's own fixtures (the C# writer's files).
 */
object Gs4SelfCheck {
    private val PHONE = "aaaaaaaa-0000-0000-0000-000000000001"
    private val PC = "bbbbbbbb-0000-0000-0000-000000000002"
    private val T0 = OffsetDateTime.of(2026, 11, 14, 9, 0, 0, 0, ZoneOffset.UTC)
    private val DELAY = Duration.ofMinutes(10)

    fun run(report: CheckReport) {
        weekly(report)
        stamping(report)
        derived(report)
        outgoing(report)
        history(report)
        names(report)
    }

    private fun env(now: OffsetDateTime = T0, pb: Boolean = false, session: Boolean = false) = GoalDeepWorkWeeklySync.Env(pb, session, now, DELAY)

    private fun stamp(minutes: Long, by: String = PC) = FieldStamp(T0.plusMinutes(minutes), by)

    private fun targets(weekly: Int?, tto: Boolean, at: FieldStamp) = GoalDeepWorkTargets(weekly?.toDouble(), tto, at)

    private fun received(held: GoalDeepWorkWeekly, incoming: GoalDeepWorkTargets, e: GoalDeepWorkWeeklySync.Env = env(), own: FieldStamp = stamp(0, PHONE)) =
        GoalDeepWorkWeeklySync.received(held, incoming, own, e, 120.0)

    private fun weekly(report: CheckReport) {
        val held = GoalDeepWorkWeekly(600, stamp = stamp(0, PHONE))
        // Higher: at once, also during a session.
        val raised = received(held, targets(900, false, stamp(5)), env(session = true))
        report.check("gs4/weekly: a higher weekly target applies at once, also in a session", raised.minutes == 900 && raised.pending == null && raised.stamp == stamp(5))
        // Lower in AccountabiliBuddy: waits THIS phone's delay from the receipt, old value stays, sender's stamp kept for the landing.
        val lower = received(held, targets(300, false, stamp(5)))
        report.check(
            "gs4/weekly: a lower weekly target waits this phone's delay from the receipt",
            lower.minutes == 600 && lower.pending?.amount == 300.0 && lower.pending?.appliesAt == T0.plus(DELAY) && lower.pendingStamp == stamp(5) && lower.stamp == stamp(0, PHONE),
        )
        report.check("gs4/weekly: while it waits the phone still sends its old value with its old stamp", GoalDeepWorkWeeklySync.targets(lower, stamp(0, PHONE)).let { it.weeklyMinutes == 600.0 && it.stamp == stamp(0, PHONE) })
        // The same file again keeps the wait's time (no ping-pong).
        val again = received(lower, targets(300, false, stamp(5)), env(T0.plusMinutes(4)))
        report.check("gs4/weekly: the same file read again keeps the wait's time", again == lower)
        // Lands with the sender's stamp.
        val landed = GoalDeepWorkWeeklySync.settle(lower, stamp(0, PHONE), env(T0.plusMinutes(11)), PHONE)
        report.check("gs4/weekly: it lands after the delay with the sender's stamp and nothing waits", landed.minutes == 300 && landed.pending == null && landed.stamp == stamp(5) && landed.pendingStamp == null)
        report.check("gs4/weekly: before the delay it does not land", GoalDeepWorkWeeklySync.settle(lower, stamp(0, PHONE), env(T0.plusMinutes(9)), PHONE).minutes == 600)
        // During a session: held, never applied, wait counted from the session's end.
        val held1 = received(held, targets(300, false, stamp(5)), env(session = true))
        report.check("gs4/weekly: lower in a session is held (after-session)", held1.pendingAfterSession && held1.minutes == 600)
        val during = GoalDeepWorkWeeklySync.settle(held1, stamp(0, PHONE), env(T0.plusHours(3), session = true), PHONE)
        report.check("gs4/weekly: never applied while the session runs, however long", during.minutes == 600)
        val ended = T0.plusHours(3)
        val rebased = GoalDeepWorkWeeklySync.settle(held1, stamp(0, PHONE), env(ended), PHONE)
        report.check("gs4/weekly: after the session its wait counts from the session's end", rebased.minutes == 600 && rebased.pending?.appliesAt == ended.plus(DELAY) && !rebased.pendingAfterSession)
        // Removed and Track time only wait too.
        val removed = received(held, targets(null, false, stamp(5)))
        report.check("gs4/weekly: a removed weekly target waits", removed.minutes == 600 && removed.pending != null && removed.pending?.amount == null)
        val tto = received(held, targets(null, true, stamp(5)))
        report.check("gs4/weekly: Track time only turning on waits", !tto.trackTimeOnly && tto.pendingTrackTimeOnly != null)
        val ttoOff = received(held.copy(trackTimeOnly = true, minutes = null), targets(720, false, stamp(5)), env(session = true))
        report.check("gs4/weekly: Track time only turning off is stricter, at once", !ttoOff.trackTimeOnly && ttoOff.minutes == 720)
        // ProductaBuddy: at once.
        val pb = received(held, targets(300, false, stamp(5)), env(pb = true, session = true))
        report.check("gs4/weekly: in ProductaBuddy mode a lower target applies at once", pb.minutes == 300 && pb.pending == null)
        // Stale and same stamp ignored.
        report.check("gs4/weekly: a stale weekly target (older stamp) is ignored", received(held, targets(300, false, stamp(-5))) == held)
        report.check("gs4/weekly: the same stamp is ignored", received(held, targets(300, false, stamp(0, PHONE))) == held)
        // Cancel of a received change re-stamps and the sender takes it as a raise.
        val cancelled = GoalDeepWorkWeeklySync.cancel(lower, stamp(0, PHONE), T0.plusMinutes(1), PHONE)
        report.check(
            "gs4/weekly: Cancel on a received change stamps this phone's value after the received one",
            cancelled.pending == null && cancelled.pendingStamp == null && cancelled.minutes == 600 && GoalDeepWorkWeeklySync.isNewer(cancelled.stamp!!, stamp(5)),
        )
        // A local change is stamped after every stamp it replaces.
        val local = GoalDeepWorkWeeklySync.setLocal(held.copy(stamp = stamp(30, PHONE)), 900, stamp(0, PHONE), env(), PHONE)
        report.check("gs4/weekly: a local raise is stamped one tick after a later held stamp", local.result == GoalDeepWorkWeeklyResult.Applied && local.state.stamp!!.at.isAfter(T0.plusMinutes(30)))
        val localLower = GoalDeepWorkWeeklySync.setLocal(held, 300, stamp(0, PHONE), env(session = true), PHONE)
        report.check("gs4/weekly: a local lowering is refused in a session", localLower.result == GoalDeepWorkWeeklyResult.RefusedDuringSession)
        // Merge path: a stale file's composed record cannot lower the target.
        val stored = GoalLists(listOf(GoalSeeds.workArea(T0)), listOf(GoalSeeds.deepWork(T0)))
        val composedHeld = GoalPhoneSync.composed(stored, held)!!
        val record = composedHeld.goals.single { it.id == Goal.DEEP_WORK_ID }
        report.check("gs4/weekly: Deep work goes out with the weekly target in effect and its own stamp, no waiting change", record.secondTarget?.amount == 600.0 && record.secondTarget?.pendingChange == null)
        val withWaiting = GoalPhoneSync.composed(stored, lower)!!.goals.single { it.id == Goal.DEEP_WORK_ID }
        report.check("gs4/weekly: a waiting change is never what goes out", withWaiting.secondTarget?.amount == 600.0)
    }

    private fun goal(id: Int, name: String = "Read", at: OffsetDateTime = T0, area: UUID = GoalSeeds.WORK_AREA_ID) = Goal(
        UUID.fromString("99999999-0000-0000-0000-00000000000$id"), area, name, "book", "#112233", GoalKind.Habit,
        GoalTarget(GoalPeriod.Day, 1.0, GoalUnit.Count), listOf(GoalSourceRule(UUID.fromString("88888888-0000-0000-0000-00000000000$id"), GoalSourceType.Hand)), at, 1.0, at,
    )

    private fun stamping(report: CheckReport) {
        val base = GoalLists(listOf(GoalSeeds.workArea(T0)), listOf(GoalSeeds.deepWork(T0)))
        val now = T0.plusHours(1)
        val added = GoalStoreRules.normalize(base, base.copy(goals = base.goals + goal(1)), now, GoalStoreRules.Kind.Local, PHONE)
        val g = added.goals.single { it.id == goal(1).id }
        report.check("gs4/stamps: a new goal has every part stamped by this device", GoalField.values().all { g.stamps?.of(it)?.by == PHONE })
        val renamed = GoalStoreRules.normalize(added, added.copy(goals = added.goals.map { if (it.id == g.id) it.copy(name = "Read more") else it }), now.plusMinutes(1), GoalStoreRules.Kind.Local, PHONE.uppercase())
        val r = renamed.goals.single { it.id == g.id }
        report.check(
            "gs4/stamps: a rename stamps only the Name part, by the lower-case device id",
            r.stamps!!.of(GoalField.Name)!!.by == PHONE && r.stamps!!.of(GoalField.Name)!!.at == now.plusMinutes(1) && r.stamps!!.of(GoalField.Look) == g.stamps!!.of(GoalField.Look),
        )
        val noop = GoalStoreRules.normalize(renamed, renamed, now.plusMinutes(2), GoalStoreRules.Kind.Local, PHONE)
        report.check("gs4/stamps: a change that alters nothing saves nothing", noop == renamed)
        // An untouched, unstamped goal stays unstamped.
        val unstampedBase = base.copy(goals = base.goals + goal(2))
        val kept = GoalStoreRules.normalize(unstampedBase, unstampedBase, now, GoalStoreRules.Kind.Local, PHONE)
        report.check("gs4/stamps: an untouched older goal keeps having no stamps", kept.goals.single { it.id == goal(2).id }.stamps == null)
        // Delete keeps entries and reminders; the tombstone has no text.
        val entry = GoalEntry(UUID.fromString("77777777-0000-0000-0000-000000000001"), goal(1).id, LocalDate.of(2026, 11, 14), null, 1.0, GoalEntrySource.Hand, T0)
        val withEntry = added.copy(entries = listOf(entry))
        val deleted = GoalStoreRules.normalize(withEntry, withEntry.copy(goals = withEntry.goals.map { if (it.id == g.id) com.accountabilibuddy.shared.goals.GoalTombstones.delete(it, now.plusMinutes(3)) else it }), now.plusMinutes(3), GoalStoreRules.Kind.Local, PHONE)
        val tomb = deleted.goals.single { it.id == g.id }
        report.check("gs4/stamps: deleting a goal keeps its entries, the tombstone holds no text", !tomb.isLive && tomb.name.isEmpty() && deleted.entries.single().isLive)
        // Entry tombstones are kept for good; a goal tombstone goes after 30 days with its entries.
        val removedEntry = entry.copy(editedAt = T0, deletedAt = T0)
        val later = T0.plusDays(60)
        val kept2 = GoalStoreRules.normalize(base, base.copy(entries = listOf(removedEntry)), later, GoalStoreRules.Kind.Local, PHONE)
        report.check("gs4/stamps: an entry tombstone is kept after 40+ days", kept2.entries.size == 1)
        val goneGoal = base.copy(goals = base.goals + goal(1).let { com.accountabilibuddy.shared.goals.GoalTombstones.delete(it, T0) }, entries = listOf(entry))
        val pruned = GoalStoreRules.normalize(base, goneGoal, later, GoalStoreRules.Kind.Local, PHONE)
        report.check("gs4/stamps: a goal tombstone older than 30 days goes with its entries", pruned.goals.none { it.id == g.id } && pruned.entries.isEmpty())
        // A tidy move is stamped with the fixed tidy id.
        val orphan = goal(3, area = UUID.fromString("66666666-0000-0000-0000-000000000009"))
        val moved = GoalStoreRules.normalize(base, base.copy(goals = base.goals + orphan), now, GoalStoreRules.Kind.Local, PHONE)
        report.check("gs4/stamps: a goal in a dead area moves to Work with its Area part stamped by the tidy id", moved.goals.single { it.id == orphan.id }.let { it.areaId == GoalSeeds.WORK_AREA_ID && it.stamps!!.of(GoalField.Area)!!.by == com.accountabilibuddy.shared.goals.GoalAreaTidy.STAMP_ID })
        // A received merge is taken as it is: nothing stamped.
        val synced = GoalStoreRules.normalize(base, base.copy(goals = base.goals + goal(4)), now, GoalStoreRules.Kind.Synced, PHONE)
        report.check("gs4/stamps: a received merge is not stamped again", synced.goals.single { it.id == goal(4).id }.stamps == null)
        // GS2 review: a Sources edit keeps the newer source rules.
        val withNewer = goal(5).copy(stamps = GoalFieldStamps.allAt(FieldStamp(T0, PC)))
        val edited = GoalEdits.edit(withNewer, GoalField.Sources, withNewer.copy(sources = emptyList()), now, PHONE)
        val n1 = com.accountabilibuddy.shared.goals.GoalNewerItem(0, Json.parseToJsonElement("{\"id\":\"1\"}") as kotlinx.serialization.json.JsonObject)
        val n2 = com.accountabilibuddy.shared.goals.GoalNewerItem(0, Json.parseToJsonElement("{\"id\":\"2\"}") as kotlinx.serialization.json.JsonObject)
        val a = withNewer.copy(newerSources = listOf(n1))
        val viaEdit = GoalEdits.edit(a, GoalField.Sources, a.copy(sources = emptyList(), newerSources = listOf(n2)), now, PHONE)
        report.check("gs4/stamps: a Sources edit carries the newer source rules with it", viaEdit.newerSources == listOf(n2))
        val ab = com.accountabilibuddy.shared.goals.GoalSyncMerge.mergeGoal(a.copy(stamps = a.stamps!!.with(GoalField.Sources, FieldStamp(T0.plusMinutes(1), PC))), a.copy(newerSources = listOf(n2), stamps = a.stamps!!.with(GoalField.Sources, FieldStamp(T0.plusMinutes(2), PC))))
        val ba = com.accountabilibuddy.shared.goals.GoalSyncMerge.mergeGoal(a.copy(newerSources = listOf(n2), stamps = a.stamps!!.with(GoalField.Sources, FieldStamp(T0.plusMinutes(2), PC))), a.copy(stamps = a.stamps!!.with(GoalField.Sources, FieldStamp(T0.plusMinutes(1), PC))))
        report.check("gs4/stamps: merge(A,B) = merge(B,A) and the newer Sources stamp brings its newer rules", ab.newerSources == listOf(n2) && ba.newerSources == listOf(n2))
        report.check("gs4/stamps: GoalEdits keeps newerSources and normalizes the device id", edited.stamps!!.of(GoalField.Sources)!!.by == PHONE && GoalEdits.new(withNewer, PHONE.uppercase()).stamps!!.of(GoalField.Name)!!.by == PHONE)
    }

    private fun derived(report: CheckReport) {
        val device = UUID.fromString(PHONE)
        val other = UUID.fromString(PC)
        val steps = goal(1).copy(sources = listOf(GoalSourceRule(UUID.randomUUID(), GoalSourceType.HealthSteps)))
        val day = LocalDate.of(2026, 11, 14)
        val on = GoalPhoneDerived.readings(listOf(steps), listOf(day to 7000L), emptyList(), day)
        val written = GoalDerivedWriter.changes(emptyList(), device, on.readings, on.covered, T0)
        report.check("gs4/derived: steps on writes one Health Connect entry with this device's id", written.size == 1 && written[0].deviceId == device && written[0].value == 7000.0)
        val off = GoalPhoneDerived.readings(listOf(steps), emptyList(), emptyList(), day)
        report.check("gs4/derived: steps off writes nothing and removes nothing", off.readings.isEmpty() && off.covered.isEmpty() && GoalDerivedWriter.changes(written, device, off.readings, off.covered, T0.plusDays(1)).isEmpty())
        val zero = GoalPhoneDerived.readings(listOf(steps), listOf(day to 0L), emptyList(), day)
        report.check("gs4/derived: a measured 0 is a real 0", GoalDerivedWriter.changes(emptyList(), device, zero.readings, zero.covered, T0).single().value == 0.0)
        val foreign = written[0].copy(id = UUID.randomUUID(), deviceId = other)
        val beeminder = written[0].copy(id = UUID.randomUUID(), source = GoalEntrySource.Beeminder, deviceId = other)
        val gone = GoalPhoneDerived.readings(listOf(steps), listOf(day.minusDays(1) to 100L, day.plusDays(1) to 100L), emptyList(), day)
        val changes = GoalDerivedWriter.changes(written + foreign + beeminder, device, gone.readings, gone.covered, T0.plusHours(1))
        report.check("gs4/derived: a day the read no longer has removes only this device's own entry, never another device's or Beeminder's", changes.any { it.id == written[0].id && !it.isLive } && changes.none { it.id == foreign.id || it.id == beeminder.id })
        val unchanged = GoalDerivedWriter.changes(written, device, on.readings, on.covered, T0.plusHours(1))
        report.check("gs4/derived: written only when the value changed", unchanged.isEmpty())
        report.check("gs4/derived: the phone never removes Beeminder entries", !GoalPhoneSync.mayRemove(beeminder, device) && !GoalPhoneSync.mayRemove(foreign, device) && GoalPhoneSync.mayRemove(written[0], device))
    }

    private fun outgoing(report: CheckReport) {
        val weekly = GoalDeepWorkWeekly(600, stamp = stamp(0, PHONE))
        val old = goal(1).copy(editedAt = T0.minusDays(100))
        val fresh = goal(2)
        val entryOld = GoalEntry(UUID.randomUUID(), old.id, LocalDate.of(2026, 7, 1), null, 1.0, GoalEntrySource.Hand, T0.minusDays(100))
        val entryNew = GoalEntry(UUID.randomUUID(), fresh.id, LocalDate.of(2026, 11, 14), null, 1.0, GoalEntrySource.Hand, T0)
        val lists = GoalLists(listOf(GoalSeeds.workArea(T0)), listOf(GoalSeeds.deepWork(T0), old, fresh), listOf(entryOld, entryNew))
        report.check("gs4/out: no goal part while the goals are not ready", GoalPhoneSync.outgoing(lists, weekly, false, T0) == null)
        val parts = GoalPhoneSync.outgoing(lists, weekly, true, T0)!!
        report.check("gs4/out: entries edited in the last 90 days go, older ones travel in the history files", parts.entries!!.map { it.id } == listOf(entryNew.id) && parts.goals!!.size == 3)
        val withPending = lists.copy(goals = lists.goals.map { if (it.id == fresh.id) it.copy(target = it.target.copy(pendingChange = GoalPendingChange(2.0, T0))) else it })
        report.check("gs4/out: a waiting change never goes out", GoalPhoneSync.outgoing(withPending, weekly, true, T0)!!.goals!!.all { it.target.pendingChange == null })
        // No file without parts changes anything; not ready refuses.
        val e = env()
        report.check("gs4/in: a file without goal parts leaves the goals alone", GoalPhoneSync.applied(lists, weekly, null, true, T0, e, 120.0) == null)
        report.check("gs4/in: nothing is applied while the goals are not ready", GoalPhoneSync.applied(lists, weekly, parts, false, T0, e, 120.0) == null)
        // Own file read back: a fixed point.
        val self = GoalPhoneSync.applied(lists, weekly, parts, true, T0, e, 120.0)!!
        report.check("gs4/in: a phone's own file merged back changes nothing", self.weekly == weekly && self.lists.goals.map { it.id } == lists.goals.map { it.id } && self.lists.entries.size == 2)
        // Round trip with the PC's own files: apply each PC fixture file into the phone's empty-seeded lists, write, re-read, apply again.
        val fixtures = java.io.File(System.getProperty("java.io.tmpdir"), "claude/round2-fixtures/gs1a/parts.json")
        if (fixtures.exists()) {
            var cases = 0
            var failures = 0
            val root = Json.parseToJsonElement(fixtures.readText()) as kotlinx.serialization.json.JsonObject
            for (case in root.getValue("cases") as kotlinx.serialization.json.JsonArray) {
                val input = (case as kotlinx.serialization.json.JsonObject)["input"] as? kotlinx.serialization.json.JsonObject ?: continue
                val file = input["parts"] as? kotlinx.serialization.json.JsonObject ?: continue
                val read = SyncGoalParts.read(file) ?: continue
                cases++
                val seeded = GoalLists(listOf(GoalSeeds.workArea(T0)), listOf(GoalSeeds.deepWork(T0)))
                val applied = GoalPhoneSync.applied(seeded, weekly, read, true, T0, env(), 120.0)
                val out = applied?.let { GoalPhoneSync.outgoing(it.lists, it.weekly, true, T0) }
                val back = out?.let { SyncGoalParts.read(kotlinx.serialization.json.JsonObject(SyncGoalParts.write(it).toMap())) }
                val again = back?.let { GoalPhoneSync.applied(applied.lists, applied.weekly, it, true, T0, env(), 120.0) }
                // The phone's file read back and applied again changes nothing (no ping-pong), and every live PC goal is held by the phone.
                val liveIds = (read.goals ?: emptyList()).filter { it.isLive }.map { it.id }.toSet()
                val held = applied?.lists?.goals?.filter { it.isLive }?.map { it.id }?.toSet() ?: emptySet()
                if (applied == null || again == null || again.lists != applied.lists || !held.containsAll(liveIds)) failures++
            }
            report.check("gs4/round-trip: the PC's goal parts ($cases fixture files) apply on the phone, go out again and read back as a fixed point", cases > 0 && failures == 0, "$failures of $cases")
        } else {
            report.check("gs4/round-trip: the PC's gs1a/parts fixture is present", false, fixtures.path)
        }
    }

    // ------------------------------------------------------------------------------------------------ history transport

    private class Fake(
        override val transport: String, override val extension: String, override val expires: Boolean, override val canDelete: Boolean,
        override val maxFileBytes: Long = 4L * 1024 * 1024, override val maxFiles: Int? = null,
    ) : GoalHistoryStore {
        val files = LinkedHashMap<String, ByteArray>()
        val puts = ArrayList<String>()
        val times = HashMap<String, OffsetDateTime>()
        var at: OffsetDateTime = T0

        override fun list() = files.map { (name, bytes) -> GoalHistoryRemoteFile(name, times[name] ?: at, bytes.size.toLong(), null) }

        override fun get(name: String) = files[name]

        override fun put(name: String, bytes: ByteArray) {
            files[name] = bytes
            puts.add(name)
            times[name] = at
        }

        override fun delete(name: String): Boolean {
            files.remove(name)
            return true
        }
    }

    private object FakeCodec : GoalHistoryCodec {
        private fun bytes(magic: String, json: String) = (magic + json).toByteArray()
        override fun seal(month: GoalHistoryMonth) = bytes("ABM1", GoalHistoryJson.writeMonth(month).toString())
        override fun seal(year: GoalHistoryYear) = bytes("ABY1", GoalHistoryJson.writeYear(year).toString())
        override fun openMonth(bytes: ByteArray) = runCatching { GoalHistoryJson.readMonth(Json.parseToJsonElement(String(bytes).removePrefix("ABM1"))) }.getOrNull()
        override fun openYear(bytes: ByteArray) = runCatching { GoalHistoryJson.readYear(Json.parseToJsonElement(String(bytes).removePrefix("ABY1"))) }.getOrNull()
    }

    private class Holder : GoalHistoryStateHolder {
        override var value = GoalHistoryState()
        override fun reset() {
            value = GoalHistoryState()
        }

        override fun save() {}
    }

    private fun history(report: CheckReport) {
        val me = UUID.fromString(PHONE)
        var now = T0
        var lists = GoalLists(
            listOf(GoalSeeds.workArea(T0)), listOf(GoalSeeds.deepWork(T0), goal(1)),
            listOf(GoalEntry(UUID.fromString("77777777-0000-0000-0000-000000000001"), goal(1).id, LocalDate.of(2026, 11, 14), null, 1.0, GoalEntrySource.Hand, T0)),
        )
        val holder = Holder()
        val sync = GoalHistorySync(
            held = { lists }, applySynced = { merge -> merge(lists)?.let { lists = it; true } ?: false }, states = holder, now = { now }, self = { me },
            key = { GoalHistorySync.GoalHistoryKey("0a1b2c3d", "fp") }, codec = FakeCodec, devices = { emptyList<GoalHistoryDeviceInfo>() },
            enabled = { true }, ready = { true },
        )
        val relay = Fake("relay", ".absync", expires = true, canDelete = false, maxFiles = 64)
        val drive = Fake("drive", ".abgh", expires = false, canDelete = true)

        val first = sync.run(relay)
        report.check("gs4/history: the first pass writes the month file", first.written == 1 && relay.puts.size == 1 && GoalHistoryNames.isHistoryFile(relay.files.values.first()))
        now = now.plusMinutes(20)
        relay.puts.clear()
        val second = sync.run(relay)
        report.check("gs4/history: a pass with nothing changed writes nothing", second.written == 0 && second.reuploaded == 0 && relay.puts.isEmpty())
        now = T0.plusDays(24)
        relay.puts.clear()
        report.check("gs4/history: before 25 days the relay file is not put again", sync.run(relay).reuploaded == 0 && relay.puts.isEmpty())
        now = T0.plusDays(26)
        val third = sync.run(relay)
        report.check("gs4/history: after 25 days the relay file is put again", third.reuploaded == 1 && relay.puts.size == 1)
        // Drive never re-uploads and the relay's file written once is not rewritten daily.
        sync.run(drive)
        now = T0.plusDays(60)
        drive.puts.clear()
        report.check("gs4/history: Drive files are not put again for age", sync.run(drive).reuploaded == 0 && drive.puts.isEmpty())
        // A file that went missing is written again.
        val name = relay.files.keys.first()
        relay.files.remove(name)
        now = now.plusMinutes(20)
        report.check("gs4/history: a missing file is put again", sync.run(relay).reuploaded == 1 && relay.files.containsKey(name))
        // Too big: not written, counted.
        val tiny = Fake("relay", ".absync", expires = true, canDelete = false, maxFileBytes = 10, maxFiles = 64)
        report.check("gs4/history: a file over the store's limit is not written", sync.run(tiny).let { it.tooBig == 1 && tiny.puts.isEmpty() })
        // The relay keeps room for the sync files.
        val crowded = Fake("relay", ".absync", expires = true, canDelete = false, maxFiles = 64)
        repeat(60) { crowded.files["%012x.absync".format(it)] = ByteArray(3) }
        report.check("gs4/history: a nearly full relay group is left alone", sync.run(crowded).let { it.groupFull == 1 && crowded.puts.isEmpty() })
        // Reading another device's month file merges its entries.
        val otherId = UUID.fromString(PC)
        val theirs = GoalHistoryMonth(
            GoalHistoryMonth.CURRENT_FORMAT_VERSION, otherId, 2026, 10, T0,
            GoalLists(entries = listOf(GoalEntry(UUID.fromString("77777777-0000-0000-0000-000000000002"), goal(1).id, LocalDate.of(2026, 10, 3), null, 1.0, GoalEntrySource.Hand, T0.minusDays(20)))),
        )
        val shared = Fake("relay", ".absync", expires = true, canDelete = false, maxFiles = 64)
        val key = GoalHistoryFileKey.forMonth(otherId, 2026, 10)
        shared.files[GoalHistoryNames.nameFor("0a1b2c3d", key, ".absync")] = FakeCodec.seal(theirs)
        val reader = GoalHistorySync(
            held = { lists }, applySynced = { merge -> merge(lists)?.let { lists = it; true } ?: false }, states = Holder(), now = { now }, self = { me },
            key = { GoalHistorySync.GoalHistoryKey("0a1b2c3d", "fp") }, codec = FakeCodec,
            devices = { listOf(GoalHistoryDeviceInfo(otherId, true, false, now)) }, enabled = { true }, ready = { true },
        )
        val read = reader.run(shared)
        report.check("gs4/history: another device's month file is read and its entries merged", read.read == 1 && lists.entries.any { it.id == theirs.entries.entries[0].id })
        // The merge runs on the lists held INSIDE the store's lock: a change that lands just before the gate is in what is merged.
        val late = GoalEntry(UUID.fromString("77777777-0000-0000-0000-000000000009"), goal(1).id, LocalDate.of(2026, 10, 9), null, 1.0, GoalEntrySource.Hand, T0)
        val raced = Fake("relay", ".absync", expires = true, canDelete = false, maxFiles = 64)
        raced.files[GoalHistoryNames.nameFor("0a1b2c3d", GoalHistoryFileKey.forMonth(UUID.fromString("33333333-3333-3333-3333-333333333333"), 2026, 9), ".absync")] =
            FakeCodec.seal(theirs.copy(deviceId = UUID.fromString("33333333-3333-3333-3333-333333333333"), month = 9))
        val racing = GoalHistorySync(
            held = { lists }, applySynced = { merge -> lists = lists.copy(entries = lists.entries + late); merge(lists)?.let { lists = it; true } ?: false },
            states = Holder(), now = { now }, self = { me }, key = { GoalHistorySync.GoalHistoryKey("0a1b2c3d", "fp") }, codec = FakeCodec,
            devices = { listOf(GoalHistoryDeviceInfo(UUID.fromString("33333333-3333-3333-3333-333333333333"), true, false, now)) }, enabled = { true }, ready = { true },
        )
        racing.run(raced)
        report.check("gs4/history: a change landing before the gate is in the merge (merged inside the lock, not before it)", lists.entries.any { it.id == late.id })
        // Disabled or not in a group: nothing.
        val off = GoalHistorySync(
            held = { lists }, applySynced = { false }, states = Holder(), now = { now }, self = { me }, key = { null }, codec = FakeCodec,
            devices = { emptyList() }, enabled = { true }, ready = { true },
        )
        report.check("gs4/history: not in a group, nothing is done", !off.run(Fake("relay", ".absync", true, false)).ran)
        // The state, with the pruned ids (Q649), survives a save and a read.
        holder.value.prunedGoals.add(goal(9).id)
        val saved = GoalHistoryStateJson.read(Json.parseToJsonElement(GoalHistoryStateJson.write(holder.value).toString()))
        report.check(
            "gs4/history: the state round-trips, pruned goal ids and own files included",
            saved != null && goal(9).id in saved.prunedGoals && saved.transport("relay").written.size == holder.value.transport("relay").written.size &&
                saved.transport("relay").ownNames == holder.value.transport("relay").ownNames,
        )
        report.check("gs4/history: the own file names are kept for leaving the group", sync.ownNames("relay").isNotEmpty())
    }

    private fun names(report: CheckReport) {
        val device = UUID.fromString("11111111-1111-1111-1111-111111111111")
        val month = GoalHistoryNames.nameFor("0a1b2c3d", GoalHistoryFileKey.forMonth(device, 2026, 11), ".absync")
        val year = GoalHistoryNames.nameFor("0a1b2c3d", GoalHistoryFileKey.forYear(2026, UUID.fromString("22222222-2222-2222-2222-222222222222")), ".abgh")
        // Computed with SHA-256 over "goalhist|<group>|<key>" as the PC's GoalHistoryNames does.
        report.check("gs4/names: a month file's name is the PC's", month == "f97d94029cfc.absync", month)
        report.check("gs4/names: a year file's name is the PC's", year == "b19909402f58.abgh", year)
        report.check("gs4/names: a name's key reads back", GoalHistoryNames.parseKey(GoalHistoryNames.keyText(GoalHistoryFileKey.forMonth(device, 2026, 11))) == GoalHistoryFileKey.forMonth(device, 2026, 11))
        report.check("gs4/names: only the header tells a history file", GoalHistoryNames.isHistoryFile("ABM1xxxx".toByteArray()) && GoalHistoryNames.isHistoryFile("ABY1xxxx".toByteArray()) && !GoalHistoryNames.isHistoryFile("ABS1xxxx".toByteArray()))
    }
}
