package com.accountabilibuddy.companion.sync

import android.content.Context
import android.net.Uri
import android.provider.DocumentsContract
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import com.accountabilibuddy.shared.sync.RekeyCodec
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFolder
import com.accountabilibuddy.shared.sync.SyncKey
import com.accountabilibuddy.shared.sync.SyncReceiptParts
import com.accountabilibuddy.shared.sync.SyncRereadResult
import com.accountabilibuddy.shared.sync.SyncStalePolicy
import com.accountabilibuddy.shared.sync.SyncUnreadableFile
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import java.time.Duration
import java.time.Instant
import java.util.UUID
import java.util.concurrent.TimeUnit

/**
 * The sync folder's last result: when it last synced, and what went wrong (null when nothing did). [otherFiles] is how many files of other
 * devices of the group the last pass found in the folder (a count only, nothing of their content); null before a pass, and for Drive and the relay.
 */
data class FolderStatus(
    val lastSyncAt: Instant? = null,
    val problem: String? = null,
    val otherFiles: Int? = null,
    // The place holds files of another group and none of this group from another device: most likely the other devices are on a
    // newer key (a soft hint, never a problem: a problem would open instant re-pointing and turn the light red).
    val newerLink: Boolean = false,
)

/**
 * The phone's sync folder (ADR 22, way B): a folder another app (e.g. Autosync) keeps in step with Google Drive,
 * chosen with Android's folder picker. Each sync reads the other devices' files first, then writes this phone's own
 * file and the files it carries that the folder doesn't have yet, under the same opaque names the PCs use. Every 15
 * minutes through WorkManager, faster (1 or 5 minutes) only while the pinned timeline notification runs. It also carries the
 * rekey file and clears an old group's files after a removal (ADR 54).
 */
class DriveFolderSource(private val context: Context, private val sync: SyncCoordinator) : RekeySyncMethod, SyncTroubleshootSource {
    private val running = Mutex()
    private val _status = MutableStateFlow(FolderStatus())

    // Each device's newest file in the folder as last read or written, so a carried file is written only when newer.
    private val inFolder = mutableMapOf<UUID, Instant>()

    // Writes this phone's own file only when it changed (or on the slow heartbeat).
    private val ownGate = OwnFileWriteGate()

    // The key generation (ADR 54) the map above belongs to: a new key reads the new group's files from scratch.
    private var generation = -1

    val status: StateFlow<FolderStatus> = _status.asStateFlow()

    /** The goal history pass (NG1: the folder method runs it like Drive, same rules, own files, names and sealing); set by the app. */
    var goalHistory: com.accountabilibuddy.shared.goals.GoalHistorySync? = null

    override val method: String = SyncDeviceCard.METHOD_FOLDER

    /** Starts or stops the 15-minute background sync to match the folder switch. */
    fun schedule() {
        val work = WorkManager.getInstance(context)
        if (backgroundOn()) {
            val request = PeriodicWorkRequestBuilder<DriveFolderWorker>(15, TimeUnit.MINUTES).build()
            work.enqueueUniquePeriodicWork(WORK_NAME, ExistingPeriodicWorkPolicy.UPDATE, request)
        } else {
            work.cancelUniqueWork(WORK_NAME)
        }
    }

    /**
     * While the pinned timeline runs: syncs at the chosen 1 or 5 minutes (15 is left to the background work), and every minute while a
     * lunch runs ([lunchRunning]). [beforeRead] runs before each read (the Autosync nudge). Checked every [STEP] so a lunch starting
     * shortens a long wait.
     */
    suspend fun syncWhilePinned(lunchRunning: () -> Boolean = { false }, beforeRead: () -> Unit = {}) {
        var lastRead = Instant.MIN
        while (true) {
            val settings = sync.settings.value
            val interval = SyncFolder.interval(settings.folderMinutes, timelineRunning = true, lunchRunning = lunchRunning())
            if (isOn() && interval < Duration.ofMinutes(15) && Duration.between(lastRead, Instant.now()) >= interval) {
                lastRead = Instant.now()
                runCatching { beforeRead() }
                syncNow()
            }

            delay(STEP.toMillis())
        }
    }

