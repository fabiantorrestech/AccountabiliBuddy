package com.accountabilibuddy.checks

import com.accountabilibuddy.companion.goals.PhoneGoalSyncWiring
import com.accountabilibuddy.companion.goals.PhoneWeeklyEnvironment
import com.accountabilibuddy.companion.goals.PhoneWeeklySettle
import com.accountabilibuddy.shared.goals.GoalApplyResult
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeekly
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklyResult
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklySync
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalHistoryDeviceInfo
import com.accountabilibuddy.shared.goals.GoalHistoryFileKey
import com.accountabilibuddy.shared.goals.GoalHistoryMonth
import com.accountabilibuddy.shared.goals.GoalHistoryNames
import com.accountabilibuddy.shared.goals.GoalHistoryRemoteFile
import com.accountabilibuddy.shared.goals.GoalHistoryState
import com.accountabilibuddy.shared.goals.GoalHistoryStateJson
import com.accountabilibuddy.shared.goals.GoalHistoryStore
import com.accountabilibuddy.shared.goals.GoalHistorySync
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalPendingChange
import com.accountabilibuddy.shared.goals.GoalSeeds
import com.accountabilibuddy.shared.sync.FieldStamp
import kotlinx.serialization.json.Json
import java.time.Duration
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicReference

/**
 * GS4 review fixes (phone only): the fail-safe default environment (no lowering before the session state is known), what taking a merge in
 * reports and when a history pass records files as read, the one history lock (passes never overlap, the merge never runs under it, no
 * deadlock with the store's lock), the relay's skip by name and the names it remembers across a restart, the wiring decisions.
 */
object Gs4FixSelfCheck {
    private val PHONE = UUID.fromString("aaaaaaaa-0000-0000-0000-000000000001")
    private val PC = UUID.fromString("bbbbbbbb-0000-0000-0000-000000000002")
    private val T0 = Gs4SelfCheck.T0
    private const val GROUP = "0a1b2c3d"

    fun run(report: CheckReport) {
        defaultEnvironment(report)
        applyResults(report)
        readsAndLock(report)
        threads(report)
        names(report)
        wiring(report)
    }

    private fun stamp(minutes: Long, by: String) = FieldStamp(T0.plusMinutes(minutes), by)

    // ------------------------------------------------------------------------------------------ BLOCKER 1

    private fun defaultEnvironment(report: CheckReport) {
        val me = PHONE.toString()
        // A lowering whose time has long come, as stored by an earlier run of the app.
        val due = GoalDeepWorkWeekly(
            minutes = 600, stamp = stamp(0, me),
            pending = GoalPendingChange(300.0, T0.plusMinutes(10), false), pendingStamp = stamp(5, PC.toString()),
        )
        val later = T0.plusDays(2)
        val unknown = PhoneWeeklyEnvironment.unknown(later)
        report.check("gs4fix/default-env: the default environment is not known", !unknown.known)
        report.check(
            "gs4fix/default-env: settling with it never applies a due lowering (the state is left exactly as stored)",
            PhoneWeeklySettle.settle(due, stamp(0, me), unknown, me) == due,
        )
        // The same state in a session, unknown: the hold must not be moved to "after the session" either (that would count its wait from a restart).
        val held = due.copy(pendingAfterSession = false)
        report.check("gs4fix/default-env: settling with it does not re-time a waiting change", PhoneWeeklySettle.settle(held, stamp(0, me), unknown, me) == held)
        // A change made in ProductaBuddy mode would restart its full delay when seen with the old default ("AccountabiliBuddy, no session").
        val pbMade = due.copy(pending = GoalPendingChange(300.0, T0.plusMinutes(10), true))
        report.check("gs4fix/default-env: settling with it does not restart a ProductaBuddy-made wait", PhoneWeeklySettle.settle(pbMade, stamp(0, me), unknown, me) == pbMade)
        // Control: with the session state known and no session, the same lowering applies.
        val known = PhoneWeeklyEnvironment(productaBuddy = false, sessionRunning = false, now = later, looserDelay = Duration.ofMinutes(10))
        val applied = PhoneWeeklySettle.settle(due, stamp(0, me), known, me)
        report.check("gs4fix/default-env: control: with a known quiet state it applies", applied.minutes == 300 && applied.pending == null)
        // Known and in a session: held after the session, never applied.
        val inSession = PhoneWeeklySettle.settle(due, stamp(0, me), known.copy(sessionRunning = true), me)
        report.check("gs4fix/default-env: control: known and in a session it is held, never applied", inSession.minutes == 600 && inSession.pending != null)
        // Asking for something with the placeholder: a lowering is refused (a session is assumed), a raise applies at once.
        val lowered = GoalDeepWorkWeeklySync.setLocal(GoalDeepWorkWeekly(600, stamp = stamp(0, me)), 300, stamp(0, me), unknown.toSync(), me)
        val raised = GoalDeepWorkWeeklySync.setLocal(GoalDeepWorkWeekly(600, stamp = stamp(0, me)), 900, stamp(0, me), unknown.toSync(), me)
        report.check(
            "gs4fix/default-env: asked for with the placeholder, a lowering is refused and a raise applies",
            lowered.result == GoalDeepWorkWeeklyResult.RefusedDuringSession && lowered.state.minutes == 600 && raised.result == GoalDeepWorkWeeklyResult.Applied && raised.state.minutes == 900,
        )
    }

