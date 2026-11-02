package com.accountabilibuddy.companion.ui

import android.content.Context
import android.net.Uri
import com.accountabilibuddy.companion.sync.DeviceBackups
import com.accountabilibuddy.companion.sync.DriveBackups
import com.accountabilibuddy.companion.sync.BackupOutcome
import com.accountabilibuddy.companion.sync.RemovalResult
import com.accountabilibuddy.companion.sync.RemovedDevice
import com.accountabilibuddy.companion.sync.SyncCoordinator
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import java.time.LocalDate
import java.util.UUID

/** The pages of the "Remove this device" dialog (ADR 54, 55), as on the PC: five question pages, then working, a backup problem, done. */
enum class RemoveStep { What, Data, Backup, Older, Confirm, Working, BackupProblem, Done }

/** Where the removed device's backup goes. */
enum class RemoveBackupChoice { Drive, File, None }

/**
 * What the dialog shows.
 *
 * @property driveAvailable Google Drive is a sync method in use on this phone: a backup can go there.
 * @property backupUri The file the user picked with the system file picker (needed when the choice is [RemoveBackupChoice.File]).
 * @property notWrapped Names of the devices that would be cut off too, because they can't get the new key (an older app).
 * @property lockedReason Why it can't be removed right now (a focus session the user started); null when it can.
 * @property canSaveFileInstead Drive failed: saving a file instead is offered.
 */
data class RemoveDeviceState(
    val deviceId: UUID,
    val name: String,
    val isPhone: Boolean,
    val driveAvailable: Boolean,
    val suggestedFileName: String,
    val notWrapped: List<String>,
    val lockedReason: String?,
    val step: RemoveStep = RemoveStep.What,
    val erase: Boolean = false,
    val backup: RemoveBackupChoice = if (driveAvailable) RemoveBackupChoice.Drive else RemoveBackupChoice.File,
    val backupUri: Uri? = null,
    val problem: String = "",
    val canSaveFileInstead: Boolean = false,
    val doneTitle: String = "",
    val doneFailed: Boolean = false,
    val summary: List<String> = emptyList(),
) {
    /** The question pages in order; the older-version page only when a device would be cut off. */
    val questionSteps: List<RemoveStep>
        get() = listOfNotNull(RemoveStep.What, RemoveStep.Data, RemoveStep.Backup, RemoveStep.Older.takeIf { notWrapped.isNotEmpty() }, RemoveStep.Confirm)

    val isQuestionPage: Boolean get() = step in questionSteps

    /** E.g. "Step 2 of 4"; empty outside the question pages. */
    val stepText: String
        get() = questionSteps.indexOf(step).let { index -> if (index < 0) "" else "Step ${index + 1} of ${questionSteps.size}" }
}

/**
 * The "Remove this device" flow (ADR 54, 55): the same five steps as the PC (what removal does, keep or erase, backup, older-version
 * warning, confirm). On confirm it does what the PC does, in the same order: the key switch ([SyncCoordinator.removeDevices]), then
 * the backup sealed with the NEW key, then the maker's own keep or erase ([SyncCoordinator.applyDataChoice]) once the backup is made
 * or given up, so a failed backup never loses the only copy. If the backup fails after the key switched, the dialog offers to try
 * again, to save a file instead, or to go on without one; closing it there leaves the data kept. Locked during a focus session the
 * user started: the reason is shown as text. Held by the view model, so a rotation does not lose it.
 */
