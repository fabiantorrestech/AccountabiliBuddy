package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.SyncGoalYearMerged
import java.time.Duration
import java.time.OffsetDateTime
import java.util.TreeSet
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.withLock

/**
 * The shared goal history on this device (ADR 79 "shared history", owner 2026-11-14; GS4; the twin of the PC's `GoalHistorySync`): each
 * device's month files and the yearly roll-up travel as sealed files next to the sync files, on Drive (the app's own folder, drive.file)
 * and on the user's own relay, by the rules of [GoalHistoryRules]. One pass per transport, in this order:
 * 1. **List** the transport and pick the files this device knows by name ([GoalHistoryNames]).
 * 2. **Read** what [GoalHistoryRules.toRead] says (new or changed since last read; everything while new to the group; a stale device's files
 *    in full): merged into the goals AFTER the regular files' goals part, union by id, newest wins, entry tombstones kept; a file that brought
 *    rows of a goal this device doesn't hold yet is not recorded as read unless the goal's tombstone was pruned here (Q649). A year file
 *    merged goes into this device's own `goalYearsMerged` marker (sent in its regular file).
 * 3. **Roll up** a finished year when this device is the lowest-id active PC. A phone is never the roller (it only reads year files).
 * 4. **Write** this device's month files: any month whose content changed, at most daily per file, every month on the first run.
 * 5. **Remove** own month files that [GoalHistoryRules.mayRemoveMonth] frees: deleted on Drive; on the relay simply no longer put again.
 * 6. **Put again** own files the relay would expire (25 days, the owner's rule) or that went missing.
 * ON THE RELAY (the PC's review fixes, rule for rule): only the PC that rolls the year keeps finished month files there, every other device its
 * open months only, each device at most [GoalHistoryRules.relayQuota] files, newest months first ([GoalHistoryRules.relayKeep]); nothing is
 * written while a device of the group is on an app that doesn't know history files ([GoalHistoryRules.knowsHistory]); a pass makes at most the
 * store's request cap; a 429 answer to a put ([GoalHistoryGroupFullException]) ends the pass quietly; a month is put again as the same bytes
 * (get, put); a phone deletes nothing there (own files only, and only when it leaves the group).
 * A file bigger than the store's limit (the relay's 4 MiB) is not written, and the relay keeps room for the devices' sync files. Sealed exactly
 * like the sync files (the group's key, AES-256-GCM, [codec]); never in a QR file. Logs counts only. Pure: no clock, no network, no storage
 * of its own, so the JVM checks drive it with a fake store and codec.
 *
 * Threads (GS4 review): the Drive and relay sources run their passes on their own threads, and the receive path and the store's change hook
 * call [noteRegularFile] and [trackPrunedGoals]. One lock covers every pass and every change of the state, so they never overlap.
 * It is **never held while [applySynced] runs** (that takes the goal store's lock; a thread holding the store's lock and wanting this one
 * would deadlock): a pass lets go for that call and checks afterwards that the state is still the one it started with. [trackPrunedGoals] is
 * called off the store's lock by the app (posted). The cheap readers ([marker], [prunedGoals], [ownNames], [isHistoryName]) read a snapshot
 * published at the end of each locked step and never wait for a pass over the network.
 */