    // ------------------------------------------------------------------------------------------ SHOULD 6 (the result), NIT (weekly-only)

    private fun applyResults(report: CheckReport) {
        report.check("gs4fix/apply: a failed write is Failed, whatever else changed", GoalApplyResult.of(true, true, true) == GoalApplyResult.Failed)
        report.check("gs4fix/apply: lists changed is Changed", GoalApplyResult.of(false, true, false) == GoalApplyResult.Changed)
        report.check("gs4fix/apply: a weekly-only change is Changed (the screens redraw)", GoalApplyResult.of(false, false, true) == GoalApplyResult.Changed)
        report.check("gs4fix/apply: nothing changed is Unchanged", GoalApplyResult.of(false, false, false) == GoalApplyResult.Unchanged)
        report.check(
            "gs4fix/apply: only Failed does not record reads",
            !GoalApplyResult.Failed.recordsReads && GoalApplyResult.Changed.recordsReads && GoalApplyResult.Unchanged.recordsReads,
        )
        report.check(
            "gs4fix/apply: a weekly-only change redraws after a receive, a failure or no change does not",
            PhoneGoalSyncWiring.redrawAfterReceive(GoalApplyResult.of(false, false, true)) &&
                !PhoneGoalSyncWiring.redrawAfterReceive(GoalApplyResult.Failed) && !PhoneGoalSyncWiring.redrawAfterReceive(GoalApplyResult.Unchanged),
        )
    }

    // ------------------------------------------------------------------------------------------ SHOULD 6 and 4 in a pass

    private class Scenario(var outcome: GoalApplyResult = GoalApplyResult.Changed) {
        var now: OffsetDateTime = T0
        val holder = Gs4SelfCheck.Holder()
        val holdsDuringApply = ArrayList<Boolean>()
        var lists = GoalLists(listOf(GoalSeeds.workArea(T0)), listOf(GoalSeeds.deepWork(T0), Gs4SelfCheck.goal(1)))
        val month = GoalHistoryMonth(
            GoalHistoryMonth.CURRENT_FORMAT_VERSION, PC, 2026, 10, T0,
            GoalLists(entries = listOf(entry(2, LocalDate.of(2026, 10, 3)))),
        )
        val store = Gs4SelfCheck.Fake("relay", ".absync", expires = true, canDelete = false, maxFiles = 64).also {
            it.files[GoalHistoryNames.nameFor(GROUP, GoalHistoryFileKey.forMonth(PC, 2026, 10), ".absync")] = Gs4SelfCheck.FakeCodec.seal(month)
        }
        lateinit var sync: GoalHistorySync