    /** True while the folder method is on with a folder chosen and this phone is in a group. */
    val active: Boolean get() = isOn()

    /** Reads the folder, then writes to it; does nothing while the switch is off or no folder is chosen. */
    suspend fun syncNow() {
        val tree = sync.settings.value.folderUri?.takeIf { isOn() }?.let(Uri::parse) ?: return
        val ownName = sync.ownFolderFileName() ?: return
        running.withLock {
            withContext(Dispatchers.IO) {
                _status.value = try {
                    followKey()
                    val unreadable = read(tree, ownName)
                    // A rekey the coordinator applied in the background may have changed the key meanwhile: write for the key as it is now.
                    followKey()
                    write(tree)
                    // A folder that can't be listed or written is skipped, tried again at the next pass; never shown as a sync problem.
                    runCatching { goalHistory?.runIfDue(FolderGoalHistoryStore(context, tree)) }
                    FolderStatus(Instant.now(), if (unreadable) "Can't read a file from the sync folder." else null, otherFilesSeen, otherGroupFilesSeen > 0 && otherFilesSeen == 0)
                } catch (e: Exception) {
                    // A folder whose permission was taken away, or a storage app that's gone.
                    FolderStatus(_status.value.lastSyncAt, "Can't open the sync folder: choose it again.")
                }
            }
        }
    }

    // After a new key (ADR 54, or joining or leaving a group) what was remembered about the old group's files no longer applies.
    private fun followKey() {
        if (generation != sync.keyGeneration) {
            generation = sync.keyGeneration
            inFolder.clear()
            ownGate.reset()
        }
    }

    override suspend fun publishRekey(sealed: ByteArray, fileName: String, key: SyncKey): Boolean? {
        val tree = sync.settings.value.folderUri?.takeIf { isOn() }?.let(Uri::parse) ?: return null
        return running.withLock {
            withContext(Dispatchers.IO) {
                try {
                    val target = children(tree).toMap()[fileName]
                        ?: DocumentsContract.createDocument(context.contentResolver, folderDocument(tree), MIME, fileName)
                        ?: return@withContext false
                    context.contentResolver.openOutputStream(target, "wt")?.use { it.write(sealed) } != null
                } catch (e: Exception) {
                    false
                }
            }
        }
    }

    override suspend fun findRekeys(fileNames: Set<String>, key: SyncKey): List<ByteArray> {
        val tree = sync.settings.value.folderUri?.takeIf { isOn() }?.let(Uri::parse) ?: return emptyList()
        return running.withLock {
            withContext(Dispatchers.IO) {
                try {
                    children(tree).filter { (name, _) -> name in fileNames }
                        .mapNotNull { (_, uri) -> context.contentResolver.openInputStream(uri)?.use { it.readBytes() } }
                } catch (e: Exception) {
                    emptyList()
                }
            }
        }
    }

    override suspend fun clearOldGroup(oldKey: SyncKey, deviceIds: List<UUID>) {
        val tree = sync.settings.value.folderUri?.takeIf { isOn() }?.let(Uri::parse) ?: return
        val names = deviceIds.map { SyncFolder.fileNameFor(oldKey.groupId, it) }.toSet()
        running.withLock {
            withContext(Dispatchers.IO) {
                runCatching {
                    for ((name, uri) in children(tree)) {
                        if (name in names) {
                            DocumentsContract.deleteDocument(context.contentResolver, uri)
                        }
                    }
                }
            }
        }
    }

    private fun isOn(): Boolean = sync.settings.value.let { it.folderOn && it.folderUri != null } && sync.isJoined

    override val methodCode: String = SyncReceiptParts.FOLDER

