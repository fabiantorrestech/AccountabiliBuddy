package com.accountabilibuddy.companion.ui

import android.content.Context
import android.net.Uri
import com.accountabilibuddy.companion.sync.BackupOpener
import com.accountabilibuddy.companion.sync.DriveBackupInfo
import com.accountabilibuddy.companion.sync.DriveBackups
import com.accountabilibuddy.companion.sync.SyncCoordinator
import com.accountabilibuddy.companion.sync.SyncShare
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/**
 * What the "Restore from an older device" dialog shows.
 *
 * @property loading The Drive backups are being listed.
 * @property backups The backups found on Google Drive, newest first.
 * @property problem Why Drive's backups could not be listed, if so.
 * @property working A backup is being downloaded or restored.
 * @property message The result of the last restore, or why it was refused.
 */
data class RestoreState(
    val loading: Boolean = true,
    val backups: List<DriveBackupInfo> = emptyList(),
    val problem: String? = null,
    val working: Boolean = false,
    val message: String? = null,
)

/**
 * Restoring a removed device's backup on the phone (ADR 55), minimal until the setup assistant is built in the phone UI run: Devices
 * tab › "Restore from an older device…" lists the backups on Google Drive (`Backups/`, newest first) and takes a backup file the user
 * picks. A backup opens with the group's current key or the past key of the group it names (ADR 56); the refusals are the PC's (this
 * phone's own backup, a different group, not a backup or too big). Restoring brings that device's history in under its old name,
 * shown as "[name] (removed)". Held by the view model.
 */
class RestoreBackupFlow(
    private val context: Context,
    private val sync: SyncCoordinator,
    private val opener: BackupOpener,
    private val drive: DriveBackups,
    private val scope: CoroutineScope,
) {
    private val _state = MutableStateFlow<RestoreState?>(null)

    /** The open dialog, or null. */
    val state: StateFlow<RestoreState?> = _state.asStateFlow()

    // The newest listing wins: a slow older one never overwrites it.
    private var listing = 0

    /** Opens the dialog and lists the Drive backups. */
    fun open() {
        val mine = ++listing
        if (!sync.isJoined) {
            _state.value = RestoreState(loading = false, message = "Link this phone to your other devices first: a backup opens only in the group it was made in.")
            return
        }

        _state.value = RestoreState(loading = true)
        scope.launch {
            val found = drive.list()
            if (mine == listing) {
                change { copy(loading = false, backups = found.backups, problem = found.problem) }
            }
        }
    }

    fun close() {
        listing++
        _state.value = null
    }

    /** Restores the backup on Drive. */
    fun restore(backup: DriveBackupInfo) {
        change { copy(working = true, message = null) }
        scope.launch(Dispatchers.IO) {
            val bytes = drive.download(backup)
            finish(if (bytes == null) "Couldn't reach Google Drive to download the backup." else restoreBytes(bytes))
        }
    }

    /** Restores the backup file the user picked with the system file picker. */
    fun restore(uri: Uri) {
        change { copy(working = true, message = null) }
        scope.launch(Dispatchers.IO) {
            val bytes = SyncShare.read(context, uri)
            finish(if (bytes == null) BackupOpener.NOT_A_BACKUP else restoreBytes(bytes))
        }
    }

    // The message to show: why it was refused, or what came back.
    private fun restoreBytes(bytes: ByteArray): String {
        val opening = opener.open(bytes)
        val file = opening.file
        if (!opening.isReady || file == null) {
            return opening.message ?: BackupOpener.NOT_A_BACKUP
        }

        return sync.restoreBackup(file)
            ?: "Restored ${file.device.name}: its history is back and shows as \"${file.device.name} (removed)\" in the Devices tab."
    }

    private fun finish(message: String) {
        change { copy(working = false, message = message) }
    }

    private fun change(update: RestoreState.() -> RestoreState) {
        _state.value = _state.value?.update()
    }
}