        init {
            sync = GoalHistorySync(
                held = { lists },
                applySynced = { merge ->
                    holdsDuringApply.add(sync.holdsLock())
                    val merged = merge(lists)
                    when (outcome) {
                        GoalApplyResult.Failed -> GoalApplyResult.Failed
                        GoalApplyResult.Unchanged -> GoalApplyResult.Unchanged
                        GoalApplyResult.Changed -> {
                            if (merged != null) lists = merged
                            GoalApplyResult.Changed
                        }
                    }
                },
                states = holder, now = { now }, self = { PHONE }, key = { GoalHistorySync.GoalHistoryKey(GROUP, "fp") }, codec = Gs4SelfCheck.FakeCodec,
                devices = { listOf(GoalHistoryDeviceInfo(PC, true, false, now)) }, enabled = { true }, ready = { true },
            )
        }

        fun readCount() = holder.value.transport("relay").read.size
    }

    private fun entry(n: Int, day: LocalDate) =
        GoalEntry(UUID.fromString("77777777-0000-0000-0000-%012d".format(n)), Gs4SelfCheck.goal(1).id, day, null, 1.0, GoalEntrySource.Hand, T0.minusDays(20))

    private fun readsAndLock(report: CheckReport) {
        // A write that failed: the file is NOT recorded as read, so the next pass reads it again; once the write works it is.
        val failing = Scenario(GoalApplyResult.Failed)
        val first = failing.sync.run(failing.store)
        report.check("gs4fix/read: a failed write records no file as read", first.read == 0 && failing.readCount() == 0, "read=${first.read} recorded=${failing.readCount()}")
        failing.outcome = GoalApplyResult.Changed
        failing.now = failing.now.plusMinutes(20)
        val second = failing.sync.run(failing.store)
        report.check("gs4fix/read: the next pass reads it again and records it once taken in", second.read == 1 && failing.readCount() == 1, "read=${second.read}")
        // Nothing needed to change (the rows were already held): the file is read.
        val unchanged = Scenario(GoalApplyResult.Unchanged)
        val third = unchanged.sync.run(unchanged.store)
        report.check("gs4fix/read: rows that were already held count as read", third.read == 1 && unchanged.readCount() == 1)
        // The merge never runs under the history lock.
        report.check(
            "gs4fix/lock: the merge is taken in without the history lock held",
            failing.holdsDuringApply.isNotEmpty() && failing.holdsDuringApply.none { it } && unchanged.holdsDuringApply.none { it },
            failing.holdsDuringApply.toString(),
        )
    }

    // ------------------------------------------------------------------------------------------ SHOULD 4 under threads

    private class Counting(private val inner: GoalHistoryStore, private val inside: AtomicInteger, private val most: AtomicInteger) : GoalHistoryStore by inner {
        override fun list(): List<GoalHistoryRemoteFile> {
            val now = inside.incrementAndGet()
            most.accumulateAndGet(now) { a, b -> maxOf(a, b) }
            try {
                Thread.sleep(1)
                return inner.list()
            } finally {
                inside.decrementAndGet()
            }
        }
    }

