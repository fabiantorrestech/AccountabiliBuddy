package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.RekeyCodec
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFolder
import com.accountabilibuddy.shared.sync.SyncKey
import com.accountabilibuddy.shared.sync.SyncReceiptParts
import com.accountabilibuddy.shared.sync.SyncRereadResult
import com.accountabilibuddy.shared.sync.SyncStalePolicy
import com.accountabilibuddy.shared.sync.SyncUnreadableFile
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
) : RekeySyncMethod, SyncTroubleshootSource {
    private val running = Mutex()
    private val _status = MutableStateFlow(FolderStatus())

    // Each device's newest file in Drive as last read or written, so a carried file is written only when newer.
    private val inDrive = mutableMapOf<UUID, Instant>()

    // Files already read, by Drive id: the change time Drive gave then.
    private val seen = mutableMapOf<String, Instant>()

    // The verdict per file id, kept because [seen] skips files already read: ids of other groups' files, and of this group's
    // files from another device. Together they tell whether the other devices have moved to a newer key.
    private val otherGroupIds = mutableSetOf<String>()
    private val groupIds = mutableSetOf<String>()

    // Writes this phone's own file only when it changed (or on the slow heartbeat).
    private val ownGate = OwnFileWriteGate()

    /** ADR 79 shared history (GS4): a pass over the goal history files in the Drive folder, run after the write when due; set by the app. */
    var goalHistory: com.accountabilibuddy.shared.goals.GoalHistorySync? = null

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
                    // A history failure is not shown: tried again at the next pass.
                    runCatching { goalHistory?.runIfDue(DriveGoalHistoryStore(files, token)) }
                    FolderStatus(Instant.now(), if (unreadable) "Can't read a file in Google Drive." else null, newerLink = otherGroupIds.isNotEmpty() && groupIds.isEmpty())
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
        otherGroupIds.clear()
        groupIds.clear()
        ownGate.reset()
        _status.value = FolderStatus()
    }

    // The files the last read looked at and how many of them could not be read (the troubleshooting result line), and the names by Drive id.
    private var passFiles = 0
    private var passProblems = 0
    private val namesById = mutableMapOf<String, String>()

    // True when a file couldn't be read (damaged, or newer than this app; another group's files are skipped).
    private fun read(token: String, listed: List<DriveFileInfo>, ownName: String): Boolean {
        var unreadable = false
        passFiles = 0
        passProblems = 0
        val started = System.currentTimeMillis()
        // Files that are gone no longer count.
        val listedIds = listed.map { it.id }.toSet()
        otherGroupIds.retainAll(listedIds)
        groupIds.retainAll(listedIds)
        namesById.clear()
        listed.forEach { namesById[it.id] = it.name }
        val ownId = currentOwn(listed, ownName)?.id
        for (file in listed) {
            // The newest of this phone's own name is its file; any more are stale copies (listed for "Remove my stale file").
            if (file.name.equals(ownName, ignoreCase = true)) {
                if (file.id != ownId) sync.unreadable?.report(SyncReceiptParts.DRIVE, file.id, SyncUnreadableFile.OWN_COPY, sync.deviceId, ownStale = true)
                continue
            }

            if (!SyncFolder.isOtherDevicesFile(file.name, ownName) || seen[file.id] == file.modifiedAt) {
                continue
            }

            val bytes = files.download(token, file.id)
            seen[file.id] = file.modifiedAt
            otherGroupIds.remove(file.id)
            groupIds.remove(file.id)
            // The rekey file (ADR 54): handed to the coordinator, which applies it a few seconds later (the key change is then
            // picked up by followKey on the next pass).
            if (RekeyCodec.isRekey(bytes)) {
                sync.acceptSealed(bytes, SyncDeviceCard.METHOD_DRIVE)
                continue
            }

            // Another group's file (from before a device removal) is a leftover, not a problem.
            val inspected = sync.inspect(bytes, SyncDeviceCard.METHOD_DRIVE, file.name)
            if (inspected.otherGroup) {
                otherGroupIds.add(file.id)
                continue
            }

            val card = inspected.card
            passFiles++
            if (card == null) {
                unreadable = true
                passProblems++
                continue
            }

            groupIds.add(file.id)
            inDrive[card.deviceId] = maxOf(card.writtenAt, inDrive[card.deviceId] ?: Instant.MIN)
            sync.acceptSealed(bytes, SyncDeviceCard.METHOD_DRIVE, file.name)
        }

        sync.unreadable?.prune(SyncReceiptParts.DRIVE, listed.map { it.name }.toSet() + listedIds)
        sync.noteReadPass(SyncReceiptParts.DRIVE, passFiles, 0, passProblems, (System.currentTimeMillis() - started).toInt())
        return unreadable
    }

    // Of several Drive files with this phone's own name, the newest (by modified time; the first listed on a tie) is its file and the rest are stale copies.
    private fun currentOwn(listed: List<DriveFileInfo>, ownName: String): DriveFileInfo? =
        listed.filter { it.name.equals(ownName, ignoreCase = true) }.maxByOrNull { it.modifiedAt }

    private fun write(token: String, listed: List<DriveFileInfo>) {
        // The first listed of a name is the file (a duplicate is stale, see read); of this phone's own name the newest one is.
        val existing = listed.asReversed().associate { it.name to it.id }.toMutableMap()
        currentOwn(listed, sync.ownFolderFileName().orEmpty())?.let { existing[it.name] = it.id }
        for (file in sync.filesForFolder(inDrive, ownGate, SyncDeviceCard.METHOD_DRIVE)) {
            files.upload(token, file.name, file.sealed, existing[file.name])
            inDrive[file.deviceId] = file.writtenAt
            if (file.own) {
                ownGate.written(Instant.now())
                sync.noteSent(SyncReceiptParts.DRIVE, file.writtenAt, 0)
            }
        }
    }

    override val methodCode: String = SyncReceiptParts.DRIVE

    override val isLive: Boolean get() = sync.settings.value.driveOn && sync.isJoined

    // SYNC1c "Re-read everything": the "seen" marks go and one pass reads and merges every file again (one download each; merging is idempotent).
    override suspend fun rereadAll(): SyncRereadResult {
        if (!isLive) return SyncRereadResult.none(methodCode)
        val ownName = sync.ownFolderFileName() ?: return SyncRereadResult.none(methodCode)
        return running.withLock {
            val token = signIn.accessToken() ?: return@withLock SyncRereadResult(methodCode, 0, 1, 0, false)
            withContext(Dispatchers.IO) {
                try {
                    followKey()
                    seen.clear()
                    read(token, files.list(token), ownName)
                    SyncRereadResult(methodCode, passFiles, passProblems, 0, false)
                } catch (e: Exception) {
                    SyncRereadResult(methodCode, passFiles, passProblems + 1, 0, false)
                }
            }
        }
    }

    // "Try again" on one file (by its name as the list holds it): its mark goes and the method reads now.
    override suspend fun tryAgain(name: String) {
        seen.keys.filter { namesById[it] == name || it == name }.forEach { seen.remove(it) }
        rereadAfterForget()
    }

    private suspend fun rereadAfterForget() {
        if (!isLive) return
        val ownName = sync.ownFolderFileName() ?: return
        running.withLock {
            val token = signIn.accessToken() ?: return@withLock
            withContext(Dispatchers.IO) {
                runCatching {
                    followKey()
                    read(token, files.list(token), ownName)
                }
            }
        }
    }

    override suspend fun writeNow(): Boolean {
        ownGate.reset()
        syncNow()
        return _status.value.problem == null
    }

    // Google Drive (drive.file: this app's own files): a duplicate of this phone's name, or its file of an old group, listed by Drive id or by name.
    override suspend fun removeOwnStale(name: String): Boolean {
        if (!isLive) return false
        val ownName = sync.ownFolderFileName() ?: return false
        return running.withLock {
            val token = signIn.accessToken() ?: return@withLock false
            withContext(Dispatchers.IO) {
                try {
                    val listing = files.list(token)
                    val current = currentOwn(listing, ownName)?.id
                    val info = listing.firstOrNull { it.id == name } ?: listing.firstOrNull { it.name == name && it.id != current }
                    if (info == null) {
                        sync.unreadable?.clear(SyncReceiptParts.DRIVE, name)
                        return@withContext true
                    }

                    if (!SyncStalePolicy.mayRemoveFromDrive(info.name, info.id, ownName, current, sync.ownOldNames())) return@withContext false
                    files.delete(token, info.id)
                    sync.unreadable?.clear(SyncReceiptParts.DRIVE, name)
                    seen.remove(info.id)
                    // A removal may have changed which copy is current: the next pass writes this phone's file again.
                    ownGate.reset()
                    true
                } catch (e: Exception) {
                    false
                }
            }
        }
    }

    override suspend fun historyNow() {
        if (!isLive) return
        running.withLock {
            val token = signIn.accessToken() ?: return@withLock
            withContext(Dispatchers.IO) {
                runCatching { goalHistory?.run(DriveGoalHistoryStore(files, token)) }
            }
        }
    }

    companion object {
        const val SIGN_IN_AGAIN = "Sign in to Google Drive again (Settings › Devices & sync)."
    }
}