class GoalHistorySync(
    /** This device's lists as the sync file carries them (Deep work composed), or null while the goals aren't ready. */
    private val held: () -> GoalLists?,
    /** Takes a merge in INSIDE the store's lock: gets the held lists, returns the merged ones (null = nothing to take in); true when anything changed. */
    private val applySynced: ((GoalLists) -> GoalLists?) -> GoalApplyResult,
    private val states: GoalHistoryStateHolder,
    private val now: () -> OffsetDateTime,
    private val self: () -> UUID?,
    /** The group's key as the history needs it: its id (in the names) and a fingerprint (the state's group); null while not in a group. */
    private val key: () -> GoalHistoryKey?,
    private val codec: GoalHistoryCodec,
    private val devices: () -> List<GoalHistoryDeviceInfo>,
    private val enabled: () -> Boolean,
    /** The goals are usable (the store is Ready). */
    private val ready: () -> Boolean,
    private val log: (String) -> Unit = {},
) {
    /** The group's key as the history files need it. */
    data class GoalHistoryKey(val groupId: String, val fingerprint: String)

    private val lock = ReentrantLock()
    private val lastRun = ConcurrentHashMap<String, OffsetDateTime>()
    private val unreadableTried = HashMap<String, OffsetDateTime>()
    private val staleNames = HashMap<String, List<String>>()
    private var previousGoals: GoalLists? = null

    /** Raised when this device's "merged year files" marker changed: its regular sync file is written again. */
    var onMarkerChanged: () -> Unit = {}

    /**
     * A goal history month file of another device was merged (ADR 86 receipts; the PC's `MonthMerged`): the device, the month as "yyyy-MM" and the
     * file's listed time. Ids, a month and a time only; set by the app to record it in the receiver-local record.
     */
    var onMonthMerged: (UUID, String, OffsetDateTime) -> Unit = { _, _, _ -> }

    /** What the cheap readers see: replaced whole after each locked step, so a reader never waits for a pass or sees a half-changed state. */
    private class Snapshot(val marker: List<SyncGoalYearMerged>?, val pruned: Set<UUID>, val ownNames: Map<String, List<String>>, val knownIds: Set<UUID>)

    @Volatile
    private var snapshot = Snapshot(null, emptySet(), emptyMap(), emptySet())

    private val namesLock = Any()

    // The history names found by header (see [noteHistoryName]); persisted in the state at the next locked step.
    @Volatile
    private var notedNames: List<String> = emptyList()
    private var namesDirty = false

    @Volatile
    private var nameCache: Pair<String, Set<String>>? = null

    init {
        previousGoals = if (ready()) held() else null
        notedNames = states.value.historyNames.toList()
        publish()
    }

    /** The year files this device has merged, for its regular sync file (`goalYearsMerged`); null for none. */
    val marker: List<SyncGoalYearMerged>? get() = snapshot.marker

    /** The ids of the goals whose tombstone this device pruned (Q649), kept for good. */
    val prunedGoals: Set<UUID> get() = snapshot.pruned

    /** Every name this device put on the transport: they go when it leaves the group (the relay) or when the key changes. */
    fun ownNames(transport: String): List<String> = snapshot.ownNames[transport] ?: emptyList()

    /** Whether this thread holds the history lock (for the checks: [applySynced] must never run while it does). */
    fun holdsLock(): Boolean = lock.isHeldByCurrentThread

    private fun publish() {
        val state = states.value
        val ids = HashSet<UUID>()
        ids.addAll(state.deviceMarkers.keys)
        ids.addAll(state.lastRegularMerge.keys)
        ids.addAll(state.lastMergeFrom.keys)
        snapshot = Snapshot(state.ownMarker.ifEmpty { null }, state.prunedGoals.toSet(), state.transports.mapValues { it.value.ownNames.toList() }, ids)
    }

    /**
     * Whether [name] is the name of a goal history file this device knows (the twin of the PC's `IsHistoryName`): the names its devices,
     * years and months can have, plus the names found by header ([noteHistoryName]). The relay's regular reader leaves such a file alone
     * WITHOUT downloading it. Never waits for a pass.
     */
    fun isHistoryName(name: String, extension: String): Boolean {
        val k = key() ?: return false
        if (name in notedNames) return true
        val ids = HashSet<UUID>(snapshot.knownIds)
        self()?.let { ids.add(it) }
        devices().forEach { ids.add(it.id) }
        val lastYear = now().withOffsetSameInstant(java.time.ZoneOffset.UTC).year + 1
        val cacheKey = k.groupId + "|" + extension + "|" + lastYear + "|" + ids.map { it.toString() }.sorted().joinToString(",")
        val cached = nameCache
        val names = if (cached != null && cached.first == cacheKey) cached.second else GoalHistoryNames.candidates(k.groupId, extension, ids, lastYear).keys.toHashSet().also { nameCache = cacheKey to it }
        return name in names
    }

    /**
     * A file the header showed to be a history file whose name wasn't known yet: remembered (and kept in the state across restarts) so
     * it is not downloaded again. Never waits for a pass; the state is written at the pass's end if one is running.
     */
    fun noteHistoryName(name: String) {
        synchronized(namesLock) {
            if (name in notedNames) return
            notedNames = (notedNames + name).takeLast(MAX_HISTORY_NAMES)
            namesDirty = true
        }

        if (lock.tryLock()) {
            try {
                flushNames()
            } finally {
                lock.unlock()
            }
        }
    }

    // Inside the lock: the noted names go into the state and the state is saved.
    private fun flushNames() {
        val names = synchronized(namesLock) {
            if (!namesDirty) return
            namesDirty = false
            notedNames
        }

        val state = states.value
        state.historyNames.clear()
        state.historyNames.addAll(names)
        states.save()
    }

    /**
     * A regular sync file with goal parts merged here: the device is syncing (its last complete merge and its last regular merge are now),
     * this device is no longer new to the group, and the device's "merged year files" marker is kept (the copy of its file kept for display has none).
     */
    fun noteRegularFile(deviceId: UUID, yearsMerged: List<SyncGoalYearMerged>?) = lock.withLock {
        val k = key() ?: return@withLock
        val state = stateFor(k)
        val at = now()
        state.goalsPartMerged = true
        state.lastRegularMerge[deviceId] = at
        state.lastMergeFrom[deviceId] = at
        if (yearsMerged != null) state.deviceMarkers[deviceId] = yearsMerged else state.deviceMarkers.remove(deviceId)
        states.save()
        publish()
    }

    /**
     * "Re-read goal history (full)" (SYNC1c; the PC's `ForceRereadAsync`): this device is new to the group on [transport] again, so the next pass
     * reads every listed file once, its own too (what a first run does; nothing is deleted, nothing bypasses the pass's request cap, the relay's
     * quota or the "no download before a goals part merged" gate). The caller then runs a normal pass ([run]).
     */
    fun forceReread(transport: String) = lock.withLock {
        val k = key() ?: return@withLock
        val state = stateFor(k).transport(transport)
        state.newToGroup = true
        state.read.clear()
        state.readTag.clear()
        unreadableTried.clear()
        states.save()
    }

    /**
     * "Send my files again" for the goal history (SYNC1c; the PC's `ResendKeptAsync`): this device's KEPT month files on [transport] are marked to
     * be put again, and the caller runs a normal pass ([run]). On the relay the "uploaded" time of each own file is forgotten, so a kept month is
     * put again as the same bytes through the same rules (the roller keeps finished months, any other device its open months only, never more than
     * the quota, nothing while an older app is in the group, at most 40 requests a pass, a 429 ends the pass); a month that rolled up and was
     * removed is never put again. On Drive the kept months are written again (a month that rolled up is not).
     */
    fun resendKept(transport: String) = lock.withLock {
        val k = key() ?: return@withLock
        val state = stateFor(k).transport(transport)
        if (transport == "relay") {
            // The relay's own files are put again as the very same bytes (the 25-day re-upload path): forgetting when they were uploaded is enough.
            state.uploaded.clear()
        } else {
            // Drive keeps files until they are removed: the kept months are written again (the content compare sees no "last" version).
            for (month in state.written.keys.filter { it !in state.removed }) state.written.remove(month)
        }

        states.save()
    }

    /** Whether a pass over [transport] is due ([RUN_EVERY]). */
    fun due(transport: String): Boolean {
        val last = lastRun[transport] ?: return true
        return Duration.between(last.toInstant(), now().toInstant()) >= RUN_EVERY
    }

    /** A pass when one is due; nothing otherwise (no network). */
    fun runIfDue(store: GoalHistoryStore): GoalHistoryRun = if (due(store.transport)) run(store) else GoalHistoryRun()

    /** One pass over the transport (see the class summary). Transport errors are the caller's to catch; what was done before one stays recorded. */
    fun run(store: GoalHistoryStore): GoalHistoryRun {
        val run = GoalHistoryRun()
        val markerChanged = lock.withLock { runLocked(store, run) }
        // Outside the lock: it sends this phone's file again.
        if (markerChanged) onMarkerChanged()
        return run
    }

    // Holds the lock except while [applySynced] runs. True when this device's merged-years marker changed.
    private fun runLocked(store: GoalHistoryStore, run: GoalHistoryRun): Boolean {
        val me = self()
        val k = key()
        if (!enabled() || me == null || me == ZERO || k == null || !ready()) return false

        run.ran = true
        lastRun[store.transport] = now()
        val state = stateFor(k)
        val markerText = markerText(state)
        var markerChanged = false
        try {
            deleteStaleNames(store)
            val pass = Pass(store, state, state.transport(store.transport), me, k, relayGated(store))
            pass.spend()
            list(pass)
            read(pass, run)
            if (!pass.stopped && roll(pass, run) && pass.spend()) {
                list(pass)
                read(pass, run)
            }

            if (!pass.stopped) write(pass, run)
            if (!pass.stopped) remove(pass, run)
            if (!pass.stopped) reuploadYears(pass, run)
            run.capped = pass.capped
        } catch (e: StalePass) {
            // The group's key changed while the merge was taken in: this pass belongs to the old state; the next one starts over.
        } finally {
            flushNames()
            states.save()
            publish()
            markerChanged = markerText(state) != markerText
            log("goal history ${store.transport}: $run")
        }

        return markerChanged
    }

    private class StalePass : RuntimeException(null, null, false, false)

    // Lets go of the lock (however many times this thread holds it) for [block] and takes it back; the pass then checks its state is current.
    private inline fun <T> outsideLock(pass: Pass, block: () -> T): T {
        val holds = lock.holdCount
        repeat(holds) { lock.unlock() }
        try {
            return block()
        } finally {
            repeat(holds) { lock.lock() }
            if (states.value !== pass.state) throw StalePass()
        }
    }

    /** Goals whose tombstone went since the last look (Q649): kept, with the id only. Call after every change of the lists. */
    fun trackPrunedGoals() = lock.withLock {
        if (!ready()) return@withLock
        val after = held() ?: return@withLock
        val state = states.value
        val heldIds = HashSet<UUID>(after.goals.map { it.id })
        after.newer.goals.mapNotNull { GoalRawItem.tryRead(it.json) }.forEach { heldIds.add(it.id) }
        val pruned = HashSet<UUID>()
        previousGoals?.let { pruned.addAll(GoalHistoryRules.prunedGoalIds(it, after)) }
        // A tombstone seen in an earlier run of the app and gone now (pruned while the app was off).
        pruned.addAll(state.tombstonedGoals.filter { it !in heldIds })
        val tombstoned = after.goals.filter { !it.isLive }.map { it.id }.toSet()
        val changed = !state.prunedGoals.containsAll(pruned) || tombstoned != state.tombstonedGoals
        previousGoals = after
        if (changed) {
            state.prunedGoals.addAll(pruned)
            state.tombstonedGoals = tombstoned
            states.save()
            publish()
        }
    }

    // ON THE RELAY: nothing is written while a device of the group (not removed) is on an app that doesn't know history files.
    private fun relayGated(store: GoalHistoryStore): Boolean =
        store.maxFiles != null && devices().filter { !it.removed }.any { !GoalHistoryRules.knowsHistory(it.kind, it.appVersion) }

    // Every device this one knows: itself, the cards it received and the ids its state keeps.
    private fun knownDevices(me: UUID, state: GoalHistoryState): Set<UUID> {
        val ids = HashSet<UUID>()
        ids.add(me)
        ids.addAll(devices().map { it.id })
        ids.addAll(state.deviceMarkers.keys)
        ids.addAll(state.lastRegularMerge.keys)
        ids.addAll(state.lastMergeFrom.keys)
        return ids
    }

    // ---------------------------------------------------------------------------------------------------- state

    private fun stateFor(k: GoalHistoryKey): GoalHistoryState {
        var state = states.value
        if (state.group == k.fingerprint) return state

        // A different key (the group was rekeyed or joined again): everything is new. The files under the old key are of no use to the
        // new group; Drive's are deleted at the next pass, the relay's expire (its own group was deleted on leaving).
        if (state.group.isNotEmpty()) {
            for ((transport, old) in state.transports) if (old.ownNames.isNotEmpty()) staleNames[transport] = old.ownNames.toList()
        }

        val pruned = state.prunedGoals.toSet()
        val tombstoned = state.tombstonedGoals
        states.reset()
        state = states.value
        synchronized(namesLock) {
            notedNames = emptyList()
            namesDirty = false
        }
        state.group = k.fingerprint
        state.prunedGoals.addAll(pruned)
        state.tombstonedGoals = tombstoned
        return state
    }

    private fun deleteStaleNames(store: GoalHistoryStore) {
        if (!store.canDelete) return
        val names = staleNames.remove(store.transport) ?: return
        for (name in names) store.delete(name)
    }

    private fun markerText(state: GoalHistoryState): String =
        state.ownMarker.joinToString(";") { "${it.year}/${it.rolledBy}/${DotNetTime.ticks(it.rolledAt)}" }

    // ---------------------------------------------------------------------------------------------------- list

    private fun list(pass: Pass) {
        val listing = pass.store.list()
        val at = now()
        val ids = knownDevices(pass.me, pass.state)
        val names = GoalHistoryNames.candidates(pass.key.groupId, pass.store.extension, ids, at.withOffsetSameInstant(java.time.ZoneOffset.UTC).year + 1)
        pass.totalFiles = listing.size
        pass.newFiles = 0
        pass.remote.clear()
        pass.history.clear()
        for (file in listing) {
            val fileKey = names[file.name] ?: continue
            pass.remote[fileKey] = file
            pass.history.add(GoalHistoryListing(fileKey, file.modifiedAt))
        }
    }

    // ---------------------------------------------------------------------------------------------------- read

    private fun read(pass: Pass, run: GoalHistoryRun) {
        val at = now()
        val state = pass.state
        val transport = pass.transport
        val infos = devices()
        val removed = infos.filter { it.removed }.map { it.id }.toSet()
        val lastMerge = GoalHistoryRules.forgetRemoved(state.lastMergeFrom, removed)
        val lastRead = HashMap<GoalHistoryFileKey, OffsetDateTime>()
        for ((text, readAt) in transport.read) GoalHistoryNames.parseKey(text)?.let { lastRead[it] = readAt }

        // Rows read before any regular file's goals part has merged are all dropped (no goal is held yet): nothing is downloaded for them. A
        // device that knows no other device has no goals part to wait for.
        val goalsReady = state.goalsPartMerged || infos.none { it.id != pass.me && !it.removed }
        val newToGroup = transport.newToGroup || !goalsReady
        var toRead = if (goalsReady) GoalHistoryRules.toRead(pass.history, lastRead, lastMerge, removed, pass.me, newToGroup, at) else emptyList()
        if (newToGroup && toRead.isNotEmpty()) {
            // New to the group, a device reads every file once, its own too; one already read at its listed version is not downloaded again.
            val ordinary = GoalHistoryRules.toRead(pass.history, lastRead, lastMerge, removed, pass.me, false, at).map { it.key }.toSet()
            toRead = toRead.filter { it.key in ordinary || !DotNetTime.same(lastRead[it.key], it.modifiedAt) }
        }

        val staleBefore = pass.history
            .filter { it.key.kind == GoalHistoryFileKind.Month && it.key.deviceId != null && it.key.deviceId != pass.me && it.key.deviceId !in removed }
            .map { it.key.deviceId!! }
            .filter { device -> lastMerge[device]?.let { Duration.between(it.toInstant(), at.toInstant()) > GoalHistoryRules.FULL_READ_AFTER } ?: true }
            .toSet()

        val rows = ArrayList<GoalHistoryRows>()
        val years = HashMap<GoalHistoryFileKey, GoalHistoryYear>()
        val skipped = HashSet<GoalHistoryFileKey>()
        for (file in toRead) {
            val remote = pass.remote[file.key] ?: continue
            val text = GoalHistoryNames.keyText(file.key)
            val tag = remote.tag
            if (tag != null && transport.read.containsKey(text) && transport.readTag[text] == tag) {
                // The same bytes as the ones merged before (the relay's etag): only its time moved (put again).
                transport.read[text] = file.modifiedAt
                continue
            }

            val tried = unreadableTried[remote.name]
            if (remote.size > pass.store.maxFileBytes || (tried != null && Duration.between(tried.toInstant(), at.toInstant()) < RETRY_UNREADABLE_AFTER)) {
                // Logged when it was found; it never holds a stale device's complete read or the "new to the group" state back.
                skipped.add(file.key)
                continue
            }

            if (!pass.spend()) break
            val bytes = pass.store.get(remote.name) ?: continue
            if (file.key.kind == GoalHistoryFileKind.Month) {
                val month = codec.openMonth(bytes)
                if (month == null || month.key != file.key) {
                    noteUnreadable(remote, run, at)
                    skipped.add(file.key)
                    continue
                }

                rows.add(GoalHistoryRows(file, month.entries))
            } else {
                val year = codec.openYear(bytes)
                if (year == null || year.key != file.key) {
                    noteUnreadable(remote, run, at)
                    skipped.add(file.key)
                    continue
                }

                rows.add(GoalHistoryRows(file, year.entries))
                years[file.key] = year
            }
        }

        if (rows.isNotEmpty()) {
            var merged: GoalHistoryMerged? = null
            // The pruned ids as they are now (a copy: another thread may add to the set while the merge runs without the lock).
            val prunedNow = state.prunedGoals.toSet()
            val late = GoalHistoryLatePrune(state.firstDropAt, activeDevices(pass, at), pass.me, state.lastRegularMerge.toMap())
            val outcome = outsideLock(pass) {
                applySynced { heldLists ->
                    val result = GoalHistoryRules.merge(heldLists, null, rows, at, prunedNow, late)
                    merged = result
                    result.lists
                }
            }

            // Recorded as read only when the rows were taken in (or were already there): a failed write leaves the files to be read again.
            merged?.takeIf { outcome.recordsReads }?.let { result ->
                // A late joiner: goals no active device knows any more (all rows older than 30 days) are pruned goals; their files count as read.
                if (result.newlyPruned.isNotEmpty()) state.prunedGoals.addAll(result.newlyPruned)
                state.firstDropAt = if (result.dropped.any { it.goalId !in state.prunedGoals }) state.firstDropAt ?: at else null
                for (readFile in result.read) {
                    val text = GoalHistoryNames.keyText(readFile.key)
                    transport.read[text] = readFile.modifiedAt
                    lastRead[readFile.key] = readFile.modifiedAt
                    pass.remote[readFile.key]?.tag?.let { transport.readTag[text] = it }
                    run.read++
                    years[readFile.key]?.let { year ->
                        state.ownMarker = GoalHistoryRules.marked(state.ownMarker, year)
                        keepYear(transport, year)
                    }
                }

                trackPrunedGoals()
                for (readFile in result.read) {
                    val owner = readFile.key.deviceId
                    val month = readFile.key.month
                    if (readFile.key.kind == GoalHistoryFileKind.Month && owner != null && owner != pass.me && month != null) {
                        runCatching { onMonthMerged(owner, GoalHistoryNames.monthText(readFile.key.year, month), readFile.modifiedAt) }
                    }
                }
            }
        }

        // A stale device whose files are all read now has a complete merge: it is not stale again until 90 days pass. A file that is too big or
        // can't be opened is logged and left out of that.
        for (device in staleBefore) {
            val complete = pass.history
                .filter { it.key.kind == GoalHistoryFileKind.Month && it.key.deviceId == device && it.key !in skipped }
                .all { file -> transport.read[GoalHistoryNames.keyText(file.key)]?.let { DotNetTime.same(it, file.modifiedAt) } == true }
            if (complete) state.lastMergeFrom[device] = at
        }

        // No longer new to the group once a regular goals part has merged and every listed file is read or left out.
        if (transport.newToGroup && goalsReady && !pass.stopped && GoalHistoryRules.allAccountedFor(pass.history, lastRead, skipped)) {
            transport.newToGroup = false
        }
    }

    private fun noteUnreadable(remote: GoalHistoryRemoteFile, run: GoalHistoryRun, at: OffsetDateTime) {
        unreadableTried[remote.name] = at
        run.unreadable++
    }

    private fun keepYear(transport: GoalHistoryTransportState, year: GoalHistoryYear) {
        val text = GoalHistoryNames.keyText(year.key)
        val kept = transport.years[text]
        if (kept == null || DotNetTime.compare(year.rolledAt, kept.rolledAt) >= 0) transport.years[text] = year.copy(entries = GoalLists.EMPTY)
    }

    // ---------------------------------------------------------------------------------------------------- roll up

    private fun roll(pass: Pass, run: GoalHistoryRun): Boolean {
        val at = now()
        val active = activeDevices(pass, at)
        if (GoalHistoryRules.roller(active) != pass.me) return false

        var rolled = false
        val years = pass.history
            .filter { it.key.kind == GoalHistoryFileKind.Month && GoalHistoryRules.rollDue(it.key.year, at) }
            .map { it.key.year }.distinct().sorted()
        for (year in years) {
            val monthFiles = pass.history.filter { it.key.kind == GoalHistoryFileKind.Month && it.key.year == year }
            val checked = pass.transport.rollChecked[year]
            if (checked != null && Duration.between(checked.toInstant(), at.toInstant()) < ROLL_EVERY && monthFiles.all { DotNetTime.compare(it.modifiedAt, checked) <= 0 }) continue
            if (pass.gated) {
                run.gated++
                continue
            }

            var existing: GoalHistoryYear? = null
            val yearFile = pass.history
                .filter { it.key.kind == GoalHistoryFileKind.Year && it.key.year == year }
                .sortedWith(compareByDescending<GoalHistoryListing> { it.key.deviceId == pass.me }.thenByDescending { DotNetTime.ticks(it.modifiedAt) })
                .firstOrNull()
            if (yearFile != null) {
                val remote = pass.remote.getValue(yearFile.key)
                if (remote.size <= pass.store.maxFileBytes && !pass.spend()) return rolled
                val bytes = if (remote.size > pass.store.maxFileBytes) null else pass.store.get(remote.name)
                existing = bytes?.let { codec.openYear(it) }
                if (existing == null || existing.key != yearFile.key) {
                    // A year file that is listed and can't be read is never rolled over.
                    run.unreadable++
                    continue
                }

                keepYear(pass.transport, existing)
            }

            val months = ArrayList<GoalHistoryMonth>()
            for (file in monthFiles) {
                val own = if (file.key.deviceId == pass.me) pass.transport.written[GoalHistoryNames.monthText(file.key.year, file.key.month ?: 0)] else null
                if (own != null) {
                    months.add(own)
                    continue
                }

                val remote = pass.remote.getValue(file.key)
                if (remote.size <= pass.store.maxFileBytes && !pass.spend()) return rolled
                val bytes = if (remote.size > pass.store.maxFileBytes) null else pass.store.get(remote.name)
                val month = bytes?.let { codec.openMonth(it) }
                if (month != null && month.key == file.key) months.add(month)
            }

            pass.transport.rollChecked[year] = at
            val rolledYear = GoalHistoryRules.rollUp(existing, months, year, pass.me, pass.history, at) ?: continue
            val sealedYear = codec.seal(rolledYear)
            val name = GoalHistoryNames.nameFor(pass.key.groupId, rolledYear.key, pass.store.extension)
            if (!fits(pass, run, sealedYear.size, name)) continue

            if (!putFile(pass, run, name, sealedYear)) return rolled
            pass.newFiles++
            pass.transport.ownNames.add(name)
            pass.transport.ownYearNames.add(name)
            pass.transport.uploaded[name] = at
            keepYear(pass.transport, rolledYear)
            run.rolled++
            rolled = true
        }

        return rolled
    }

    private fun activeDevices(pass: Pass, at: OffsetDateTime): List<GoalHistoryDevice> {
        val group = ArrayList<GoalGroupDevice>()
        for (info in devices()) {
            // Only a device whose regular file WITH goal parts merged here counts: an app that doesn't sync goals never merges history.
            group.add(GoalGroupDevice(info.id, info.isPc, info.removed, pass.state.lastRegularMerge[info.id], pass.state.deviceMarkers[info.id]))
        }

        // This device is a phone: counted among the group's devices, never as a PC (a phone never rolls a year).
        group.add(GoalGroupDevice(pass.me, false, false, at, pass.state.ownMarker))
        return GoalHistoryRules.activeDevices(group, pass.me, at)
    }

    // ---------------------------------------------------------------------------------------------------- write

    private fun write(pass: Pass, run: GoalHistoryRun) {
        val lists = held() ?: return
        val at = now()
        val transport = pass.transport
        forgetVanishedYears(pass)
        val months = TreeSet<Pair<Int, Int>>(compareBy({ it.first }, { it.second }))
        months.addAll(GoalHistoryRules.monthsOf(lists))
        for (text in transport.written.keys) {
            val parts = text.split('-')
            val year = parts.getOrNull(0)?.toIntOrNull()
            val month = parts.getOrNull(1)?.toIntOrNull()
            if (parts.size == 2 && year != null && month != null) months.add(year to month)
        }

        // ON THE RELAY: only the roller keeps finished months, a device at most its quota, newest first ([GoalHistoryRules.relayKeep]).
        val keep = if (pass.store.maxFiles == null) null else relayMonths(pass, months, at)
        for ((year, month) in months.descendingSet()) {
            if (pass.stopped) return
            if (keep != null && (year to month) !in keep) continue
            val monthText = GoalHistoryNames.monthText(year, month)
            val last = transport.written[monthText]
            val fresh = GoalHistoryRules.monthToWrite(last, lists, pass.me, year, month, at)
            var toPut = fresh
            val name = GoalHistoryNames.nameFor(pass.key.groupId, GoalHistoryFileKey.forMonth(pass.me, year, month), pass.store.extension)
            val listed = pass.remote.values.any { it.name == name }
            var reupload = false
            var sameBytes = false
            if (toPut == null && last != null && monthText !in transport.removed) {
                // The same content: put again only when the file is missing here, or the store would expire it.
                val missing = !listed
                val uploaded = transport.uploaded[name]
                val aging = pass.store.expires && (uploaded == null || Duration.between(uploaded.toInstant(), at.toInstant()) >= REUPLOAD_EVERY)
                if (missing || aging) {
                    toPut = last
                    reupload = true
                    sameBytes = !missing
                }
            }

            if (toPut == null) continue
            if (pass.gated) {
                // A device of the group is on an app that would take the file for an unreadable sync file.
                run.gated++
                continue
            }

            var sealedMonth: ByteArray? = null
            if (sameBytes) {
                // Like a year file: the very same bytes, so the relay's etag doesn't change and no reader downloads it again.
                if (!pass.spend()) return
                val current = pass.store.get(name)
                if (current != null && codec.openMonth(current)?.key == toPut.key) sealedMonth = current
            }

            val sealed = sealedMonth ?: codec.seal(toPut)
            if (!fits(pass, run, sealed.size, name, listed)) continue
            if (!putFile(pass, run, name, sealed)) return

            if (!listed) pass.newFiles++
            transport.written[monthText] = toPut
            transport.uploaded[name] = at
            transport.ownNames.add(name)
            transport.removed.remove(monthText)
            if (reupload) run.reuploaded++ else run.written++
        }
    }

    // ON THE RELAY: the months this device keeps there now ([GoalHistoryRules.relayKeep] with [GoalHistoryRules.relayQuota]): the roller all,
    // the others their open months; at most the quota; a month already rolled up and removed is not a candidate.
    private fun relayMonths(pass: Pass, months: Iterable<Pair<Int, Int>>, at: OffsetDateTime): Set<Pair<Int, Int>> {
        val removed = devices().filter { it.removed }.map { it.id }.toSet()
        val roller = GoalHistoryRules.roller(activeDevices(pass, at)) == pass.me
        val yearFiles = pass.history.count { it.key.kind == GoalHistoryFileKind.Year }
        val known = maxOf(1, knownDevices(pass.me, pass.state).count { it !in removed })
        val quota = GoalHistoryRules.relayQuota(yearFiles, known)
        val candidates = months.filter { GoalHistoryNames.monthText(it.first, it.second) !in pass.transport.removed }
        return GoalHistoryRules.relayKeep(candidates, roller, quota, at).toSet()
    }

    // A year file this device has seen that is no longer listed (on the relay: its roller was away for over 30 days): the months of that year
    // it had removed are put again and rolled again, and the year's old record goes so no month is removed on its account.
    private fun forgetVanishedYears(pass: Pass) {
        val transport = pass.transport
        for (year in transport.years.values.map { it.year }.distinct()) {
            if (pass.history.any { it.key.kind == GoalHistoryFileKind.Year && it.key.year == year }) continue
            transport.removed.removeAll { it.startsWith("%04d-".format(year)) }
            transport.years.entries.removeAll { it.value.year == year }
            transport.rollChecked.remove(year)
        }
    }

    // One put. A 429 on the relay means the group is full (or the rate limit): the pass ends here, quietly, with no retry.
    private fun putFile(pass: Pass, run: GoalHistoryRun, name: String, bytes: ByteArray): Boolean {
        if (!pass.spend()) return false
        return try {
            pass.store.put(name, bytes)
            true
        } catch (e: GoalHistoryGroupFullException) {
            run.groupFull++
            pass.stopped = true
            false
        }
    }

    // The store's limits: a file over its size is not written; a last guard keeps a few places of the relay's group free.
    private fun fits(pass: Pass, run: GoalHistoryRun, length: Int, name: String, listed: Boolean = false): Boolean {
        if (length > pass.store.maxFileBytes) {
            run.tooBig++
            return false
        }

        val isNew = !listed && pass.remote.values.none { it.name == name }
        val limit = pass.store.maxFiles
        if (isNew && limit != null && pass.totalFiles + pass.newFiles + 1 > limit - RELAY_HEADROOM) {
            run.groupFull++
            return false
        }

        return true
    }

    // ---------------------------------------------------------------------------------------------------- remove

    private fun remove(pass: Pass, run: GoalHistoryRun) {
        val at = now()
        val transport = pass.transport
        val active = activeDevices(pass, at)
        for ((monthText, written) in transport.written.entries.toList()) {
            if (monthText in transport.removed) continue
            val monthKey = written.key
            val name = GoalHistoryNames.nameFor(pass.key.groupId, monthKey, pass.store.extension)
            if (!mayRemove(transport, monthKey, written.writtenAt, active, at)) continue

            if (pass.store.canDelete) {
                if (!pass.spend()) return
                pass.store.delete(name)
                transport.uploaded.remove(name)
                transport.ownNames.remove(name)
            }

            // On the relay the file is just not put again; it expires.
            transport.removed.add(monthText)
            run.removed++
        }
    }

    private fun mayRemove(transport: GoalHistoryTransportState, monthKey: GoalHistoryFileKey, writtenAt: OffsetDateTime, active: List<GoalHistoryDevice>, at: OffsetDateTime): Boolean =
        transport.years.values.filter { it.year == monthKey.year }.any { GoalHistoryRules.mayRemoveMonth(monthKey, writtenAt, it, active, at) }

    // ---------------------------------------------------------------------------------------------------- put again

    // The year files this device rolled: put again as they are (the same bytes) before the relay would expire them.
    private fun reuploadYears(pass: Pass, run: GoalHistoryRun) {
        if (!pass.store.expires) return
        val at = now()
        for (name in pass.transport.ownYearNames.toList()) {
            val uploaded = pass.transport.uploaded[name]
            if (uploaded != null && Duration.between(uploaded.toInstant(), at.toInstant()) < REUPLOAD_EVERY) continue
            if (pass.gated) {
                run.gated++
                continue
            }

            if (!pass.spend()) return
            val bytes = pass.store.get(name)
            if (bytes == null) {
                // Gone (expired): the next roll-up writes it again if its month files are still here.
                pass.transport.ownYearNames.remove(name)
                pass.transport.ownNames.remove(name)
                pass.transport.uploaded.remove(name)
                pass.transport.rollChecked.clear()
                continue
            }

            if (!putFile(pass, run, name, bytes)) return
            pass.transport.uploaded[name] = at
            run.reuploaded++
        }
    }

    private class Pass(
        val store: GoalHistoryStore, val state: GoalHistoryState, val transport: GoalHistoryTransportState, val me: UUID, val key: GoalHistoryKey,
        /** ON THE RELAY: a device of the group is on an app that doesn't know history files, so nothing is written. */
        val gated: Boolean,
    ) {
        private var requests = 0

        /** The history files the listing holds, by key. */
        val remote = HashMap<GoalHistoryFileKey, GoalHistoryRemoteFile>()
        val history = ArrayList<GoalHistoryListing>()
        var totalFiles = 0

        /** Files this pass added to the store (they count toward its file limit). */
        var newFiles = 0

        /** The pass ends here: the request cap was reached, or the relay answered 429 to a put. */
        var stopped = false

        /** The pass ended on the request cap (not on a 429). */
        var capped = false
            private set

        /** Counts one request; false (and the pass is over) when the store's cap per pass is used up. */
        fun spend(): Boolean {
            if (stopped) return false
            val cap = store.maxRequestsPerPass
            if (cap != null && requests >= cap) {
                stopped = true
                capped = true
                return false
            }

            requests++
            return true
        }
    }

    companion object {
        /** The relay deletes files untouched for 30 days (ADR 46); own files are put again after this (owner 2026-11-14). */
        val REUPLOAD_EVERY: Duration = Duration.ofDays(25)

        /** A transport is listed and worked through at most this often (a file is still written at most daily). */
        val RUN_EVERY: Duration = Duration.ofMinutes(15)

        /** A last guard: no new file when fewer than this many places of the relay's 64 would stay free (the quota is the real limit). */
        const val RELAY_HEADROOM = 4

        private val ROLL_EVERY: Duration = Duration.ofDays(1)
        private val RETRY_UNREADABLE_AFTER: Duration = Duration.ofDays(1)
        private val ZERO = UUID(0L, 0L)
    }
}
