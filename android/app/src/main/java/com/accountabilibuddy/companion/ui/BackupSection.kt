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
import com.accountabilibuddy.companion.backup.PhoneBackup
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTonalButton

/**
 * Settings → Backup: export this phone's settings to a file, import one (asks first, then restarts the app), and
 * auto-backup to a folder (when the app updates and a minute after a setting changes; the 10 newest kept).
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
    androidx.compose.runtime.LaunchedEffect(Unit) {
        while (true) {
            importLocked = app.sync.removalLockedReason()
            kotlinx.coroutines.delay(2_000L)
        }
    }

    val export = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument(PhoneBackup.MIME)) { uri ->
        if (uri != null) {
            message = runCatching {
                context.contentResolver.openOutputStream(uri)?.use { it.write(PhoneBackup.export(context, app.versionName()).toByteArray()) }
                "Exported."
            }.getOrElse { "Couldn't export: ${it.message}" }
        }
    }
    val restore = rememberBackupRestore(onMessage = { message = it })
    val pickFolder = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri ->
        if (uri != null) {
            context.contentResolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION)
            app.autoBackup.folder = uri
            folder = uri
            app.autoBackup.backUpNow("folder chosen")
        }
    }

    Section("Backup", summary = if (folder != null) "Auto-backup on" else "Export or import this phone's settings") {
        Note("This phone's settings, timers, Curbox settings and guard history in one file. Reasons, notes, sign-ins and link codes are never in it. When you import one, you choose what comes back.")
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            AbTonalButton("Export…", onClick = { export.launch("AccountabiliBuddy-phone-settings.json") })
            AbTonalButton("Import…", onClick = restore, enabled = importLocked == null)
        }
        importLocked?.let { Note("Import rests while you focus. $it") }
        AbSectionLabel("Auto-backup")
        Note(
            if (folder == null) "Off. Choose a folder: a backup lands there whenever the app updates and a minute after a setting changes (the 10 newest kept)."
            else "To ${com.accountabilibuddy.companion.sync.DriveFolderSource.displayName(folder.toString())}: when the app updates and a minute after a setting changes.",
        )
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            AbTonalButton(if (folder == null) "Choose folder" else "Change folder", onClick = { pickFolder.launch(null) })
            if (folder != null) {
                AbTonalButton("Back up now", onClick = { app.autoBackup.backUpNow("by hand") })
                AbTextButton("Turn off", onClick = { app.autoBackup.folder = null; folder = null })
            }
        }
        status?.let { Note(it) }
        message?.let { Note(it) }
    }
}
