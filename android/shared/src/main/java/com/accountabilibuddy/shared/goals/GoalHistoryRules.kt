package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncGoalYearMerged
import java.time.Duration
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID
import kotlinx.serialization.json.JsonElement

/**
 * The shared goal history's rules (ADR 79 "shared history"; the PC's `GoalHistoryRules`, rule for rule). Pure, no transport. Months and
 * years are UTC, of an entry's stamp (edit time, or deletion time when later).
 * - **Month files**: a device writes its file for any month whose content differs from the version it last wrote ([monthToWrite]), at most
 *   once a day per file; the first run writes every month it holds ([monthsOf]); a month that has ended gets one closing write.
 * - **Reading** ([toRead]): files new or changed since this device last read them; every file when it is new to the group; every file of a
 *   device (and every year file) whose last complete merge here is older than 90 days; a removed device's files only when they changed.
 * - **Active devices** ([activeDevices]): this device, and every device not removed whose regular file was merged within 90 days.
 * - **Year files** ([rollUp]): from 1 February UTC the active PC with the lowest id ([roller]) rolls every month file of the ended year; a
 *   later roll-up only adds.
 * - **Removing a month file** ([mayRemoveMonth]): only once the year file holds that version and every active device merged that year file.
 * - **Merging** ([merge]): the regular file's goal parts first, then the history rows; rows of a goal the device does not hold are dropped and
 *   their file is not recorded as read.
 */
object GoalHistoryRules {
    val REWRITE_EVERY: Duration = Duration.ofDays(1)
    val FULL_READ_AFTER: Duration = GoalSyncWindow.WINDOW
    val ACTIVE_WITHIN: Duration = GoalSyncWindow.WINDOW

    /** The relay keeps at most this many files a group (ADR 46). */
    const val RELAY_FILE_LIMIT = 64

    /** Places of the relay's 64 that history files never plan on: the devices' sync files and some slack ([relayQuota]). */
    const val RELAY_SYNC_RESERVE = 16

    /** The first PC app version that reads and skips goal history files (the PC's `FirstPcVersionWithHistory`). */
    const val FIRST_PC_VERSION_WITH_HISTORY = "0.1.134"

    /** The first phone app version that reads and skips goal history files (the PC's `FirstPhoneVersionWithHistory`). */
    const val FIRST_PHONE_VERSION_WITH_HISTORY = "0.1.78"

    /** Rows of an unknown goal count as pruned only when all are older than this (the tombstone retention, [latePrunable]). */
    val LATE_PRUNE_AGE: Duration = Duration.ofDays(30)

    /** The UTC year and month of the entry's stamp: the month file it goes to. */
    fun monthOf(entry: GoalEntry): Pair<Int, Int> = utcMonth(entry.stamp)

    /** The open months: the current UTC month and the one before. */
    fun openMonths(now: OffsetDateTime): List<Pair<Int, Int>> {
        val current = now.withOffsetSameInstant(ZoneOffset.UTC)
        val previous = current.withDayOfMonth(1).minusMonths(1)
        return listOf(previous.year to previous.monthValue, current.year to current.monthValue)
    }

    fun isOpen(year: Int, month: Int, now: OffsetDateTime): Boolean = (year to month) in openMonths(now)

    /** Every month [held] has an entry in (newer items whose stamps read too), oldest first. */
    fun monthsOf(held: GoalLists): List<Pair<Int, Int>> =
        (held.entries.map(::monthOf) + held.newer.entries.mapNotNull { GoalRawItem.tryRead(it.json) }.map(::stampMonth))
            .distinct()
            .sortedWith(compareBy({ it.first }, { it.second }))

    /** What the month file holds: every entry whose stamp is in that month ordered by id text, newer items after the known ones. */
    fun monthContent(held: GoalLists, year: Int, month: Int): GoalLists {
        val known = GoalListMerge.sortedByText(held.entries.filter { monthOf(it) == (year to month) }) { it.id.toString() }
        val newer = GoalListMerge.sortedByText(
            held.newer.entries
                .map { it to GoalRawItem.tryRead(it.json) }
                .filter { (_, raw) -> raw != null && stampMonth(raw) == (year to month) },
        ) { (_, raw) -> raw!!.id.toString() }
            .mapIndexed { order, (item, _) -> GoalNewerItem(known.size + order, item.json) }
        return GoalLists(entries = known, newer = GoalNewerLists(entries = newer))
    }

