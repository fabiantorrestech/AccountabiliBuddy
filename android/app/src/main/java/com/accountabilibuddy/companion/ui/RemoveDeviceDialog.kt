package com.accountabilibuddy.companion.ui

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.LinkOff
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.sync.DeviceBackups
import com.accountabilibuddy.companion.sync.SyncShare

/**
 * The "Remove this device" dialog (ADR 54, 55), the PC's five pages in one dialog: what removal does, keep or erase its data, a backup
 * first (Google Drive when it is a sync method here, else a file through the system file picker, or none), a warning about devices
 * on an older version, and the confirmation. The work and its order are in [RemoveDeviceFlow]. Minimal and functional; it gets
 * restyled in the phone UI run.
 */
@Composable
internal fun RemoveDeviceDialog(state: RemoveDeviceState, flow: RemoveDeviceFlow) {
    val chooseFile = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument(SyncShare.MIME_TYPE)) { uri ->
        if (uri != null) flow.onFilePicked(uri)
    }
    val saveFileInstead = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument(SyncShare.MIME_TYPE)) { uri ->
        if (uri != null) flow.saveFileInstead(uri)
    }
    val working = state.step == RemoveStep.Working
    // The Next button of the backup page needs a file when the backup goes to a file.
    val needsFile = state.step == RemoveStep.Backup && state.backup == RemoveBackupChoice.File && state.backupUri == null

    AlertDialog(
        // Not dismissed by a tap outside while the removal is working; elsewhere it closes without changing anything more.
        onDismissRequest = { if (!working) flow.close() },
        icon = { Icon(Icons.Outlined.LinkOff, contentDescription = null) },
        title = {
            Column {
                Text("Remove ${state.name}", maxLines = 2)
                if (state.stepText.isNotEmpty()) {
                    Text(state.stepText, style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                when (state.step) {
                    RemoveStep.What -> WhatPage(state)
                    RemoveStep.Data -> DataPage(state, flow)
                    RemoveStep.Backup -> BackupPage(state, flow) { chooseFile.launch(state.suggestedFileName) }
                    RemoveStep.Older -> OlderPage(state)
                    RemoveStep.Confirm -> ConfirmPage(state)
                    RemoveStep.Working -> Text("Removing it and making the backup…")
                    RemoveStep.BackupProblem -> BackupProblemPage(state)
                    RemoveStep.Done -> DonePage(state)
                }
            }
        },
        confirmButton = {
            when {
                state.isQuestionPage -> TextButton(
                    onClick = flow::next,
                    enabled = !needsFile && !(state.step == RemoveStep.Confirm && state.lockedReason != null),
                ) { Text(if (state.step == RemoveStep.Confirm) "Remove ${state.name}" else "Next") }
                state.step == RemoveStep.BackupProblem -> Column(horizontalAlignment = Alignment.End) {
                    TextButton(onClick = flow::tryBackupAgain) { Text("Try again") }
                    if (state.canSaveFileInstead) {
                        TextButton(onClick = { saveFileInstead.launch(state.suggestedFileName) }) { Text("Save a file instead…") }
                    }
                    TextButton(onClick = flow::goOnWithoutBackup) { Text("Go on without a backup") }
                }
                state.step == RemoveStep.Done -> TextButton(onClick = flow::close) { Text("Close") }
            }
        },
        dismissButton = {
            if (state.isQuestionPage) {
                Row {
                    if (state.step != RemoveStep.What) {
                        TextButton(onClick = flow::back) { Text("Back") }
                    }
                    TextButton(onClick = flow::close) { Text("Cancel") }
                }
            }
        },
    )
}

@Composable
private fun WhatPage(state: RemoveDeviceState) {
    Text("Removing a device locks it out. Your other devices switch to a new key, so it can't read anything new, even if it still has the old link code.")
    Text("To use it again it has to be linked again, with a link code or QR from a device that is still linked. What it already received stays on it.")
    if (state.isPhone) {
        Text("Its watch goes with it.")
    }

    LockedLine(state)
}

@Composable
private fun DataPage(state: RemoveDeviceState, flow: RemoveDeviceFlow) {
    Text("What should happen to the data this phone got from it (focus sessions, pickups, sleep nights, drift)?")
    ChoiceRow(!state.erase, "Keep it. The Devices tab shows it as \"(removed)\" after its name.") { flow.setErase(false) }
    ChoiceRow(
        state.erase,
        "Erase it from every device: this phone once the backup is done, and each of your other devices when it applies the change. " +
            "Items in shared lists (tasks, Later) stay, because they belong to everyone.",
    ) { flow.setErase(true) }
}

@Composable
private fun BackupPage(state: RemoveDeviceState, flow: RemoveDeviceFlow, chooseFile: () -> Unit) {
    Text("Back its data up first? The backup is a sealed file only your linked devices can open; a new setup can restore it.")
    if (state.driveAvailable) {
        ChoiceRow(state.backup == RemoveBackupChoice.Drive, "Google Drive: in the app's own Drive folder, under Backups.") {
            flow.setBackup(RemoveBackupChoice.Drive)
        }
    }

    ChoiceRow(state.backup == RemoveBackupChoice.File, "Save a file on this phone.") { flow.setBackup(RemoveBackupChoice.File) }
    if (state.backup == RemoveBackupChoice.File) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = chooseFile) { Text(if (state.backupUri == null) "Save a file…" else "Choose another…") }
            Text(
                state.backupUri?.let(DeviceBackups::displayName) ?: "Choose where to save it first.",
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }

    ChoiceRow(state.backup == RemoveBackupChoice.None, "No backup.") { flow.setBackup(RemoveBackupChoice.None) }
    if (!state.driveAvailable) {
        Text(
            "Google Drive isn't one of the ways this phone syncs, so the backup goes to a file.",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

@Composable
private fun OlderPage(state: RemoveDeviceState) {
    Text("Some devices can't get the new key", fontWeight = FontWeight.SemiBold)
    Text(
        "These run an older version of AccountabiliBuddy, or this phone hasn't heard from them. They will be cut off too. " +
            "Update them first, or link them again afterwards:",
    )
    state.notWrapped.forEach { Text("• $it") }
}

@Composable
private fun ConfirmPage(state: RemoveDeviceState) {
    Text("Remove ${state.name} from your synced devices now?")
    Text(
        "Your other devices switch to a new key. This can't be undone, except by linking the device again.",
        style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
    LockedLine(state)
}

@Composable
private fun BackupProblemPage(state: RemoveDeviceState) {
    Text("The device is removed, but its backup wasn't saved", fontWeight = FontWeight.SemiBold)
    Text(state.problem)
    Text("Its data on this phone is untouched until you pick.", style = MaterialTheme.typography.bodySmall)
    if (state.erase) {
        Text(
            "You chose Erase: your other devices erase their copy as they apply the change, so this phone's copy is the only one left. " +
                "Closing this window keeps it.",
            style = MaterialTheme.typography.bodySmall,
        )
    }
}

@Composable
private fun DonePage(state: RemoveDeviceState) {
    Text(state.doneTitle, fontWeight = FontWeight.SemiBold)
    state.summary.forEach { Text("• $it") }
}

// Why it can't be removed right now (a focus session the user started), as text.
@Composable
private fun LockedLine(state: RemoveDeviceState) {
    state.lockedReason?.let {
        Text("Locked: $it", color = MaterialTheme.colorScheme.error)
    }
}

@Composable
private fun ChoiceRow(selected: Boolean, text: String, onSelect: () -> Unit) {
    Row(
        Modifier.fillMaxWidth().clickable(onClick = onSelect),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        RadioButton(selected = selected, onClick = onSelect)
        Text(text, Modifier.weight(1f))
    }
}
