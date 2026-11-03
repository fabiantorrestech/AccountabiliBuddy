package com.accountabilibuddy.companion.sync

import android.content.Context
import android.net.Uri
import android.provider.DocumentsContract
import android.provider.OpenableColumns
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncFileCodec
import com.accountabilibuddy.shared.sync.SyncKey
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.IOException
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.util.UUID

/**
 * The outcome of writing a removed device's backup (ADR 55).
 *
 * @property done True when the backup was written.
 * @property where Where it went, for the user: the Drive path or the file's name; null when not written.
 * @property problem In plain words why not, when [done] is false.
 * @property driveRefused True when Google Drive refused the upload (a full Drive answers 403) or couldn't be reached: a file is offered then.
 */
data class BackupOutcome(val done: Boolean, val where: String?, val problem: String?, val driveRefused: Boolean = false) {
    companion object {
        fun saved(where: String) = BackupOutcome(true, where, null)

        fun failed(problem: String, driveRefused: Boolean = false) = BackupOutcome(false, null, problem, driveRefused)
    }
}

/**
 * Backups of a removed device's data (ADR 55), as on the PC (App's DeviceBackupService). A backup is the device's last received
 * sync file, re-sealed with the group's NEW key (so make it after the key switch), as a sealed `.absync`: to Google Drive under
 * `Backups/<device name> (<4 hex of its id>)/<yyyy-MM-dd>.absync`, or to a file the user picks with the system file picker. What
 * is live or belongs to the devices that stay (a running session, actions, the shared lists, past keys) is left out, so restoring
 * a backup can't bring back a session that is long over or a task someone deleted.
 */
object DeviceBackups {
    private val dayFormat: DateTimeFormatter = DateTimeFormatter.ofPattern("yyyy-MM-dd")

    /** "2026-10-28.absync" for the day. */
    fun fileNameFor(day: LocalDate): String = day.format(dayFormat) + SyncShare.EXTENSION

    /** A name the system file picker suggests: "Pixel 7 (3fa2) 2026-10-28.absync". */
    fun suggestedFileName(card: SyncDeviceCard, day: LocalDate): String =
        "${safeName(card.name)} (${shortId(card.deviceId)}) ${fileNameFor(day)}"

    /**
     * The Drive folder of a device's backups: its name and the first four characters of its id, so two devices with the same name
     * (phones are often both "Pixel 7") never share a folder or overwrite each other's backup; "device" when the name is blank. The
     * PC's rule exactly (the name trimmed, no character filtered), so one device never gets two folders; Drive allows any character in a
     * name, and [DriveFiles] escapes it in its queries. Only the local file name ([suggestedFileName]) filters characters.
     */
    fun folderNameFor(card: SyncDeviceCard): String = "${card.name.trim().ifEmpty { "device" }} (${shortId(card.deviceId)})"

    private fun shortId(deviceId: UUID): String = deviceId.toString().replace("-", "").substring(0, 4)

    // The name with the characters a file name can't hold taken out ("device" when nothing is left), as on the PC.
    private fun safeName(name: String): String {
        val plain = name.filter { it !in "\\/:*?\"<>|" && !it.isISOControl() }.trim()
        return plain.ifEmpty { "device" }
    }

    /** What a backup keeps, sealed with [key]. */
    fun seal(file: SyncFile, key: SyncKey): ByteArray = SyncFileCodec.seal(forBackup(file), key)

    private fun forBackup(file: SyncFile): SyncFile = file.copy(
        running = null,
        actions = null,
        acks = null,
        actionResults = null,
        endEarly = null,
        placement = null,
        events = null,
        nextTasks = null,
        parked = null,
        later = null,
        comebacks = null,
        pastKeys = null,
        categories = null,
        steps = null,
    )

