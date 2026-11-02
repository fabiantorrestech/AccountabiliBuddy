package com.accountabilibuddy.companion.ui

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.LinkOff
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.sync.DeviceBackups
import com.accountabilibuddy.companion.sync.SyncShare
import com.accountabilibuddy.companion.ui.kit.AbChoiceRow
import com.accountabilibuddy.companion.ui.kit.AbInfoBanner
import com.accountabilibuddy.companion.ui.kit.AbPageSheet
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The "Remove this device" pages (ADR 54, 55) as a full-height sheet, the PC's five pages in the PC's order: what removal does, keep or
 * erase its data, a backup first (Google Drive when it is a sync method here, else a file through the system file picker, or none), a
 * warning about devices on an older version, and the confirmation. The work, the order and the lock checks are in [RemoveDeviceFlow]
 * (read again on every page and on confirm); this only draws what its state says and calls it.
 */
@Composable
internal fun RemoveDeviceSheet(state: RemoveDeviceState, flow: RemoveDeviceFlow) {
    val chooseFile = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument(SyncShare.MIME_TYPE)) { uri ->
        if (uri != null) flow.onFilePicked(uri)
    }
    val saveFileInstead = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument(SyncShare.MIME_TYPE)) { uri ->
        if (uri != null) flow.saveFileInstead(uri)
    }
    val working = state.step == RemoveStep.Working
    // The Next button of the backup page needs a file when the backup goes to a file.
    val needsFile = state.step == RemoveStep.Backup && state.backup == RemoveBackupChoice.File && state.backupUri == null

    AbPageSheet(
        title = "Remove ${state.name}",
        icon = Icons.Outlined.LinkOff,
        subtitle = state.stepText,
        // Not dismissed while the removal is working; elsewhere it closes without changing anything more.
        onDismiss = { if (!working) flow.close() },
        dismissible = !working,
        footer = {
            when {
                state.step == RemoveStep.Confirm -> {
                    // The one destructive button gets a whole row, so its words never truncate; TalkBack hears the device's name too.
                    AbPrimaryButton(
                        "Remove device", onClick = flow::next, enabled = state.lockedReason == null,
                        modifier = Modifier.fillMaxWidth().semantics { contentDescription = "Remove ${state.name}" },
                    )
                    AbTonalButton("Back", onClick = flow::back, modifier = Modifier.fillMaxWidth())
                    AbTextButton("Cancel", onClick = flow::close, modifier = Modifier.align(Alignment.CenterHorizontally))
                }
                state.isQuestionPage -> {
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        if (state.step != RemoveStep.What) {
                            AbTonalButton("Back", onClick = flow::back, modifier = Modifier.weight(1f))
                        }
                        AbPrimaryButton("Next", onClick = flow::next, enabled = !needsFile, modifier = Modifier.weight(1f))
                    }
                    AbTextButton("Cancel", onClick = flow::close, modifier = Modifier.align(Alignment.CenterHorizontally))
                }
                state.step == RemoveStep.BackupProblem -> {
                    AbPrimaryButton("Try again", onClick = flow::tryBackupAgain, modifier = Modifier.fillMaxWidth())
                    if (state.canSaveFileInstead) {
                        AbTonalButton("Save a file instead…", onClick = { saveFileInstead.launch(state.suggestedFileName) }, modifier = Modifier.fillMaxWidth())
                    }
                    AbTonalButton("Go on without a backup", onClick = flow::goOnWithoutBackup, modifier = Modifier.fillMaxWidth())
                }
                state.step == RemoveStep.Done -> AbPrimaryButton("Close", onClick = flow::close, modifier = Modifier.fillMaxWidth())
                else -> Unit
            }
        },
    ) {
        when (state.step) {
            RemoveStep.What -> WhatPage(state)
            RemoveStep.Data -> DataPage(state, flow)
            RemoveStep.Backup -> BackupPage(state, flow) { chooseFile.launch(state.suggestedFileName) }
            RemoveStep.Older -> OlderPage(state)
            RemoveStep.Confirm -> ConfirmPage(state)
            RemoveStep.Working -> WorkingPage()
            RemoveStep.BackupProblem -> BackupProblemPage(state)
            RemoveStep.Done -> DonePage(state)
        }
    }
}

@Composable
private fun PageText(text: String) = Text(text, style = Ab.type.bodyMedium, color = Ab.colors.ink)