    /** The month file to write now, or null when there is nothing to write. */
    fun monthToWrite(last: GoalHistoryMonth?, held: GoalLists, deviceId: UUID, year: Int, month: Int, now: OffsetDateTime): GoalHistoryMonth? {
        val content = monthContent(held, year, month)
        if (last == null) {
            return if (content.entries.isEmpty() && content.newer.entries.isEmpty()) null
            else GoalHistoryMonth(GoalHistoryMonth.CURRENT_FORMAT_VERSION, deviceId, year, month, now, content)
        }

        if (rowsText(content) == rowsText(last.entries)) return null
        val monthEnd = monthStart(year, month).plusMonths(1)
        val closing = DotNetTime.compare(now, monthEnd) >= 0 && DotNetTime.compare(last.writtenAt, monthEnd) < 0
        if (!closing && ticksBetween(last.writtenAt, now) < REWRITE_EVERY.toNanos() / 100) return null
        val writtenAt = if (DotNetTime.compare(now, last.writtenAt) > 0) now else DotNetTime.plusTick(last.writtenAt)
        return GoalHistoryMonth(GoalHistoryMonth.CURRENT_FORMAT_VERSION, deviceId, year, month, writtenAt, content)
    }

    /** The listed files to read now, year files first, then month files (by year, month, device id text). */
    fun toRead(
        listing: List<GoalHistoryListing>,
        lastRead: Map<GoalHistoryFileKey, OffsetDateTime>,
        lastMergeFrom: Map<UUID, OffsetDateTime>,
        removed: Set<UUID>,
        self: UUID,
        newToGroup: Boolean,
        now: OffsetDateTime,
    ): List<GoalHistoryListing> {
        fun stale(device: UUID): Boolean {
            val merged = lastMergeFrom[device]
            return device !in removed && (merged == null || ticksBetween(merged, now) > FULL_READ_AFTER.toNanos() / 100)
        }

        fun changed(file: GoalHistoryListing): Boolean {
            val read = lastRead[file.key]
            return read == null || !DotNetTime.same(read, file.modifiedAt)
        }

        val anyStale = (
            listing.filter { it.key.kind == GoalHistoryFileKind.Month && it.key.deviceId != null && it.key.deviceId != self }.map { it.key.deviceId!! } +
                lastMergeFrom.keys.filter { it != self }
            ).distinct().any(::stale)
        return listing
            .filter { file ->
                newToGroup || when (file.key.kind) {
                    GoalHistoryFileKind.Year -> anyStale || changed(file)
                    GoalHistoryFileKind.Month -> file.key.deviceId.let { device -> device != null && device != self && (stale(device) || changed(file)) }
                }
            }
            .sortedWith(
                compareBy<GoalHistoryListing> { if (it.key.kind == GoalHistoryFileKind.Year) 0 else 1 }
                    .thenBy { it.key.year }
                    .thenBy { it.key.month ?: 0 }
                    .thenBy { it.key.deviceId?.toString() ?: "" },
            )
    }

    /** The active devices: this device unless removed, and every device not removed whose regular file was merged within 90 days; by id text. */
    fun activeDevices(group: List<GoalGroupDevice>, self: UUID, now: OffsetDateTime): List<GoalHistoryDevice> =
        group
            .filter { device ->
                !device.removed && (device.id == self || (device.lastRegularMerge != null && ticksBetween(device.lastRegularMerge, now) <= ACTIVE_WITHIN.toNanos() / 100))
            }
            .groupBy { it.id }.values.map { it.first() }
            .sortedBy { it.id.toString() }
            .map { GoalHistoryDevice(it.id, it.isPc, it.yearsMerged) }

    /** [lastMergeFrom] without the devices removed from the group. */
    fun forgetRemoved(lastMergeFrom: Map<UUID, OffsetDateTime>, removed: Iterable<UUID>): Map<UUID, OffsetDateTime> {
        val gone = removed.toSet()
        return lastMergeFrom.filterKeys { it !in gone }
    }

    /** The PC that rolls the year files: the active PC whose id (text, ordinal) is the lowest; null with no PC. */
    fun roller(activeDevices: List<GoalHistoryDevice>): UUID? = activeDevices.filter { it.isPc }.minByOrNull { it.id.toString() }?.id

    /** Whether the year may be rolled: from 1 February UTC of the next year. */
    fun rollDue(year: Int, now: OffsetDateTime): Boolean =
        DotNetTime.compare(now, OffsetDateTime.of(year + 1, 2, 1, 0, 0, 0, 0, ZoneOffset.UTC)) >= 0

