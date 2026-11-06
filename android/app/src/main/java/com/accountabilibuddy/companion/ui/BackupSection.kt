package com.accountabilibuddy.companion.ui

import android.content.Intent
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTonalButton

/**
 * Settings → Backup (ADR 71): the auto-backup folder, then one card per kind (synced settings, this phone's settings, task lists), each
 * with export, import and its auto-backup switch.
 */
@Composable
internal fun BackupSection() {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val status by app.autoBackup.status.collectAsState()
    var folder by remember { mutableStateOf(app.autoBackup.folder) }
    var message by remember { mutableStateOf<String?>(null) }
    // Import is locked during a focus session the user started (looked at again while the page is open, and again by import itself).
    var importLocked by remember { mutableStateOf(app.sync.removalLockedReason()) }
    PollWhileVisible(periodMs = { 5_000L }) { importLocked = app.sync.removalLockedReason() }

    val pickFolder = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri ->
        if (uri != null) {
            context.contentResolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION)
            app.autoBackup.folder = uri
            folder = uri
            app.autoBackup.backUpNow("folder chosen")
        }
    }

    Section("Backup", summary = if (folder != null) "Auto-backup on" else "Export or import by kind") {
        Note("Reasons, notes, sign-ins and link codes are never in a backup.")
        AbSectionLabel("Auto-backup folder")
        Note(
            if (folder == null) "Off. Choose a folder: each kind that's on lands there whenever the app updates and a minute after it changes (the 10 newest of each kept)."
            else "To ${com.accountabilibuddy.companion.sync.DriveFolderSource.displayName(folder.toString())}: when the app updates and a minute after a change. With Drive sync on, also sealed to Google Drive.",
        )
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            AbTonalButton(if (folder == null) "Choose folder" else "Change folder", onClick = { pickFolder.launch(null) })
            if (folder != null) {
                AbTonalButton("Back up now", onClick = { app.autoBackup.backUpNow("by hand") })
                AbTextButton("Turn off", onClick = { app.autoBackup.folder = null; folder = null })
            }
        }
        status?.let { Note(it) }
        importLocked?.let { Note("Import rests while you focus. $it") }
        com.accountabilibuddy.companion.backup.PhoneKindBackup.Kind.entries.forEach { kind ->
            BackupKindCard(kind, importLocked, folder != null, onMessage = { message = it })
        }
        message?.let { Note(it) }
    }
}