@Composable
private fun PageNote(text: String) = Text(text, style = Ab.type.small, color = Ab.colors.muted)

@Composable
private fun PageHeading(text: String) = Text(text, style = Ab.type.body, fontWeight = FontWeight.SemiBold, color = Ab.colors.ink)

@Composable
private fun WhatPage(state: RemoveDeviceState) {
    PageText("Removing a device locks it out. Your other devices switch to a new key, so it can't read anything new, even if it still has the old link code.")
    PageText("To use it again it has to be linked again, with a link code or QR from a device that is still linked. What it already received stays on it.")
    if (state.isPhone) {
        PageText("Its watch goes with it.")
    }

    LockedLine(state)
}

@Composable
private fun DataPage(state: RemoveDeviceState, flow: RemoveDeviceFlow) {
    PageText("What should happen to the data this phone got from it (focus sessions, pickups, sleep nights, drift)?")
    AbChoiceRow("Keep it", selected = !state.erase, subtitle = "Devices & sync shows it as \"(removed)\" after its name.") { flow.setErase(false) }
    AbChoiceRow(
        "Erase it from every device", selected = state.erase,
        subtitle = "This phone once the backup is done, and each of your other devices when it applies the change. " +
            "Items in shared lists (tasks, Later) stay, because they belong to everyone.",
    ) { flow.setErase(true) }
}

@Composable
private fun BackupPage(state: RemoveDeviceState, flow: RemoveDeviceFlow, chooseFile: () -> Unit) {
    PageText("Back its data up first? The backup is a sealed file only your linked devices can open; a new setup can restore it.")
    if (state.driveAvailable) {
        AbChoiceRow(
            "Google Drive", selected = state.backup == RemoveBackupChoice.Drive,
            subtitle = "In the app's own Drive folder, under Backups.",
        ) { flow.setBackup(RemoveBackupChoice.Drive) }
    }

    AbChoiceRow("Save a file on this phone", selected = state.backup == RemoveBackupChoice.File) { flow.setBackup(RemoveBackupChoice.File) }
    if (state.backup == RemoveBackupChoice.File) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            AbTonalButton(if (state.backupUri == null) "Save a file…" else "Choose another…", onClick = chooseFile)
            Text(
                state.backupUri?.let { DeviceBackups.displayName(LocalContext.current, it) } ?: "Choose where to save it first.",
                style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.weight(1f),
            )
        }
    }

    AbChoiceRow("No backup", selected = state.backup == RemoveBackupChoice.None) { flow.setBackup(RemoveBackupChoice.None) }
    if (!state.driveAvailable) {
        PageNote("Google Drive isn't one of the ways this phone syncs, so the backup goes to a file.")
    }
}

@Composable
private fun OlderPage(state: RemoveDeviceState) {
    PageHeading("Some devices can't get the new key")
    PageText(
        "These run an older version of AccountabiliBuddy, or this phone hasn't heard from them. They will be cut off too. " +
            "Update them first, or link them again afterwards:",
    )
    state.notWrapped.forEach { PageText("• $it") }
}

@Composable
private fun ConfirmPage(state: RemoveDeviceState) {
    PageText("Remove ${state.name} from your synced devices now?")
    PageNote("Your other devices switch to a new key. This can't be undone, except by linking the device again.")
    LockedLine(state)
}

@Composable
private fun WorkingPage() {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp)) {
        CircularProgressIndicator(color = Ab.colors.ink, trackColor = Ab.colors.line, strokeWidth = 3.dp)
        PageText("Removing it and making the backup…")
    }
}

@Composable
private fun BackupProblemPage(state: RemoveDeviceState) {
    PageHeading("The device is removed, but its backup wasn't saved")
    PageText(state.problem)
    PageNote("Its data on this phone is untouched until you pick.")
    if (state.erase) {
        PageNote(
            "You chose Erase: your other devices erase their copy as they apply the change, so this phone's copy is the only one left. " +
                "Closing this window keeps it.",
        )
    }
}

@Composable
private fun DonePage(state: RemoveDeviceState) {
    PageHeading(state.doneTitle)
    state.summary.forEach { PageText("• $it") }
}

// Why it can't be removed right now (a focus session the user started), as text.
@Composable
private fun LockedLine(state: RemoveDeviceState) {
    state.lockedReason?.let { AbInfoBanner("Locked: $it") }
}