    /** The year file to write ([existing] plus every month file of [year] it does not hold yet), or null when nothing is due or new. */
    fun rollUp(
        existing: GoalHistoryYear?, months: List<GoalHistoryMonth>, year: Int, rolledBy: UUID, listing: List<GoalHistoryListing>, now: OffsetDateTime,
    ): GoalHistoryYear? {
        if (!rollDue(year, now) || (existing != null && existing.year != year)) return null
        if (existing == null && listing.any { it.key.kind == GoalHistoryFileKind.Year && it.key.year == year }) return null
        val ofYear = months.filter { it.year == year }
        val held = existing?.months ?: emptyList()
        val fresh = ofYear.filter { month ->
            held.none { it.deviceId == month.deviceId && it.month == month.month && DotNetTime.compare(it.writtenAt, month.writtenAt) >= 0 }
        }
        if (fresh.isEmpty()) return null
        val pieces = (held + fresh.map { GoalHistoryPiece(it.deviceId, it.month, it.writtenAt) })
            .groupBy { it.deviceId to it.month }.values
            .map { group -> group.sortedByDescending { DotNetTime.ticks(it.writtenAt) }.first() }
            .sortedWith(compareBy<GoalHistoryPiece> { it.deviceId.toString() }.thenBy { it.month })
        val entries = mergeEntries(listOf(existing?.entries ?: GoalLists.EMPTY) + fresh.map { it.entries }, now)
        val rolledAt = if (existing == null || DotNetTime.compare(now, existing.rolledAt) > 0) now else DotNetTime.plusTick(existing.rolledAt)
        return GoalHistoryYear(GoalHistoryYear.CURRENT_FORMAT_VERSION, year, rolledBy, rolledAt, pieces, entries)
    }

    /** Whether a month file may be removed: not open, held by the year file, and every active device merged that year file at that version. */
    fun mayRemoveMonth(
        monthKey: GoalHistoryFileKey, monthWrittenAt: OffsetDateTime, year: GoalHistoryYear?, activeDevices: List<GoalHistoryDevice>, now: OffsetDateTime,
    ): Boolean {
        val device = monthKey.deviceId
        val month = monthKey.month
        if (monthKey.kind != GoalHistoryFileKind.Month || device == null || month == null || year == null || year.year != monthKey.year ||
            isOpen(monthKey.year, month, now) || activeDevices.isEmpty()
        ) {
            return false
        }

        val held = year.months.any { it.deviceId == device && it.month == month && DotNetTime.compare(it.writtenAt, monthWrittenAt) >= 0 }
        return held && activeDevices.all { active ->
            active.yearsMerged?.any { it.year == year.year && it.rolledBy == year.rolledBy && DotNetTime.compare(it.rolledAt, year.rolledAt) >= 0 } == true
        }
    }

    /** This device's marker after it merged [merged]: each year file's newest version, by year then rolling PC id text; an item without a rolling PC is dropped. */
    fun marked(marker: List<SyncGoalYearMerged>?, merged: GoalHistoryYear): List<SyncGoalYearMerged> =
        ((marker ?: emptyList()).filter { it.rolledBy != UUID(0L, 0L) } + SyncGoalYearMerged(merged.year, merged.rolledBy, merged.rolledAt))
            .groupBy { it.year to it.rolledBy }.values
            .map { group -> group.sortedByDescending { DotNetTime.ticks(it.rolledAt) }.first() }
            .sortedWith(compareBy<SyncGoalYearMerged> { it.year }.thenBy { it.rolledBy.toString() })

    /** History rows merged with each other: union by id, newest wins, tombstones kept for good, newer items by id and stamps; ordered by id. */
    fun mergeEntries(lists: List<GoalLists>, now: OffsetDateTime): GoalLists {
        val items = ArrayList<GoalMergeItem>()
        val carried = ArrayList<JsonElement>()
        for (list in lists) {
            items.addAll(GoalSyncClean.entries(list.entries).map(GoalTombstones::clean))
            for (item in list.newer.entries) {
                val raw = GoalRawItem.tryRead(item.json)
                if (raw != null) items.add(raw) else carried.add(item.json)
            }
        }

        val merged = GoalListMerge.sortedByText(GoalListMerge.merge(items, emptyList(), now, keepTombstones = true)) { it.id.toString() }
        val known = merged.filterIsInstance<GoalEntry>()
        val newer = (merged.filterIsInstance<GoalRawItem>().map { it.json } +
            carried.groupBy { GoalJson.text(it) }.entries.sortedBy { it.key }.map { it.value.first() })
            .mapIndexed { order, json -> GoalNewerItem(known.size + order, json as kotlinx.serialization.json.JsonObject) }
        return GoalLists(entries = known, newer = GoalNewerLists(entries = newer))
    }

    /** [held] after a regular file's goal parts ([file], null for none) and then the history files' rows. */
    fun merge(held: GoalLists, file: GoalLists?, history: List<GoalHistoryRows>, now: OffsetDateTime): GoalHistoryMerged =
        merge(held, file, history, now, emptySet())

