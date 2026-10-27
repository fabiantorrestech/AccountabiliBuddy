package com.accountabilibuddy.companion.sync

import android.content.Context
import android.net.Uri
import android.provider.DocumentsContract
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
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
import java.util.concurrent.TimeUnit

/** The sync folder's last result: when it last synced, and what went wrong (null when nothing did). */
data class FolderStatus(val lastSyncAt: Instant? = null, val problem: String? = null)

/**
 * The phone's sync folder (ADR 22, way B): a folder another app (e.g. Autosync) keeps in step with Google Drive,
 * chosen with Android's folder picker. Each sync reads the other devices' files first, then writes this phone's own
 * file and the files it carries that the folder doesn't have yet, under the same opaque names the PCs use. Every 15
 * minutes through WorkManager, faster (1 or 5 minutes) only while the pinned timeline notification runs.
 */
class DriveFolderSource(private val context: Context, private val sync: SyncCoordinator) {
    private val running = Mutex()
    private val _status = MutableStateFlow(FolderStatus())

    // Each device's newest file in the folder as last read or written, so a carried file is written only when newer.
    private val inFolder = mutableMapOf<UUID, Instant>()

    // Writes this phone's own file only when it changed (or on the slow heartbeat).
    private val ownGate = OwnFileWriteGate()

    val status: StateFlow<FolderStatus> = _status.asStateFlow()

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

    /** While the pinned timeline runs: syncs at the chosen 1 or 5 minutes (15 is left to the background work). */
    suspend fun syncWhilePinned() {
        while (true) {
            val settings = sync.settings.value
            if (isOn() && settings.folderMinutes < 15) {
                syncNow()
            }

            delay(SyncFolder.interval(settings.folderMinutes, timelineRunning = true).toMillis())
        }
    }

    /** Reads the folder, then writes to it; does nothing while the switch is off or no folder is chosen. */
    suspend fun syncNow() {
        val tree = sync.settings.value.folderUri?.takeIf { isOn() }?.let(Uri::parse) ?: return
        val ownName = sync.ownFolderFileName() ?: return
        running.withLock {
            withContext(Dispatchers.IO) {
                _status.value = try {
                    val unreadable = read(tree, ownName)
                    write(tree)
                    FolderStatus(Instant.now(), if (unreadable) "Can't read a file from the sync folder." else null)
                } catch (e: Exception) {
                    // A folder whose permission was taken away, or a storage app that's gone.
                    FolderStatus(_status.value.lastSyncAt, "Can't open the sync folder: choose it again.")
                }
            }
        }
    }

    private fun isOn(): Boolean = sync.settings.value.let { it.folderOn && it.folderUri != null } && sync.isJoined

    // The background work also runs Drive's (ADR 26) and the relay's (ADR 46) 15-minute sync.
    private fun backgroundOn(): Boolean = (isOn() || sync.settings.value.driveOn || sync.settings.value.relayOn) && sync.isJoined

    // True when a file couldn't be read (another group's, or damaged).
    private fun read(tree: Uri, ownName: String): Boolean {
        var unreadable = false
        for ((name, uri) in children(tree)) {
            if (!SyncFolder.isOtherDevicesFile(name, ownName)) {
                continue
            }

            val bytes = context.contentResolver.openInputStream(uri)?.use { it.readBytes() } ?: continue
            val card = sync.cardOf(bytes)
            if (card == null) {
                unreadable = true
                continue
            }

            inFolder[card.deviceId] = maxOf(card.writtenAt, inFolder[card.deviceId] ?: Instant.MIN)
            sync.acceptSealed(bytes, SyncDeviceCard.METHOD_FOLDER)
        }

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

        // No extension is added for this type, so the name stays "<id>.absync".
        private const val MIME = "application/octet-stream"

        /** The chosen folder as people know it, e.g. "Documents/AccountabiliBuddySync". */
        fun displayName(folderUri: String): String =
            runCatching { DocumentsContract.getTreeDocumentId(Uri.parse(folderUri)).substringAfter(':') }.getOrNull()
                ?.ifEmpty { "Internal storage" } ?: folderUri
    }
}
