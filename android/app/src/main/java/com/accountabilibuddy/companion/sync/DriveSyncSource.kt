package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.RekeyCodec
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFolder
import com.accountabilibuddy.shared.sync.SyncKey
import kotlinx.coroutines.CancellationException
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
 * pinned, else every 15 minutes (the folder's background work runs it too); nothing while the switch is off. It also
 * carries the rekey file and clears an old group's files after a removal (ADR 54).
 */
class DriveSyncSource(
    private val sync: SyncCoordinator,
    private val signIn: DriveSignIn,
    private val files: DriveFiles = DriveFiles(DriveNetwork),
    /** Runs before each sync, so this phone's file says what's true now (e.g. its linked watch is still valid, Task 18). */
    private val beforeSync: suspend () -> Unit = {},
) : RekeySyncMethod {
    private val running = Mutex()
    private val _status = MutableStateFlow(FolderStatus())

    // Each device's newest file in Drive as last read or written, so a carried file is written only when newer.
    private val inDrive = mutableMapOf<UUID, Instant>()

    // Files already read, by Drive id: the change time Drive gave then.
    private val seen = mutableMapOf<String, Instant>()

    // Writes this phone's own file only when it changed (or on the slow heartbeat).
    private val ownGate = OwnFileWriteGate()

    // The key generation (ADR 54) the maps above belong to: a new key reads the new group's files from scratch.
    private var generation = -1

    val status: StateFlow<FolderStatus> = _status.asStateFlow()

    override val method: String = SyncDeviceCard.METHOD_DRIVE

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

            try {
                beforeSync()
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                // A failed check of the watch link doesn't stop the sync.
            }

            _status.value = withContext(Dispatchers.IO) {
                try {
                    followKey()
                    val listed = files.list(token)
                    val unreadable = read(token, listed, ownName)
                    // A rekey the coordinator applied in the background may have changed the key meanwhile: write for the key as it is now.
                    followKey()
                    write(token, listed)
                    FolderStatus(Instant.now(), if (unreadable) "Can't read a file in Google Drive." else null)
                } catch (e: Exception) {
                    FolderStatus(_status.value.lastSyncAt, "Can't reach Google Drive right now.")
                }
            }
        }
    }

    // After a new key (ADR 54, or joining or leaving a group) what was remembered about the old group's files no longer applies.
    private fun followKey() {
        if (generation != sync.keyGeneration) {
            generation = sync.keyGeneration
            forget()
        }
    }

    override suspend fun publishRekey(sealed: ByteArray, fileName: String, key: SyncKey): Boolean? {
        if (!sync.settings.value.driveOn || !sync.isJoined) {
            return null
        }

        return running.withLock {
            val token = signIn.accessToken() ?: return@withLock false
            withContext(Dispatchers.IO) {
                try {
                    files.upload(token, fileName, sealed, files.list(token).firstOrNull { it.name == fileName }?.id)
                    true
                } catch (e: Exception) {
                    false
                }
            }
        }
    }

    override suspend fun findRekeys(fileNames: Set<String>, key: SyncKey): List<ByteArray> {
        if (!sync.settings.value.driveOn || !sync.isJoined) {
            return emptyList()
        }

        return running.withLock {
            val token = signIn.accessToken() ?: return@withLock emptyList()
            withContext(Dispatchers.IO) {
                try {
                    files.list(token).filter { it.name in fileNames }.map { files.download(token, it.id) }
                } catch (e: Exception) {
                    emptyList()
                }
            }
        }
    }

    override suspend fun clearOldGroup(oldKey: SyncKey, deviceIds: List<UUID>) {
        if (!sync.settings.value.driveOn || !sync.isJoined) {
            return
        }

        val names = deviceIds.map { SyncFolder.fileNameFor(oldKey.groupId, it) }.toSet()
        running.withLock {
            val token = signIn.accessToken() ?: return@withLock
            withContext(Dispatchers.IO) {
                runCatching { files.list(token).filter { it.name in names }.forEach { files.delete(token, it.id) } }
            }
        }
    }

    /** Signed out or switched off: forget what was read, so the next sign-in reads everything again. */
    fun forget() {
        inDrive.clear()
        seen.clear()
        ownGate.reset()
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
            // The rekey file (ADR 54): handed to the coordinator, which applies it a few seconds later (the key change is then
            // picked up by followKey on the next pass).
            if (RekeyCodec.isRekey(bytes)) {
                sync.acceptSealed(bytes, SyncDeviceCard.METHOD_DRIVE)
                continue
            }

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
        for (file in sync.filesForFolder(inDrive, ownGate, SyncDeviceCard.METHOD_DRIVE)) {
            files.upload(token, file.name, file.sealed, existing[file.name])
            inDrive[file.deviceId] = file.writtenAt
            if (file.own) {
                ownGate.written(Instant.now())
            }
        }
    }

    companion object {
        const val SIGN_IN_AGAIN = "Sign in to Google Drive again (Settings › Devices & sync)."
    }
}
