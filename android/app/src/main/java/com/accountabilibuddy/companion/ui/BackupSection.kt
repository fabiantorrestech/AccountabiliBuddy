package com.accountabilibuddy.companion.ui

import android.content.Intent
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.MainActivity
import com.accountabilibuddy.companion.backup.PhoneBackup
import kotlin.system.exitProcess

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
    var toImport by remember { mutableStateOf<String?>(null) }

    val export = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument(PhoneBackup.MIME)) { uri ->
        if (uri != null) {
            message = runCatching {
                context.contentResolver.openOutputStream(uri)?.use { it.write(PhoneBackup.export(context, app.versionName()).toByteArray()) }
                "Exported."
            }.getOrElse { "Couldn't export: ${it.message}" }
        }
    }
    val import = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) {
            toImport = runCatching { context.contentResolver.openInputStream(uri)?.use { it.readBytes().decodeToString() } }.getOrNull()
            if (toImport == null) message = "Couldn't read that file."
        }
    }
    val pickFolder = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri ->
        if (uri != null) {
            context.contentResolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION)
            app.autoBackup.folder = uri
            folder = uri
            app.autoBackup.backUpNow("folder chosen")
        }
    }

    Section("Backup", summary = if (folder != null) "Auto-backup on" else "Export or import this phone's settings") {
        Text("This phone's settings in one file: essentials, emergency time, headphones, noise, pause, work card, startup, sync switches and sleep. Never the device group's code or the Drive sign-in.",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = { export.launch("AccountabiliBuddy-phone-settings.json") }) { Text("Export…") }
            OutlinedButton(onClick = { import.launch(arrayOf("application/json", "text/plain", "*/*")) }) { Text("Import…") }
        }
        Text("Auto-backup", style = MaterialTheme.typography.labelLarge)
        Text(
            if (folder == null) "Off. Choose a folder: a backup lands there whenever the app updates and a minute after a setting changes (the 10 newest kept)."
            else "To ${com.accountabilibuddy.companion.sync.DriveFolderSource.displayName(folder.toString())}: when the app updates and a minute after a setting changes.",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = { pickFolder.launch(null) }) { Text(if (folder == null) "Choose folder" else "Change folder") }
            if (folder != null) {
                OutlinedButton(onClick = { app.autoBackup.backUpNow("by hand") }) { Text("Back up now") }
                TextButton(onClick = { app.autoBackup.folder = null; folder = null }) { Text("Turn off") }
            }
        }
        status?.let { Text(it, style = MaterialTheme.typography.bodySmall) }
        message?.let { Text(it, style = MaterialTheme.typography.bodySmall) }
    }

    toImport?.let { text ->
        AlertDialog(
            onDismissRequest = { toImport = null },
            title = { Text("Import these settings?") },
            text = { Text("They replace this phone's settings, then the app restarts. Folders (noise, sync, backup) may need choosing again.") },
            confirmButton = {
                TextButton(onClick = {
                    toImport = null
                    val problem = PhoneBackup.import(context, text)
                    if (problem != null) {
                        message = problem
                    } else {
                        // A fresh start reads the imported settings everywhere.
                        context.startActivity(Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK))
                        exitProcess(0)
                    }
                }) { Text("Import and restart") }
            },
            dismissButton = { TextButton(onClick = { toImport = null }) { Text("Cancel") } },
        )
    }
}
