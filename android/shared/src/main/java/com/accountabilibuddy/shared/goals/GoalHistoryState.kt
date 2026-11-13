package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.SyncGoalYearMerged
import java.time.OffsetDateTime
import java.util.UUID

/**
 * What a device keeps of the goal history files on one transport (Drive or the relay; the PC's `GoalHistoryTransportState`). Device-local,
 * sealed at rest with the rest of [GoalHistoryState], never in a backup or a sync file.
 */
class GoalHistoryTransportState {
    /** The files read, by key text ([GoalHistoryNames.keyText]), at the version listed when they were read. */
    val read = LinkedHashMap<String, OffsetDateTime>()

    /** The content tag of each read file, where the store gives one: an unchanged tag is not downloaded again. */
    val readTag = LinkedHashMap<String, String>()

    /** This device's own month files as last written, by [GoalHistoryNames.monthText]: what "the content changed" compares and what is put again. */
    val written = LinkedHashMap<String, GoalHistoryMonth>()

    /** When each own file (by name) was last put: the relay's 25-day re-upload. */
    val uploaded = LinkedHashMap<String, OffsetDateTime>()

    /** The own month files that rolled up and are no longer kept (deleted on Drive, not put again on the relay), by month text. */
    val removed = LinkedHashSet<String>()

    /** The year files seen (read, or rolled here), without their rows: the pieces and the version, by key text. */
    val years = LinkedHashMap<String, GoalHistoryYear>()

    /** The names of the year files this device rolled (put again every 25 days on the relay). */
    val ownYearNames = LinkedHashSet<String>()

    /** Every name this device ever put here: they are deleted when it leaves the group. */
    val ownNames = LinkedHashSet<String>()

    /** When each year was last checked for a roll-up (at most daily). */
    val rollChecked = LinkedHashMap<Int, OffsetDateTime>()
}

/**
 * What a device keeps for the shared goal history (ADR 79, GS3b/GS4; the PC's `GoalHistoryState`), in one device-local sealed document:
 * when each device's regular file last merged here, the "merged year files" markers, this device's own marker, the kept set of pruned goal
 * ids (Q649) and, per transport, the files read and written. Reset when the group's key changes.
 */
class GoalHistoryState {
    /** The key fingerprint this state belongs to; a different key resets it. */
    var group: String = ""

    /** A regular file's goals part has merged here: until then this device is new to the group and reads every history file. */
    var goalsPartMerged = false

    val lastRegularMerge = LinkedHashMap<UUID, OffsetDateTime>()

    /** Each device's last complete merge here: its regular file, or a full read of its history files. */
    val lastMergeFrom = LinkedHashMap<UUID, OffsetDateTime>()

    val deviceMarkers = LinkedHashMap<UUID, List<SyncGoalYearMerged>>()

    /** This device's own marker: the year files it merged, newest version of each. */
    var ownMarker: List<SyncGoalYearMerged> = emptyList()

    /** The goal ids held as tombstones when last looked at: one that is gone later was pruned. */
    var tombstonedGoals: Set<UUID> = emptySet()

    /** The ids of goals whose tombstone this device pruned, kept for good: their rows in history files never keep a file unread. */
    val prunedGoals = LinkedHashSet<UUID>()

    val transports = LinkedHashMap<String, GoalHistoryTransportState>()

    /**
     * Names the relay's regular reader found to be history files by their header (a device whose name wasn't known yet): kept so a restart
     * doesn't download them again. Opaque 12-hex-character names; at most [MAX_HISTORY_NAMES], oldest dropped.
     */
    val historyNames = LinkedHashSet<String>()

    fun transport(name: String): GoalHistoryTransportState = transports.getOrPut(name) { GoalHistoryTransportState() }
}

const val MAX_HISTORY_NAMES = 256

/** Where the state is kept between runs (the phone: a sealed file); [value] is the live state. */
interface GoalHistoryStateHolder {
    val value: GoalHistoryState

    /** Forgets everything (the group's key changed) and returns the empty state. */
    fun reset()

    /** Keeps [value]; a failure is not an error (only a read position is lost, never a goal). */
    fun save()
}
