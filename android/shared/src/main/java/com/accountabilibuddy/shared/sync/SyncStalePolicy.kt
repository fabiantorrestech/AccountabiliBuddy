package com.accountabilibuddy.shared.sync

import java.util.UUID

/**
 * Which files "Remove my stale file" may delete (SYNC1c; the PC's `RemoveOwnStaleAsync` rules): only THIS phone's own old files, on the shared
 * folder and on Google Drive. Never another device's file, never this phone's current file, and never anything on the relay (ADR 46: a relay name
 * is unique per device and the relay deletes only when a device leaves the group). Pure, so the checks drive it with names alone.
 */
object SyncStalePolicy {
    /** This phone's own file names in the groups it left behind (the past keys' group ids). */
    fun oldOwnNames(pastGroupIds: Collection<String>, self: UUID): Set<String> =
        pastGroupIds.map { SyncFolder.fileNameFor(it, self).lowercase() }.toSet()

    /**
     * The shared folder: an old-group file of this phone, or an extra copy of its current file (a "(conflict ...)" copy of the own name). The
     * current file itself is never removable, nor is a name that is not a plain sync file name.
     */
    fun mayRemoveFromFolder(name: String, ownCurrentName: String, oldOwnNames: Set<String>): Boolean {
        if (name.equals(ownCurrentName, ignoreCase = true) || name != name.substringAfterLast('/').substringAfterLast('\\') || !name.endsWith(SyncFolder.EXTENSION, ignoreCase = true)) return false
        val stem = ownCurrentName.substringBeforeLast('.')
        return name.lowercase() in oldOwnNames || (SyncFolder.isConflictCopy(name) && name.startsWith(stem, ignoreCase = true))
    }

    /**
     * Google Drive, one listed file ([name], [id]): a duplicate of this phone's current name (another id than [ownCurrentId], the first listed) or
     * a file of an old group. The file Drive lists as the current one is never removable.
     */
    fun mayRemoveFromDrive(name: String, id: String, ownCurrentName: String, ownCurrentId: String?, oldOwnNames: Set<String>): Boolean {
        if (id == ownCurrentId) return false
        if (name == ownCurrentName) return true
        return name.lowercase() in oldOwnNames
    }

    /** The relay: never. */
    @Suppress("UNUSED_PARAMETER")
    fun mayRemoveFromRelay(name: String): Boolean = false
}