    /** As [merge], with Q649's rule: rows of a goal whose tombstone this device pruned never keep a file unread. */
    fun merge(held: GoalLists, file: GoalLists?, history: List<GoalHistoryRows>, now: OffsetDateTime, prunedGoals: Set<UUID>): GoalHistoryMerged =
        merge(held, file, history, now, prunedGoals, null)

    /**
     * As [merge] with the late joiner's rule ([latePrunable], review blocker 2): goals it frees are treated as pruned for the "read" decision
     * and returned in [GoalHistoryMerged.newlyPruned] (the device adds them to its kept set of pruned goal ids). [late] null = off.
     */
    fun merge(
        held: GoalLists, file: GoalLists?, history: List<GoalHistoryRows>, now: OffsetDateTime, prunedGoals: Set<UUID>, late: GoalHistoryLatePrune?,
    ): GoalHistoryMerged {
        val result = if (file == null) held else GoalSyncMerge.merge(held, file, now)
        val goalsPart = file != null
        if (history.isEmpty()) return GoalHistoryMerged(result, emptyList(), emptyList(), goalsPart)
        val merged = GoalSyncMerge.mergeReported(result, mergeEntries(history.map { it.entries }, now), now)
        val newlyPruned =
            if (late == null) emptyList()
            else latePrunable(merged.droppedEntries, late.firstDropAt, late.active, late.self, late.lastRegularMerge, prunedGoals, now)
        val droppedIds = merged.droppedEntries.filter { it.goalId !in prunedGoals && it.goalId !in newlyPruned }.map { it.id }.toHashSet()
        val read = history
            .filter { item -> item.entries.entries.none { it.id in droppedIds } }
            .map { it.file }
            // The PC's record equality compares a DateTimeOffset by its instant, not by its offset.
            .distinctBy { it.key to DotNetTime.ticks(it.modifiedAt) }
        return GoalHistoryMerged(merged.lists, read, merged.droppedEntries, goalsPart, newlyPruned)
    }

    /** Q649: the goals whose tombstone went between [before] and [after] (pruned), ordered by id text. */
    fun prunedGoalIds(before: GoalLists, after: GoalLists): List<UUID> {
        val stillHeld = after.goals.map { it.id }.toHashSet()
        after.newer.goals.mapNotNull { GoalRawItem.tryRead(it.json) }.forEach { stillHeld.add(it.id) }
        return before.goals.filter { !it.isLive && it.id !in stillHeld }.map { it.id }.distinct().sortedBy { it.toString() }
    }

    /** Whether this device is still new to the group after [merged]: it was, and no regular file's goals part has merged yet. */
    fun stillNewToGroup(newToGroup: Boolean, merged: GoalHistoryMerged): Boolean = newToGroup && !merged.goalsPartMerged

    /** The rows' JSON text: what "the content changed" compares. */
    fun rowsText(entries: GoalLists): String = GoalJson.text(GoalSyncJson.entriesJson(entries))

    /**
     * ON THE RELAY ONLY (the PC's `RelayQuota`): the most month files one device keeps there, so that history files never pass 64 - 16 = 48
     * places together with the year files: `floor((64 - 16 - yearFiles) / knownDevices)`, never below 0. [yearFiles] is how many year files
     * the listing holds; [knownDevices] the devices not removed from the group that this device knows, itself included (at least 1).
     */
    fun relayQuota(yearFiles: Int, knownDevices: Int): Int =
        maxOf(0, (RELAY_FILE_LIMIT - RELAY_SYNC_RESERVE - maxOf(0, yearFiles)) / maxOf(1, knownDevices))

    /**
     * ON THE RELAY ONLY (the PC's `RelayKeep`): the months of this device's own history that stay on the relay, newest first: only the PC that
     * rolls the year ([isRoller]) keeps finished months; every other device keeps its [openMonths] only; then at most [quota] of them, newest
     * first. A month not returned is not put again (its file expires). Drive keeps every month.
     */
    fun relayKeep(months: Iterable<Pair<Int, Int>>, isRoller: Boolean, quota: Int, now: OffsetDateTime): List<Pair<Int, Int>> =
        months.distinct()
            .filter { isRoller || isOpen(it.first, it.second, now) }
            .sortedWith(compareByDescending<Pair<Int, Int>> { it.first }.thenByDescending { it.second })
            .take(maxOf(0, quota))