    /** Writes [bytes] to the file the user picked with the system file picker ([uri]). */
    suspend fun writeToFile(context: Context, uri: Uri, bytes: ByteArray): BackupOutcome = withContext(Dispatchers.IO) {
        try {
            // "wt": an existing file the picker returned is replaced, not written over its start.
            val stream = context.contentResolver.openOutputStream(uri, "wt") ?: return@withContext BackupOutcome.failed("Couldn't open the file to write.")
            stream.use { it.write(bytes) }
            BackupOutcome.saved(displayName(context, uri))
        } catch (e: IOException) {
            BackupOutcome.failed("Couldn't write the file: ${e.message ?: "storage error"}")
        } catch (e: SecurityException) {
            BackupOutcome.failed("Couldn't write the file: the phone didn't allow it.")
        }
    }

    /** A picked document's name as its provider says it ("x.absync"), else the last part of its address; for "Backup saved: …". */
    fun displayName(context: Context, uri: Uri): String {
        val named = runCatching {
            context.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { cursor ->
                if (cursor.moveToFirst()) cursor.getString(0) else null
            }
        }.getOrNull()
        return named?.ifEmpty { null } ?: uri.lastPathSegment?.substringAfterLast('/')?.substringAfterLast(':')?.ifEmpty { null } ?: "the file you chose"
    }

    /**
     * Deletes a file the system picker made but nothing was written to (the dialog was cancelled, or the removal failed), so no empty
     * `.absync` is left in the user's folder. Quiet when the provider doesn't allow it.
     */
    fun deleteUnwritten(context: Context, uri: Uri) {
        runCatching { DocumentsContract.deleteDocument(context.contentResolver, uri) }
    }
}

/**
 * The backups on Google Drive (ADR 55): writing one, listing them and downloading one. Plain network calls on the IO dispatcher,
 * with a fresh access token from the Drive sign-in; nothing readable leaves the phone (the file is sealed like any sync file).
 */
class DriveBackups(private val signIn: DriveSignIn, private val files: DriveFiles = DriveFiles(DriveNetwork)) {
    /** Writes [sealedBytes] to `Backups/[deviceFolder]/[fileName]` in the app's Drive folder. */
    suspend fun save(deviceFolder: String, fileName: String, sealedBytes: ByteArray): BackupOutcome {
        val token = signIn.accessToken() ?: return BackupOutcome.failed("Google Drive isn't signed in (sign in again in Settings › Devices & sync).")
        return withContext(Dispatchers.IO) {
            try {
                files.uploadBackup(token, deviceFolder, fileName, sealedBytes)
                BackupOutcome.saved("Google Drive › ${DriveFiles.FOLDER_NAME} › ${DriveFiles.BACKUPS_FOLDER_NAME} › $deviceFolder › $fileName")
            } catch (e: DriveException) {
                if (e.code == 403) {
                    BackupOutcome.failed("Google Drive refused the backup, most likely because it is full.", driveRefused = true)
                } else {
                    BackupOutcome.failed("Google Drive couldn't save the backup (it said ${e.code}).", driveRefused = true)
                }
            } catch (e: Exception) {
                BackupOutcome.failed("Couldn't reach Google Drive to save the backup.", driveRefused = true)
            }
        }
    }

    /** The backups on Drive, newest first, or why they can't be listed. */
    suspend fun list(): DriveBackupList {
        val token = signIn.accessToken() ?: return DriveBackupList(emptyList(), "Google Drive isn't signed in (sign in in Settings › Devices & sync to see its backups).")
        return withContext(Dispatchers.IO) {
            try {
                // A file bigger than a backup can be isn't listed (it is never read into memory): the same cap the opener enforces.
                DriveBackupList(files.listBackups(token).filter { it.size <= BackupOpener.MAX_BACKUP_BYTES }.sortedByDescending { it.modifiedAt }, null)
            } catch (e: Exception) {
                DriveBackupList(emptyList(), "Couldn't reach Google Drive to list its backups.")
            }
        }
    }

    /** The backup's bytes; null when Drive can't be reached. */
    suspend fun download(backup: DriveBackupInfo): ByteArray? {
        val token = signIn.accessToken() ?: return null
        return withContext(Dispatchers.IO) { runCatching { files.download(token, backup.id) }.getOrNull() }
    }
}

/** The backups found on Drive, or the reason none could be listed. */
data class DriveBackupList(val backups: List<DriveBackupInfo>, val problem: String?)