class RemoveDeviceFlow(
    private val context: Context,
    private val sync: SyncCoordinator,
    private val drive: DriveBackups,
    private val scope: CoroutineScope,
) {
    private val _state = MutableStateFlow<RemoveDeviceState?>(null)

    /** The open dialog, or null. */
    val state: StateFlow<RemoveDeviceState?> = _state.asStateFlow()

    // The device's copy to back up, read just before the removal (a file that arrived while the dialog sat open is in it).
    private var snapshot: SyncFile? = null
    private var result: RemovalResult? = null
    private var day: LocalDate = LocalDate.now()

    // A backup was written into the picked file: it is not an empty file to clean up.
    private var fileWritten = false

    /** Opens the dialog for the device with [deviceId]. */
    fun open(deviceId: UUID) {
        val device = sync.received.value.firstOrNull { it.file.device.deviceId == deviceId } ?: return
        val card = device.file.device
        snapshot = device.file
        result = null
        fileWritten = false
        day = LocalDate.now()
        _state.value = RemoveDeviceState(
            deviceId = deviceId,
            name = card.name.ifBlank { "this device" },
            isPhone = card.kind == SyncDeviceCard.KIND_PHONE,
            driveAvailable = sync.settings.value.driveOn,
            suggestedFileName = DeviceBackups.suggestedFileName(card, day),
            notWrapped = sync.notWrappedFor(listOf(deviceId)),
            lockedReason = sync.removalLockedReason(),
        )
    }

    /** Closes the dialog; during the removal itself it stays (the work goes on), and a backup problem is left with the data kept. */
    fun close() {
        if (_state.value?.step != RemoveStep.Working) {
            discardUnwrittenFile()
            _state.value = null
        }
    }

    fun setErase(erase: Boolean) = change { copy(erase = erase) }

    fun setBackup(choice: RemoveBackupChoice) {
        if (choice != RemoveBackupChoice.File) {
            // A file the picker made for a backup that is not going there any more.
            discardUnwrittenFile()
            change { copy(backup = choice, backupUri = null) }
        } else {
            change { copy(backup = choice) }
        }
    }

    /** The file picked with the system file picker for the backup. */
    fun onFilePicked(uri: Uri) {
        // Picking another place leaves the first one empty: it goes.
        if (_state.value?.backupUri != uri) {
            discardUnwrittenFile()
        }

        change { copy(backupUri = uri, backup = RemoveBackupChoice.File) }
    }

    // The system file picker makes the (empty) file when it is picked. Until a backup was written into it, it is deleted when the dialog
    // closes, the removal fails, or another place is chosen, so no empty ".absync" is left in the user's folder.
    private fun discardUnwrittenFile() {
        val uri = _state.value?.backupUri ?: return
        if (!fileWritten) {
            scope.launch(Dispatchers.IO) { DeviceBackups.deleteUnwritten(context, uri) }
        }

        fileWritten = false
    }

    fun next() {
        val state = _state.value ?: return
        if (state.step != RemoveStep.Confirm) {
            val steps = state.questionSteps
            // The lock is read again on every page: a session may have started since the dialog opened.
            change { copy(step = steps[steps.indexOf(step) + 1], lockedReason = sync.removalLockedReason()) }
            return
        }

        confirm()
    }

    fun back() {
        val state = _state.value ?: return
        val steps = state.questionSteps
        val index = steps.indexOf(state.step)
        if (index > 0) {
            change { copy(step = steps[index - 1], lockedReason = sync.removalLockedReason()) }
        }
    }

    // Remove first (the key changes), then the backup with the new key, then the keep or erase choice.
    private fun confirm() {
        val state = _state.value ?: return
        val locked = sync.removalLockedReason()
        if (locked != null) {
            change { copy(lockedReason = locked) }
            return
        }

        change { copy(step = RemoveStep.Working) }
        snapshot = sync.allReceived.value.firstOrNull { it.file.device.deviceId == state.deviceId }?.file ?: snapshot
        scope.launch(Dispatchers.IO) {
            val removal = sync.removeDevices(listOf(state.deviceId), state.erase)
            if (!removal.done) {
                // Nothing was removed and no backup will be made: the empty file the picker made goes.
                discardUnwrittenFile()
                change {
                    copy(
                        backupUri = null,
                        doneFailed = true,
                        doneTitle = if (removal.conflict) "Another device changed your devices at the same time" else "$name was not removed",
                        summary = listOf(
                            removal.problem ?: "Something went wrong.",
                            if (removal.conflict) "$name was not removed by this attempt. Look at your devices and try again if it is still there."
                            else "Nothing was changed on this phone.",
                        ),
                        step = RemoveStep.Done,
                    )
                }
                return@launch
            }

            result = removal
            makeBackup()
        }
    }

    private suspend fun makeBackup() {
        val state = _state.value ?: return
        val device = snapshot
        if (state.backup == RemoveBackupChoice.None || device == null) {
            finish(null)
            return
        }

        change { copy(step = RemoveStep.Working) }
        // Sealed with the key the group has now, after the switch.
        val key = sync.key.value
        if (key == null) {
            handleBackup(BackupOutcome.failed("This phone isn't in a device group any more, so the backup can't be sealed."))
            return
        }

        val bytes = DeviceBackups.seal(device, key)
        val outcome = when (state.backup) {
            RemoveBackupChoice.Drive -> drive.save(DeviceBackups.folderNameFor(device.device), DeviceBackups.fileNameFor(day), bytes)
            RemoveBackupChoice.File -> state.backupUri?.let { DeviceBackups.writeToFile(context, it, bytes) } ?: BackupOutcome.failed("No file was chosen.")
            RemoveBackupChoice.None -> BackupOutcome.saved("")
        }
        if (outcome.done && state.backup == RemoveBackupChoice.File) {
            fileWritten = true
        }

        handleBackup(outcome)
    }

    private fun handleBackup(outcome: BackupOutcome) {
        if (outcome.done) {
            finish(outcome.where)
            return
        }

        val choice = _state.value?.backup
        change {
            copy(problem = outcome.problem ?: "The backup couldn't be saved.", canSaveFileInstead = choice == RemoveBackupChoice.Drive, step = RemoveStep.BackupProblem)
        }
    }

    /** After a backup problem: try the same place again. */
    fun tryBackupAgain() {
        scope.launch(Dispatchers.IO) { makeBackup() }
    }

    /** After a Drive problem: the backup goes to the file the user picked instead. */
    fun saveFileInstead(uri: Uri) {
        change { copy(backupUri = uri, backup = RemoveBackupChoice.File) }
        scope.launch(Dispatchers.IO) { makeBackup() }
    }

    /** After a backup problem: remove without a backup. */
    fun goOnWithoutBackup() {
        scope.launch(Dispatchers.IO) { finish(null) }
    }

    // The backup is made or given up: now the keep or erase choice is applied, and the page says what happened.
    private fun finish(backupWhere: String?) {
        val state = _state.value ?: return
        val erase = state.erase
        // Your other devices erase (or keep) as they apply the change; this phone does it now, after the backup.
        val reason = sync.applyDataChoice(state.deviceId, if (erase) RemovedDevice.ERASE_DATA else RemovedDevice.KEEP_DATA)
        val lines = mutableListOf("${state.name} can no longer read new data from your devices. To use it again it has to be linked again.")
        if (state.isPhone) {
            lines += "Its watch goes with it."
        }

        lines += when {
            reason != null && erase -> "Its data was not erased on this phone: $reason"
            reason != null -> "The choice to keep its data could not be saved: $reason"
            erase -> "Its data was erased from this phone. Each of your other devices erases it when it applies the change (a PC in a focus session waits until it ends). Shared lists keep the items it added."
            else -> "Its data is kept on every device. Devices & sync shows it as \"${state.name} (removed)\"."
        }
        lines += if (backupWhere == null) {
            if (state.backup == RemoveBackupChoice.None) "No backup was made." else "It was removed without a backup."
        } else {
            "Backup saved: $backupWhere"
        }
        result?.let { removal ->
            if (removal.notWrapped.isNotEmpty()) {
                lines += "${removal.notWrapped.size} other device(s) could not get the new key, so they are cut off too: update them, then link them again."
            }

            if (removal.failedMethods > 0) {
                lines += "${removal.failedMethods} sync method(s) did not take the change: a device that syncs only through those will not hear of it until you link it again."
            }
        }

        change { copy(doneFailed = false, doneTitle = "$name was removed", summary = lines, step = RemoveStep.Done) }
    }

    private fun change(update: RemoveDeviceState.() -> RemoveDeviceState) {
        _state.value = _state.value?.update()
    }
}