    private fun threads(report: CheckReport) {
        val storeLock = Any()
        val lists = AtomicReference(GoalLists(listOf(GoalSeeds.workArea(T0)), listOf(GoalSeeds.deepWork(T0), Gs4SelfCheck.goal(1))))
        val clock = AtomicReference(T0)
        val holder = Gs4SelfCheck.Holder()
        val inside = AtomicInteger()
        val most = AtomicInteger()
        val failures = java.util.Collections.synchronizedList(ArrayList<String>())
        val sync = GoalHistorySync(
            held = { lists.get() },
            // The store's lock, as the phone's store takes it for a merge.
            applySynced = { merge ->
                synchronized(storeLock) {
                    val merged = merge(lists.get())
                    if (merged != null && merged != lists.get()) {
                        lists.set(merged)
                        GoalApplyResult.Changed
                    } else {
                        GoalApplyResult.Unchanged
                    }
                }
            },
            states = holder, now = { clock.get() }, self = { PHONE }, key = { GoalHistorySync.GoalHistoryKey(GROUP, "fp") }, codec = Gs4SelfCheck.FakeCodec,
            devices = { listOf(GoalHistoryDeviceInfo(PC, true, false, clock.get())) }, enabled = { true }, ready = { true },
        )
        fun monthFile(n: Int, month: Int) = GoalHistoryMonth(
            GoalHistoryMonth.CURRENT_FORMAT_VERSION, PC, 2026, month, T0, GoalLists(entries = listOf(entry(n, LocalDate.of(2026, month, 3)))),
        )
        val relay = Gs4SelfCheck.Fake("relay", ".absync", expires = true, canDelete = false, maxFiles = 64)
        val drive = Gs4SelfCheck.Fake("drive", ".abgh", expires = false, canDelete = true)
        relay.files[GoalHistoryNames.nameFor(GROUP, GoalHistoryFileKey.forMonth(PC, 2026, 9), ".absync")] = Gs4SelfCheck.FakeCodec.seal(monthFile(10, 9))
        drive.files[GoalHistoryNames.nameFor(GROUP, GoalHistoryFileKey.forMonth(PC, 2026, 8), ".abgh")] = Gs4SelfCheck.FakeCodec.seal(monthFile(11, 8))
        val countedRelay = Counting(relay, inside, most)
        val countedDrive = Counting(drive, inside, most)

        fun thread(name: String, body: () -> Unit) = Thread {
            try {
                body()
            } catch (e: Throwable) {
                failures.add("$name: ${e::class.simpleName} ${e.message}")
            }
        }.also { it.isDaemon = true; it.name = name }

        val passes = listOf(
            thread("relay-pass") { repeat(40) { i -> clock.set(T0.plusMinutes(20L * (i + 1))); relay.times.keys.forEach { relay.times[it] = clock.get() }; sync.run(countedRelay) } },
            thread("drive-pass") { repeat(40) { i -> drive.times.keys.forEach { drive.times[it] = T0.plusMinutes(20L * (i + 1)) }; sync.run(countedDrive) } },
            // The store's own hook running the pruned-goal look INSIDE the store's lock (the worst case; the app posts it instead).
            thread("store-hook") { repeat(1500) { synchronized(storeLock) { sync.trackPrunedGoals() } } },
            thread("receive") { repeat(1500) { sync.noteRegularFile(PC, null); sync.marker; sync.ownNames("relay"); sync.isHistoryName("0123456789ab.absync", ".absync") } },
        )
        passes.forEach { it.start() }
        val deadline = System.currentTimeMillis() + 30_000
        passes.forEach { it.join(maxOf(1, deadline - System.currentTimeMillis())) }
        val stuck = passes.filter { it.isAlive }.map { it.name }
        report.check("gs4fix/threads: passes, the store's hook and the receive path finish together (no deadlock)", stuck.isEmpty(), "stuck: $stuck")
        report.check("gs4fix/threads: no thread failed", failures.isEmpty(), failures.toString())
        report.check("gs4fix/threads: two passes never overlap (one lock)", most.get() == 1, "most at once: ${most.get()}")
        report.check(
            "gs4fix/threads: the receive path's note landed in the state, the files were read",
            holder.value.lastRegularMerge.containsKey(PC) && lists.get().entries.any { it.id == entry(10, LocalDate.of(2026, 9, 3)).id } &&
                lists.get().entries.any { it.id == entry(11, LocalDate.of(2026, 8, 3)).id },
        )
    }

    // ------------------------------------------------------------------------------------------ SHOULD 5

