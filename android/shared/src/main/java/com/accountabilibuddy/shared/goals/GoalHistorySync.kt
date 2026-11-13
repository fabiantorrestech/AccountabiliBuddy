package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.SyncGoalYearMerged
import java.time.Duration
import java.time.OffsetDateTime
import java.util.TreeSet
import java.util.UUID

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
 * A file bigger than the store's limit (the relay's 4 MiB) is not written, and the relay keeps room for the devices' sync files. Sealed exactly
 * like the sync files (the group's key, AES-256-GCM, [codec]); never in a QR file. Logs counts only. Pure: no clock, no network, no storage
 * of its own, so the JVM checks drive it with a fake store and codec. Passes must not overlap (the caller holds its sync mutex).
 */
class GoalHistorySync(
    /** This device's lists as the sync file carries them (Deep work composed), or null while the goals aren't ready. */
    private val held: () -> GoalLists?,
    /** Takes a merge in INSIDE the store's lock: gets the held lists, returns the merged ones (null = nothing to take in); true when anything changed. */
    private val applySynced: ((GoalLists) -> GoalLists?) -> Boolean,
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

    private val lastRun = HashMap<String, OffsetDateTime>()
    private val unreadableTried = HashMap<String, OffsetDateTime>()
    private val staleNames = HashMap<String, List<String>>()
    private var previousGoals: GoalLists? = null

    /** Raised when this device's "merged year files" marker changed: its regular sync file is written again. */
    var onMarkerChanged: () -> Unit = {}

    init {
        previousGoals = if (ready()) held() else null
    }

    /** The year files this device has merged, for its regular sync file (`goalYearsMerged`); null for none. */
    val marker: List<SyncGoalYearMerged>? get() = states.value.ownMarker.ifEmpty { null }

    /** The ids of the goals whose tombstone this device pruned (Q649), kept for good. */
    val prunedGoals: Set<UUID> get() = states.value.prunedGoals

    /** Every name this device put on the transport: they go when it leaves the group (the relay) or when the key changes. */
    fun ownNames(transport: String): List<String> = states.value.transports[transport]?.ownNames?.toList() ?: emptyList()

    /**
     * A regular sync file with goal parts merged here: the device is syncing (its last complete merge and its last regular merge are now),
     * this device is no longer new to the group, and the device's "merged year files" marker is kept (the copy of its file kept for display has none).
     */
    fun noteRegularFile(deviceId: UUID, yearsMerged: List<SyncGoalYearMerged>?) {
        val k = key() ?: return
        val state = stateFor(k)
        val at = now()
        state.goalsPartMerged = true
        state.lastRegularMerge[deviceId] = at
        state.lastMergeFrom[deviceId] = at
        if (yearsMerged != null) state.deviceMarkers[deviceId] = yearsMerged else state.deviceMarkers.remove(deviceId)
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
        val me = self()
        val k = key()
        if (!enabled() || me == null || me == ZERO || k == null || !ready()) return run

        run.ran = true
        lastRun[store.transport] = now()
        val state = stateFor(k)
        val markerText = markerText(state)
        try {
            deleteStaleNames(store)
            val pass = Pass(store, state, state.transport(store.transport), me, k)
            list(pass)
            read(pass, run)
            if (roll(pass, run)) {
                list(pass)
                read(pass, run)
            }

            write(pass, run)
            remove(pass, run)
            reuploadYears(pass, run)
        } finally {
            states.save()
            if (markerText(state) != markerText) onMarkerChanged()
            log("goal history ${store.transport}: $run")
        }

        return run
    }

    /** Goals whose tombstone went since the last look (Q649): kept, with the id only. Call after every change of the lists. */
    fun trackPrunedGoals() {
        if (!ready()) return
        val after = held() ?: return
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
        }
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
        val ids = HashSet<UUID>()
        ids.add(pass.me)
        ids.addAll(devices().map { it.id })
        ids.addAll(pass.state.deviceMarkers.keys)
        ids.addAll(pass.state.lastRegularMerge.keys)
        ids.addAll(pass.state.lastMergeFrom.keys)
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

        val newToGroup = !state.goalsPartMerged
        val toRead = GoalHistoryRules.toRead(pass.history, lastRead, lastMerge, removed, pass.me, newToGroup, at)
        val staleBefore = pass.history
            .filter { it.key.kind == GoalHistoryFileKind.Month && it.key.deviceId != null && it.key.deviceId != pass.me && it.key.deviceId !in removed }
            .map { it.key.deviceId!! }
            .filter { device -> lastMerge[device]?.let { Duration.between(it.toInstant(), at.toInstant()) > GoalHistoryRules.FULL_READ_AFTER } ?: true }
            .toSet()

        val rows = ArrayList<GoalHistoryRows>()
        val years = HashMap<GoalHistoryFileKey, GoalHistoryYear>()
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
            if (remote.size > pass.store.maxFileBytes || (tried != null && Duration.between(tried.toInstant(), at.toInstant()) < RETRY_UNREADABLE_AFTER)) continue
            val bytes = pass.store.get(remote.name) ?: continue
            if (file.key.kind == GoalHistoryFileKind.Month) {
                val month = codec.openMonth(bytes)
                if (month == null || month.key != file.key) {
                    noteUnreadable(remote, run, at)
                    continue
                }

                rows.add(GoalHistoryRows(file, month.entries))
            } else {
                val year = codec.openYear(bytes)
                if (year == null || year.key != file.key) {
                    noteUnreadable(remote, run, at)
                    continue
                }

                rows.add(GoalHistoryRows(file, year.entries))
                years[file.key] = year
            }
        }

        if (rows.isNotEmpty()) {
            var merged: GoalHistoryMerged? = null
            applySynced { heldLists ->
                val result = GoalHistoryRules.merge(heldLists, null, rows, at, state.prunedGoals)
                merged = result
                result.lists
            }
            merged?.let { result ->
                for (readFile in result.read) {
                    val text = GoalHistoryNames.keyText(readFile.key)
                    transport.read[text] = readFile.modifiedAt
                    pass.remote[readFile.key]?.tag?.let { transport.readTag[text] = it }
                    run.read++
                    years[readFile.key]?.let { year ->
                        state.ownMarker = GoalHistoryRules.marked(state.ownMarker, year)
                        keepYear(transport, year)
                    }
                }

                trackPrunedGoals()
            }
        }

        // A stale device whose files are all read now has a complete merge: it is not stale again until 90 days pass.
        for (device in staleBefore) {
            val complete = pass.history
                .filter { it.key.kind == GoalHistoryFileKind.Month && it.key.deviceId == device }
                .all { file -> transport.read[GoalHistoryNames.keyText(file.key)]?.let { DotNetTime.same(it, file.modifiedAt) } == true }
            if (complete) state.lastMergeFrom[device] = at
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

            var existing: GoalHistoryYear? = null
            val yearFile = pass.history
                .filter { it.key.kind == GoalHistoryFileKind.Year && it.key.year == year }
                .sortedWith(compareByDescending<GoalHistoryListing> { it.key.deviceId == pass.me }.thenByDescending { DotNetTime.ticks(it.modifiedAt) })
                .firstOrNull()
            if (yearFile != null) {
                val remote = pass.remote.getValue(yearFile.key)
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
                val bytes = if (remote.size > pass.store.maxFileBytes) null else pass.store.get(remote.name)
                val month = bytes?.let { codec.openMonth(it) }
                if (month != null && month.key == file.key) months.add(month)
            }

            pass.transport.rollChecked[year] = at
            val rolledYear = GoalHistoryRules.rollUp(existing, months, year, pass.me, pass.history, at) ?: continue
            val sealedYear = codec.seal(rolledYear)
            val name = GoalHistoryNames.nameFor(pass.key.groupId, rolledYear.key, pass.store.extension)
            if (!fits(pass, run, sealedYear.size, name)) continue

            pass.store.put(name, sealedYear)
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
            var merged: OffsetDateTime? = pass.state.lastRegularMerge[info.id]
            val received = info.receivedAt
            if (received != null && (merged == null || DotNetTime.compare(received, merged) > 0)) merged = received
            group.add(GoalGroupDevice(info.id, info.isPc, info.removed, merged, pass.state.deviceMarkers[info.id]))
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
        val months = TreeSet<Pair<Int, Int>>(compareBy({ it.first }, { it.second }))
        months.addAll(GoalHistoryRules.monthsOf(lists))
        for (text in transport.written.keys) {
            val parts = text.split('-')
            val year = parts.getOrNull(0)?.toIntOrNull()
            val month = parts.getOrNull(1)?.toIntOrNull()
            if (parts.size == 2 && year != null && month != null) months.add(year to month)
        }

        for ((year, month) in months) {
            val monthText = GoalHistoryNames.monthText(year, month)
            val last = transport.written[monthText]
            val fresh = GoalHistoryRules.monthToWrite(last, lists, pass.me, year, month, at)
            var toPut = fresh
            val name = GoalHistoryNames.nameFor(pass.key.groupId, GoalHistoryFileKey.forMonth(pass.me, year, month), pass.store.extension)
            val listed = pass.remote.values.any { it.name == name }
            var reupload = false
            if (toPut == null && last != null && monthText !in transport.removed) {
                // The same content: put again only when the file is missing here, or the store would expire it.
                val missing = !listed
                val uploaded = transport.uploaded[name]
                val aging = pass.store.expires && (uploaded == null || Duration.between(uploaded.toInstant(), at.toInstant()) >= REUPLOAD_EVERY)
                if (missing || aging) {
                    toPut = last
                    reupload = true
                }
            }

            if (toPut == null) continue
            val sealedMonth = codec.seal(toPut)
            if (!fits(pass, run, sealedMonth.size, name, listed)) continue

            pass.store.put(name, sealedMonth)
            if (!listed) pass.newFiles++
            transport.written[monthText] = toPut
            transport.uploaded[name] = at
            transport.ownNames.add(name)
            transport.removed.remove(monthText)
            if (reupload) run.reuploaded++ else run.written++
        }
    }

    // The store's limits: a file over its size is not written; the relay's group keeps room for the devices' sync files.
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

            val bytes = pass.store.get(name)
            if (bytes == null) {
                // Gone (expired): the next roll-up writes it again if its month files are still here.
                pass.transport.ownYearNames.remove(name)
                pass.transport.ownNames.remove(name)
                pass.transport.uploaded.remove(name)
                pass.transport.rollChecked.clear()
                continue
            }

            pass.store.put(name, bytes)
            pass.transport.uploaded[name] = at
            run.reuploaded++
        }
    }

    private class Pass(val store: GoalHistoryStore, val state: GoalHistoryState, val transport: GoalHistoryTransportState, val me: UUID, val key: GoalHistoryKey) {
        /** The history files the listing holds, by key. */
        val remote = HashMap<GoalHistoryFileKey, GoalHistoryRemoteFile>()
        val history = ArrayList<GoalHistoryListing>()
        var totalFiles = 0

        /** Files this pass added to the store (they count toward its file limit). */
        var newFiles = 0
    }

    companion object {
        /** The relay deletes files untouched for 30 days (ADR 46); own files are put again after this (owner 2026-11-14). */
        val REUPLOAD_EVERY: Duration = Duration.ofDays(25)

        /** A transport is listed and worked through at most this often (a file is still written at most daily). */
        val RUN_EVERY: Duration = Duration.ofMinutes(15)

        /** The relay's file limit leaves this many places for the devices' sync files. */
        const val RELAY_HEADROOM = 8

        private val ROLL_EVERY: Duration = Duration.ofDays(1)
        private val RETRY_UNREADABLE_AFTER: Duration = Duration.ofDays(1)
        private val ZERO = UUID(0L, 0L)
    }
}
