package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncFileCodec
import com.accountabilibuddy.shared.sync.SyncKey
import java.util.UUID

/** What opening a backup gave (ADR 55): the file, or why not. */
enum class BackupOpeningKind { Ready, NotInGroup, DifferentGroup, ThisPhone, Unreadable }

/**
 * @property file The backup's sync file when [kind] is [BackupOpeningKind.Ready].
 * @property message In plain words why it can't be restored, otherwise null.
 */
class BackupOpening(val kind: BackupOpeningKind, val file: SyncFile?, val message: String?) {
    val isReady: Boolean get() = kind == BackupOpeningKind.Ready && file != null
}

/**
 * Opens a removed device's backup (ADR 55), as on the PC (App's BackupRestoreService.Open). A backup is a sealed sync file sealed
 * with the key the group had when it was made. It opens with the group's current key, or with the past key of the group named in
 * its header (ADR 56), so a backup survives later removals. The refusals are the PC's: not in a group, not a backup (or too big),
 * made in a group this phone holds no key for, this phone's own data.
 */
class BackupOpener(private val key: () -> SyncKey?, private val pastKeys: PastKeysStore, private val ownDeviceId: () -> UUID) {
    fun open(bytes: ByteArray): BackupOpening {
        val current = key() ?: return BackupOpening(
            BackupOpeningKind.NotInGroup, null, "Link this phone to your other devices first: a backup opens only in the group it was made in.",
        )
        if (bytes.isEmpty() || bytes.size > MAX_BACKUP_BYTES) {
            return BackupOpening(BackupOpeningKind.Unreadable, null, NOT_A_BACKUP)
        }

        // A sync file starts "ABS1" and the 4-byte id of the group it was sealed for.
        var openWith = current
        val isSyncFile = bytes.size >= HEADER_BYTES && bytes[0] == 'A'.code.toByte() && bytes[1] == 'B'.code.toByte() &&
            bytes[2] == 'S'.code.toByte() && bytes[3] == '1'.code.toByte()
        if (isSyncFile) {
            val groupId = bytes.copyOfRange(4, HEADER_BYTES).joinToString("") { "%02x".format(it) }
            if (groupId != current.groupId) {
                // Another group id: a past key of this group may open it (ADR 56); if none matches, it is a different group.
                openWith = pastKeys.keyFor(groupId) ?: return BackupOpening(BackupOpeningKind.DifferentGroup, null, DIFFERENT_GROUP)
            }
        }

        val file = SyncFileCodec.open(bytes, openWith)
            ?: return BackupOpening(BackupOpeningKind.Unreadable, null, "That backup can't be read. It may be cut off, changed, or from a newer version.")
        return if (file.device.deviceId == ownDeviceId()) {
            BackupOpening(BackupOpeningKind.ThisPhone, null, "That backup is this phone's own data.")
        } else {
            BackupOpening(BackupOpeningKind.Ready, file, null)
        }
    }

    companion object {
        // A backup is a few KB to a few MB; anything bigger isn't one (as on the PC).
        const val MAX_BACKUP_BYTES = 16 * 1024 * 1024

        const val NOT_A_BACKUP = "That isn't an AccountabiliBuddy backup."

        private const val HEADER_BYTES = 8

        private const val DIFFERENT_GROUP =
            "This backup was made in a different group, or in one older than the keys your devices keep (the 20 newest, up to a year), " +
                "so it can't be opened here. Link this phone to the group it was made in, or restore it on a device that holds its key."
    }
}
