package com.accountabilibuddy.shared.sync

import java.security.MessageDigest
import java.time.Duration
import java.util.UUID

/**
 * The sync folder method on the phone (ADR 22, way B: another app such as Autosync keeps a Google Drive folder on the
 * phone). File names match the PC's (SyncFolderService.FileNameFor), so every device writes one file of its own and
 * replaces it, and the names say nothing about who wrote them.
 */
object SyncFolder {
    const val EXTENSION = ".absync"

    /** Android lets background work run every 15 minutes at most often; faster only while the pinned timeline runs. */
    private const val BACKGROUND_MINUTES = 15L

    /** The first 6 bytes of SHA-256 over the group id and the device id ("D" format, lower case), in hex, then [EXTENSION]. */
    fun fileNameFor(groupId: String, deviceId: UUID): String {
        val hash = MessageDigest.getInstance("SHA-256").digest((groupId + deviceId.toString()).toByteArray(Charsets.UTF_8))
        return hash.take(6).joinToString("") { "%02x".format(it) } + EXTENSION
    }

    /** How often the phone reads and writes the folder: the chosen 1, 5 or 15 minutes while the pinned timeline runs, else 15. */
    fun interval(chosenMinutes: Int, timelineRunning: Boolean): Duration {
        val chosen = chosenMinutes.toLong().coerceIn(1, BACKGROUND_MINUTES)
        return Duration.ofMinutes(if (timelineRunning) chosen else BACKGROUND_MINUTES)
    }

    /** A sync file another device wrote: ends in [EXTENSION] and isn't this device's [ownName]. */
    fun isOtherDevicesFile(name: String, ownName: String): Boolean =
        name.endsWith(EXTENSION, ignoreCase = true) && !name.equals(ownName, ignoreCase = true)
}
