package com.accountabilibuddy.shared.goals

import java.time.OffsetDateTime

/** A file in a goal history transport's listing (the PC's `GoalHistoryRemoteFile`): the store's version of it, its size and a content tag when it gives one. */
data class GoalHistoryRemoteFile(val name: String, val modifiedAt: OffsetDateTime, val size: Long, val tag: String?)

/**
 * Where the goal history files (month and year files, ADR 79 shared history) are kept: the app's Drive folder or the user's own relay
 * (the PC's `IGoalHistoryStore`). The rules and the order of a pass are the same for both ([GoalHistorySync]); only the limits differ.
 */
interface GoalHistoryStore {
    /** "drive" or "relay": the key of this transport's state. */
    val transport: String

    /** The file name's extension, with the dot. */
    val extension: String

    /** The biggest file the store takes (the relay refuses over 4 MiB, ADR 46). */
    val maxFileBytes: Long

    /** The most files one group can hold (the relay's 64); null for no limit. */
    val maxFiles: Int?

    /** True when the store deletes files nobody touched for 30 days (the relay): own files are put again every 25 days. */
    val expires: Boolean

    /** True when this device may delete its own history files here (Drive). The relay deletes only when a device leaves its group. */
    val canDelete: Boolean

    /** Every file of the group's folder or relay group (not only history files). */
    fun list(): List<GoalHistoryRemoteFile>

    /** The file's bytes; null when it is gone. */
    fun get(name: String): ByteArray?

    fun put(name: String, bytes: ByteArray)

    /** Deletes the file; true when it was deleted or already gone, false when this store never deletes. */
    fun delete(name: String): Boolean
}

/** Seals and opens the history files with the group's key (the PC's `GoalHistoryCodec`): `ABM1` for a month, `ABY1` for a year; null when a file can't be opened. */
interface GoalHistoryCodec {
    fun seal(month: GoalHistoryMonth): ByteArray
    fun seal(year: GoalHistoryYear): ByteArray
    fun openMonth(bytes: ByteArray): GoalHistoryMonth?
    fun openYear(bytes: ByteArray): GoalHistoryYear?
}

/** A device of the sync group as this device knows it from the files it received. [receivedAt]: when a file from it was last accepted (the trusted clock); null for never. */
data class GoalHistoryDeviceInfo(val id: java.util.UUID, val isPc: Boolean, val removed: Boolean, val receivedAt: OffsetDateTime?)

/** What one pass over a transport's goal history files did: counts only, never a name or a value. */
class GoalHistoryRun {
    var ran = false
    var read = 0
    var written = 0
    var rolled = 0
    var removed = 0
    var reuploaded = 0
    var tooBig = 0
    var groupFull = 0
    var unreadable = 0

    override fun toString(): String =
        "read=$read written=$written rolled=$rolled removed=$removed reuploaded=$reuploaded tooBig=$tooBig groupFull=$groupFull unreadable=$unreadable"
}
