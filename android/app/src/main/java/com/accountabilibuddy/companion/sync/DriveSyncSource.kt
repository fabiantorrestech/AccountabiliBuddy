package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFolder
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import java.time.Instant
import java.util.UUID

/**
 * The phone's Google Drive method (ADR 26): reads the other devices' files from the app's Drive folder (downloading a
 * file again only when Drive says it changed), then writes this phone's own file and the files it carries that Drive
 * doesn't have yet, under the same opaque names the PCs use. Every 30 seconds while a session runs or the timeline is
 * pinned, else every 15 minutes (the folder's background work runs it too); nothing while the switch is off.
 */
class DriveSyncSource(
    private val sync: SyncCoordinator,
    private val signIn: DriveSignIn,
    private val files: DriveFiles = DriveFiles(DriveNetwork),
) {
    private val running = Mutex()
    private val _status = MutableStateFlow(FolderStatus())

    // Each device's newest file in Drive as last read or written, so a carried file is written only when newer.
    private val inDrive = mutableMapOf<UUID, Instant>()

    // Files already read, by Drive id: the change time Drive gave then.
    private val seen = mutableMapOf<String, Instant>()

    val status: StateFlow<FolderStatus> = _status.asStateFlow()

    /** Syncs at [DrivePoll]'s pace while the app runs; [busy] says whether a session runs or the timeline is pinned. */
    suspend fun loop(busy: () -> Boolean) {
        while (true) {
            syncNow()
            // Checked again at every busy step, so a request to a PC (ADR 42) or a session starting shortens a long wait.
            val step = DrivePoll.interval(true).toMillis()
            var waited = 0L
            while (waited < DrivePoll.interval(busy()).toMillis()) {
                delay(step)
                waited += step
            }
        }
    }

    /** Reads, then writes; does nothing while the switch is off or the phone isn't in a group. */
    suspend fun syncNow() {
        if (!sync.settings.value.driveOn || !sync.isJoined) {
            return
        }

        val ownName = sync.ownFolderFileName() ?: return
        running.withLock {
            val token = signIn.accessToken()
            if (token == null) {
                _status.value = FolderStatus(_status.value.lastSyncAt, SIGN_IN_AGAIN)
                return
            }

            _status.value = withContext(Dispatchers.IO) {
                try {
                    val listed = files.list(token)
                    val unreadable = read(token, listed, ownName)
                    write(token, listed)
                    FolderStatus(Instant.now(), if (unreadable) "Can't read a file in Google Drive." else null)
                } catch (e: Exception) {
                    FolderStatus(_status.value.lastSyncAt, "Can't reach Google Drive right now.")
                }
            }
        }
    }

    /** Signed out or switched off: forget what was read, so the next sign-in reads everything again. */
    fun forget() {
        inDrive.clear()
        seen.clear()
        _status.value = FolderStatus()
    }

    // True when a file couldn't be read (another group's, or damaged).
    private fun read(token: String, listed: List<DriveFileInfo>, ownName: String): Boolean {
        var unreadable = false
        for (file in listed) {
            if (!SyncFolder.isOtherDevicesFile(file.name, ownName) || seen[file.id] == file.modifiedAt) {
                continue
            }

            val bytes = files.download(token, file.id)
            seen[file.id] = file.modifiedAt
            val card = sync.cardOf(bytes)
            if (card == null) {
                unreadable = true
                continue
            }

            inDrive[card.deviceId] = maxOf(card.writtenAt, inDrive[card.deviceId] ?: Instant.MIN)
            sync.acceptSealed(bytes, SyncDeviceCard.METHOD_DRIVE)
        }

        return unreadable
    }

    private fun write(token: String, listed: List<DriveFileInfo>) {
        val existing = listed.associate { it.name to it.id }
        for (file in sync.filesForFolder(inDrive, SyncDeviceCard.METHOD_DRIVE)) {
            files.upload(token, file.name, file.sealed, existing[file.name])
            inDrive[file.deviceId] = file.writtenAt
        }
    }

    companion object {
        const val SIGN_IN_AGAIN = "Sign in to Google Drive again (Devices)."
    }
}