    /**
     * Whether an app of this kind ("pc", "phone", "watch") at this version (a leading dotted number; anything after it is ignored) reads and
     * skips goal history files (the PC's `KnowsHistory`): pc from [FIRST_PC_VERSION_WITH_HISTORY], phone from [FIRST_PHONE_VERSION_WITH_HISTORY];
     * a watch never syncs through the relay (true); an unreadable version or an unknown kind is false. ON THE RELAY a device writes history
     * files only while this is true for every device of the group that is not removed; Drive is unaffected.
     */
    fun knowsHistory(kind: String?, appVersion: String?): Boolean {
        if (kind == SyncDeviceCard.KIND_WATCH) return true
        val first = when (kind) {
            SyncDeviceCard.KIND_PC -> FIRST_PC_VERSION_WITH_HISTORY
            SyncDeviceCard.KIND_PHONE -> FIRST_PHONE_VERSION_WITH_HISTORY
            else -> return false
        }
        if (appVersion == null) return false
        val version = dotNetVersion(appVersion.takeWhile { it in '0'..'9' || it == '.' }.trimEnd('.')) ?: return false
        return compareVersions(version, dotNetVersion(first)!!) >= 0
    }

    // .NET's Version.TryParse: two to four non-empty groups of digits, each an Int32; a missing part counts as -1 (so "0.1" is below "0.1.0").
    private fun dotNetVersion(text: String): List<Int>? {
        val parts = text.split('.')
        if (parts.size !in 2..4 || parts.any { it.isEmpty() }) return null
        val numbers = parts.map { it.toIntOrNull() ?: return null }
        return numbers + List(4 - numbers.size) { -1 }
    }

    private fun compareVersions(a: List<Int>, b: List<Int>): Int {
        for (i in 0 until 4) {
            if (a[i] != b[i]) return a[i].compareTo(b[i])
        }

        return 0
    }

    /**
     * Late joiner (the PC's `LatePrunable`): the ids of the goals whose rows in [dropped] (rows no history file could land because the goal
     * isn't held) can be taken as pruned for good. Only when a first drop was seen ([firstDropAt]) AND every active device other than [self]
     * has had its regular sync file merged here strictly after it ([lastRegularMerge]); of those, only a goal ALL of whose dropped rows are
     * older than [LATE_PRUNE_AGE] (by the entry's stamp) and which isn't in [alreadyPruned]. Ordered by id text.
     */
    fun latePrunable(
        dropped: List<GoalEntry>,
        firstDropAt: OffsetDateTime?,
        active: List<GoalHistoryDevice>,
        self: UUID,
        lastRegularMerge: Map<UUID, OffsetDateTime>,
        alreadyPruned: Set<UUID>,
        now: OffsetDateTime,
    ): List<UUID> {
        if (firstDropAt == null) return emptyList()
        val everyoneMerged = active.filter { it.id != self }.all { device ->
            lastRegularMerge[device.id]?.let { DotNetTime.compare(it, firstDropAt) > 0 } == true
        }
        if (!everyoneMerged) return emptyList()
        val ageLimit = LATE_PRUNE_AGE.toNanos() / 100
        return dropped
            .filter { it.goalId !in alreadyPruned }
            .groupBy { it.goalId }
            .filter { (_, rows) -> rows.all { DotNetTime.ticks(now) - DotNetTime.ticks(it.stamp) > ageLimit } }
            .keys
            .sortedBy { it.toString() }
    }

    /**
     * Whether every listed file is accounted for (the PC's `AllAccountedFor`): recorded as read at its listed version in [lastRead], or in
     * [skipped] (too big or unreadable: logged and left out, they never hold anything back).
     */
    fun allAccountedFor(listing: List<GoalHistoryListing>, lastRead: Map<GoalHistoryFileKey, OffsetDateTime>, skipped: Set<GoalHistoryFileKey>): Boolean =
        listing.all { file -> file.key in skipped || lastRead[file.key]?.let { DotNetTime.same(it, file.modifiedAt) } == true }

    private fun monthStart(year: Int, month: Int): OffsetDateTime = OffsetDateTime.of(year, month, 1, 0, 0, 0, 0, ZoneOffset.UTC)

    private fun utcMonth(time: OffsetDateTime): Pair<Int, Int> = time.withOffsetSameInstant(ZoneOffset.UTC).let { it.year to it.monthValue }

    private fun stampMonth(raw: GoalRawItem): Pair<Int, Int> =
        utcMonth(if (raw.deletedAt != null && DotNetTime.compare(raw.deletedAt, raw.editedAt) > 0) raw.deletedAt else raw.editedAt)

    // Ticks from [from] to [to].
    private fun ticksBetween(from: OffsetDateTime, to: OffsetDateTime): Long = DotNetTime.ticks(to) - DotNetTime.ticks(from)
}