    override val isLive: Boolean get() = isOn()

    // The folder keeps no "seen" marks: every pass reads every file again, so "re-read" is one read pass without the write.
    override suspend fun rereadAll(): SyncRereadResult {
        val tree = sync.settings.value.folderUri?.takeIf { isOn() }?.let(Uri::parse) ?: return SyncRereadResult.none(methodCode)
        val ownName = sync.ownFolderFileName() ?: return SyncRereadResult.none(methodCode)
        return running.withLock {
            withContext(Dispatchers.IO) {
                try {
                    followKey()
                    read(tree, ownName)
                    SyncRereadResult(methodCode, passFiles, passProblems, 0, false)
                } catch (e: Exception) {
                    SyncRereadResult(methodCode, passFiles, passProblems + 1, 0, false)
                }
            }
        }
    }

    override suspend fun tryAgain(name: String) {
        rereadAll()
    }

    override suspend fun writeNow(): Boolean {
        ownGate.reset()
        syncNow()
        return _status.value.problem == null
    }

    // The folder: an old-group file of this phone, or an extra copy of its current file (SyncStalePolicy); never another device's, never the current one.
    override suspend fun removeOwnStale(name: String): Boolean {
        val tree = sync.settings.value.folderUri?.takeIf { isOn() }?.let(Uri::parse) ?: return false
        val ownName = sync.ownFolderFileName() ?: return false
        if (!SyncStalePolicy.mayRemoveFromFolder(name, ownName, sync.ownOldNames())) return false
        return running.withLock {
            withContext(Dispatchers.IO) {
                try {
                    val uri = children(tree).firstOrNull { it.first == name }?.second
                    if (uri == null) {
                        sync.unreadable?.clear(SyncReceiptParts.FOLDER, name)
                        true
                    } else if (DocumentsContract.deleteDocument(context.contentResolver, uri)) {
                        sync.unreadable?.clear(SyncReceiptParts.FOLDER, name)
                        true
                    } else {
                        false
                    }
                } catch (e: Exception) {
                    false
                }
            }
        }
    }

    override suspend fun historyNow() {
        val tree = sync.settings.value.folderUri?.takeIf { isOn() }?.let(Uri::parse) ?: return
        running.withLock {
            withContext(Dispatchers.IO) {
                runCatching { goalHistory?.run(FolderGoalHistoryStore(context, tree)) }
            }
        }
    }

    // The background work also runs Drive's (ADR 26) and the relay's (ADR 46) 15-minute sync.
    private fun backgroundOn(): Boolean = (isOn() || sync.settings.value.driveOn || sync.settings.value.relayOn) && sync.isJoined

    // How many readable files of other devices of the group the last read found (set by read, under the same lock).
    private var otherFilesSeen = 0

    // How many files of another group the last read skipped (same lock).
    private var otherGroupFilesSeen = 0

    // The files the last read looked at and how many of them could not be read (the troubleshooting result line).
    private var passFiles = 0
    private var passProblems = 0