    private fun names(report: CheckReport) {
        val holder = Gs4SelfCheck.Holder()
        var fingerprint = "fp"
        fun make() = GoalHistorySync(
            held = { GoalLists.EMPTY }, applySynced = { GoalApplyResult.Unchanged }, states = holder, now = { T0 }, self = { PHONE },
            key = { GoalHistorySync.GoalHistoryKey(GROUP, fingerprint) }, codec = Gs4SelfCheck.FakeCodec, devices = { listOf(GoalHistoryDeviceInfo(PC, true, false, null)) },
            enabled = { true }, ready = { true },
        )
        val sync = make()
        val known = GoalHistoryNames.nameFor(GROUP, GoalHistoryFileKey.forMonth(PC, 2026, 11), ".absync")
        val ownYear = GoalHistoryNames.nameFor(GROUP, GoalHistoryFileKey.forYear(2026, PHONE), ".absync")
        val stranger = GoalHistoryNames.nameFor(GROUP, GoalHistoryFileKey.forMonth(UUID.fromString("cccccccc-0000-0000-0000-000000000003"), 2026, 11), ".absync")
        report.check("gs4fix/names: a known device's month file is skipped by name", sync.isHistoryName(known, ".absync"))
        report.check("gs4fix/names: this phone's own year file is skipped by name", sync.isHistoryName(ownYear, ".absync"))
        report.check("gs4fix/names: a sync file's name is not a history name", !sync.isHistoryName("0123456789ab.absync", ".absync") && !sync.isHistoryName(known, ".abgh"))
        report.check("gs4fix/names: a device not heard of yet is not skipped by name (the header fallback tells)", !sync.isHistoryName(stranger, ".absync"))
        // The header found one: remembered, kept in the state, there after a restart, gone with the group's key.
        sync.noteHistoryName(stranger)
        report.check("gs4fix/names: a name found by header is skipped from then on", sync.isHistoryName(stranger, ".absync"))
        report.check("gs4fix/names: it is kept in the saved state", stranger in holder.value.historyNames)
        val saved = GoalHistoryStateJson.read(Json.parseToJsonElement(GoalHistoryStateJson.write(holder.value).toString()))
        report.check("gs4fix/names: the state's JSON carries it", saved != null && stranger in saved.historyNames)
        val restarted = GoalHistorySync(
            held = { GoalLists.EMPTY }, applySynced = { GoalApplyResult.Unchanged }, states = Gs4SelfCheck.Holder().also { it.value = saved ?: GoalHistoryState() }, now = { T0 }, self = { PHONE },
            key = { GoalHistorySync.GoalHistoryKey(GROUP, "fp") }, codec = Gs4SelfCheck.FakeCodec, devices = { emptyList() }, enabled = { true }, ready = { true },
        )
        report.check("gs4fix/names: after a restart it is still skipped without a download", restarted.isHistoryName(stranger, ".absync"))
        val old = GoalHistoryState()
        report.check("gs4fix/names: an older saved state (no names) reads fine", GoalHistoryStateJson.read(Json.parseToJsonElement(GoalHistoryStateJson.write(old).toString().replace("\"historyNames\":[],", ""))) != null)
        fingerprint = "other"
        sync.noteRegularFile(PC, null)
        report.check("gs4fix/names: a new key for the group forgets the names (they were another group's)", !sync.isHistoryName(stranger, ".absync") && holder.value.historyNames.isEmpty())
        // The cap.
        repeat(300) { sync.noteHistoryName("%012x.absync".format(it)) }
        report.check(
            "gs4fix/names: at most 256 names are kept, the newest",
            holder.value.historyNames.size == 256 && "%012x.absync".format(299) in holder.value.historyNames && "%012x.absync".format(0) !in holder.value.historyNames,
            "${holder.value.historyNames.size}",
        )
    }

    // ------------------------------------------------------------------------------------------ SHOULD 3, SHOULD 4, wiring

    private fun wiring(report: CheckReport) {
        val weekly = PhoneGoalSyncWiring.afterWeeklyChange()
        report.check(
            "gs4fix/wiring: a weekly edit or Cancel's re-stamp is backed up AND sent soon",
            PhoneGoalSyncWiring.Action.AutoBackup in weekly && PhoneGoalSyncWiring.Action.SendSyncFiles in weekly,
        )
        val lists = PhoneGoalSyncWiring.afterListChange()
        report.check(
            "gs4fix/wiring: a list change backs up, tracks pruned goals, sends and sets the reminders",
            lists.toSet() == PhoneGoalSyncWiring.Action.values().toSet(),
        )
        report.check(
            "gs4fix/wiring: tracking pruned goals is never run inside the store's lock; the rest may",
            !PhoneGoalSyncWiring.mayRunInsideStoreLock(PhoneGoalSyncWiring.Action.TrackPrunedGoals) &&
                PhoneGoalSyncWiring.Action.values().filter { it != PhoneGoalSyncWiring.Action.TrackPrunedGoals }.all { PhoneGoalSyncWiring.mayRunInsideStoreLock(it) },
        )
    }
}
