package com.accountabilibuddy.companion.sync

import android.content.Context
import android.net.Uri
import android.provider.DocumentsContract
import com.accountabilibuddy.shared.goals.GoalHistoryGroupFullException
import com.accountabilibuddy.shared.goals.GoalHistoryNames
import com.accountabilibuddy.shared.goals.GoalHistoryRemoteFile
import com.accountabilibuddy.shared.goals.GoalHistoryStore
import java.time.OffsetDateTime
import java.time.ZoneOffset

/** The goal history files in the app's own Drive folder (ADR 26, drive.file scope), next to the sync files, under their own extension (the PC's `DriveGoalHistoryStore`). */
class DriveGoalHistoryStore(private val files: DriveFiles, private val token: String) : GoalHistoryStore {
    private val ids = HashMap<String, String>()

    override val transport = "drive"
    override val extension = GoalHistoryNames.DRIVE_EXTENSION
    override val maxFileBytes = 16L * 1024 * 1024
    override val maxFiles: Int? = null
    override val expires = false
    override val canDelete = true

    override fun list(): List<GoalHistoryRemoteFile> {
        val listed = files.list(token)
        ids.clear()
        listed.forEach { ids[it.name] = it.id }
        return listed.map { GoalHistoryRemoteFile(it.name, OffsetDateTime.ofInstant(it.modifiedAt, ZoneOffset.UTC), it.size, null) }
    }

    override fun get(name: String): ByteArray? = ids[name]?.let { files.download(token, it) }

    override fun put(name: String, bytes: ByteArray) {
        ids[name] = files.upload(token, name, bytes, ids[name])
    }

    override fun delete(name: String): Boolean {
        ids[name]?.let {
            files.delete(token, it)
            ids.remove(name)
        }

        return true
    }
}

/**
 * The goal history files on the user's own relay (ADR 46): names of 12 hex characters plus ".absync", at most 64 files and 4 MiB a file, files
 * untouched for 30 days deleted, and a device deletes its files there only when it leaves the group. So the files are put again every 25 days
 * and a rolled-up month file is simply not put again (it expires).
 */
class RelayGoalHistoryStore(private val endpoint: RelayEndpoint) : GoalHistoryStore {
    override val transport = "relay"
    override val extension = GoalHistoryNames.RELAY_EXTENSION
    override val maxFileBytes = 4L * 1024 * 1024
    override val maxFiles: Int? = 64

    // The relay allows 120 requests a minute: a pass stops at 40 and goes on 15 minutes later.
    override val maxRequestsPerPass: Int? = 40
    override val expires = true
    override val canDelete = false

    override fun list(): List<GoalHistoryRemoteFile> =
        RelayNetwork.list(endpoint).map { GoalHistoryRemoteFile(it.name, it.at ?: OffsetDateTime.of(1, 1, 1, 0, 0, 0, 0, ZoneOffset.UTC), it.size, it.etag) }

    override fun get(name: String): ByteArray? = RelayNetwork.get(endpoint, name)?.bytes

    override fun put(name: String, bytes: ByteArray) {
        try {
            RelayNetwork.put(endpoint, name, bytes)
        } catch (e: RelayException) {
            // 429: the group is full (or the rate limit). The pass ends there, quietly, with no retry.
            if (e.problem == RelayProblem.Busy) throw GoalHistoryGroupFullException()
            throw e
        }
    }

    override fun delete(name: String): Boolean = false
}

/**
 * The goal history files in the user's sync folder (ADR 22 way B, NG1): the same names and `.abgh` extension as on Drive, kept next to
 * the sync files, which the regular folder reader skips by extension. Only this phone's own files are ever put or deleted (the pass's
 * rules); a folder whose provider can't delete is said so in [canDelete] and the pass then never deletes.
 * [problem] says why the folder can't be used safely (it can't list); the caller skips the pass then.
 */
class FolderGoalHistoryStore(private val context: Context, private val tree: Uri) : GoalHistoryStore {
    private val resolver get() = context.contentResolver
    private val uris = HashMap<String, Uri>()

    override val transport = "folder"
    override val extension = GoalHistoryNames.DRIVE_EXTENSION
    override val maxFileBytes = 16L * 1024 * 1024
    override val maxFiles: Int? = null
    override val expires = false
    override val canDelete: Boolean = supportsDelete()

    override fun list(): List<GoalHistoryRemoteFile> {
        uris.clear()
        val treeId = DocumentsContract.getTreeDocumentId(tree)
        val children = DocumentsContract.buildChildDocumentsUriUsingTree(tree, treeId)
        val columns = arrayOf(
            DocumentsContract.Document.COLUMN_DOCUMENT_ID, DocumentsContract.Document.COLUMN_DISPLAY_NAME,
            DocumentsContract.Document.COLUMN_LAST_MODIFIED, DocumentsContract.Document.COLUMN_SIZE,
        )
        val files = ArrayList<GoalHistoryRemoteFile>()
        // A folder that can't be listed throws or gives no cursor: the pass is skipped (the caller's catch), never guessed at.
        val cursor = resolver.query(children, columns, null, null, null) ?: throw IllegalStateException("the sync folder can't be listed")
        cursor.use {
            while (it.moveToNext()) {
                val id = it.getString(0) ?: continue
                val name = it.getString(1) ?: continue
                uris[name] = DocumentsContract.buildDocumentUriUsingTree(tree, id)
                val at = if (it.isNull(2)) 0L else it.getLong(2)
                files += GoalHistoryRemoteFile(name, OffsetDateTime.ofInstant(java.time.Instant.ofEpochMilli(at), ZoneOffset.UTC), if (it.isNull(3)) 0L else it.getLong(3), null)
            }
        }

        return files
    }

    override fun get(name: String): ByteArray? = uris[name]?.let { resolver.openInputStream(it)?.use { stream -> stream.readBytes() } }

    override fun put(name: String, bytes: ByteArray) {
        val target = uris[name]
            ?: DocumentsContract.createDocument(resolver, DocumentsContract.buildDocumentUriUsingTree(tree, DocumentsContract.getTreeDocumentId(tree)), MIME, name)
            ?: throw IllegalStateException("the sync folder won't take a file")
        val out = resolver.openOutputStream(target, "wt") ?: throw IllegalStateException("the sync folder won't take a file")
        out.use { it.write(bytes) }
        uris[name] = target
    }

    override fun delete(name: String): Boolean {
        val target = uris[name] ?: return true
        val gone = runCatching { DocumentsContract.deleteDocument(resolver, target) }.getOrDefault(false)
        if (gone) uris.remove(name)
        return gone
    }

    // The tree's folder lets its files be deleted (the provider's flag on each file is checked by the delete itself).
    private fun supportsDelete(): Boolean = runCatching {
        val root = DocumentsContract.buildDocumentUriUsingTree(tree, DocumentsContract.getTreeDocumentId(tree))
        resolver.query(root, arrayOf(DocumentsContract.Document.COLUMN_FLAGS), null, null, null)?.use {
            it.moveToFirst() && (it.getInt(0) and DocumentsContract.Document.FLAG_DIR_SUPPORTS_CREATE) != 0
        } ?: false
    }.getOrDefault(false)

    private companion object {
        const val MIME = "application/octet-stream"
    }
}