    // True when a file couldn't be read (damaged, or newer than this app; another group's files are skipped).
    private fun read(tree: Uri, ownName: String): Boolean {
        var unreadable = false
        otherFilesSeen = 0
        otherGroupFilesSeen = 0
        passFiles = 0
        passProblems = 0
        val started = System.currentTimeMillis()
        val listing = children(tree)
        val ownStem = ownName.substringBeforeLast('.')
        for ((name, uri) in listing) {
            // This phone's own extra copies the folder's sync made (a "conflict" copy of its own file) are listed as stale (SYNC1c).
            if (SyncFolder.isConflictCopy(name) && name.startsWith(ownStem, ignoreCase = true)) {
                sync.unreadable?.report(SyncReceiptParts.FOLDER, name, SyncUnreadableFile.OWN_COPY, sync.deviceId, ownStale = true)
            }

            if (!SyncFolder.isOtherDevicesFile(name, ownName)) {
                continue
            }

            val bytes = context.contentResolver.openInputStream(uri)?.use { it.readBytes() } ?: continue
            // The rekey file (ADR 54): handed to the coordinator, which applies it a few seconds later (the key change is then
            // picked up by followKey on the next pass).
            if (RekeyCodec.isRekey(bytes)) {
                sync.acceptSealed(bytes, SyncDeviceCard.METHOD_FOLDER)
                continue
            }

            // Another group's file (from before a device removal) is a leftover, not a problem.
            val inspected = sync.inspect(bytes, SyncDeviceCard.METHOD_FOLDER, name)
            if (inspected.otherGroup) {
                otherGroupFilesSeen++
                continue
            }

            val card = inspected.card
            passFiles++
            if (card == null) {
                unreadable = true
                passProblems++
                continue
            }

            // Counted unless this phone wrote it there itself (carrying another device's file on): that says nothing about the folder being shared.
            if (!sync.writtenByThisPhone(SyncDeviceCard.METHOD_FOLDER, card)) {
                otherFilesSeen++
            }

            inFolder[card.deviceId] = maxOf(card.writtenAt, inFolder[card.deviceId] ?: Instant.MIN)
            sync.acceptSealed(bytes, SyncDeviceCard.METHOD_FOLDER, name)
        }

        sync.unreadable?.prune(SyncReceiptParts.FOLDER, listing.map { it.first }.toSet())
        sync.noteReadPass(SyncReceiptParts.FOLDER, passFiles, 0, passProblems, (System.currentTimeMillis() - started).toInt())
        return unreadable
    }

    private fun write(tree: Uri) {
        val existing = children(tree).toMap()
        for (file in sync.filesForFolder(inFolder, ownGate)) {
            val target = existing[file.name] ?: DocumentsContract.createDocument(context.contentResolver, folderDocument(tree), MIME, file.name)
                ?: continue
            context.contentResolver.openOutputStream(target, "wt")?.use { it.write(file.sealed) }
            inFolder[file.deviceId] = file.writtenAt
            if (file.own) {
                ownGate.written(Instant.now())
                sync.noteSent(SyncReceiptParts.FOLDER, file.writtenAt, 0)
            }
        }
    }

    // The folder's files as (name, document uri).
    private fun children(tree: Uri): List<Pair<String, Uri>> {
        val treeId = DocumentsContract.getTreeDocumentId(tree)
        val childrenUri = DocumentsContract.buildChildDocumentsUriUsingTree(tree, treeId)
        val columns = arrayOf(DocumentsContract.Document.COLUMN_DOCUMENT_ID, DocumentsContract.Document.COLUMN_DISPLAY_NAME)
        val files = mutableListOf<Pair<String, Uri>>()
        context.contentResolver.query(childrenUri, columns, null, null, null)?.use { cursor ->
            while (cursor.moveToNext()) {
                val id = cursor.getString(0) ?: continue
                val name = cursor.getString(1) ?: continue
                files += name to DocumentsContract.buildDocumentUriUsingTree(tree, id)
            }
        }

        return files
    }

    private fun folderDocument(tree: Uri): Uri = DocumentsContract.buildDocumentUriUsingTree(tree, DocumentsContract.getTreeDocumentId(tree))

    companion object {
        private const val WORK_NAME = "sync-folder"

        private val STEP: Duration = Duration.ofSeconds(15)

        // No extension is added for this type, so the name stays "<id>.absync".
        private const val MIME = "application/octet-stream"

        /** The chosen folder as people know it, e.g. "Documents/AccountabiliBuddySync". */
        fun displayName(folderUri: String): String =
            runCatching { DocumentsContract.getTreeDocumentId(Uri.parse(folderUri)).substringAfter(':') }.getOrNull()
                ?.ifEmpty { "Internal storage" } ?: folderUri
    }
}
