package com.accountabilibuddy.companion.ui

import android.content.Intent
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Checkbox
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.MainActivity
import com.accountabilibuddy.companion.backup.BackupGroup
import com.accountabilibuddy.companion.backup.PhoneBackup
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.theme.Ab
import kotlin.system.exitProcess

/**
 * Restoring a backup file, on Settings → Backup and the first setup page: picks the file, then asks which parts to bring back (everything the
 * file holds is ticked), and restarts the app. Returns what opens the file picker. A problem goes to [onMessage].
 */
@Composable
internal fun rememberBackupRestore(onMessage: (String) -> Unit): () -> Unit {
    val context = LocalContext.current
    var text by remember { mutableStateOf<String?>(null) }
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) {
            val read = runCatching { context.contentResolver.openInputStream(uri)?.use { it.readBytes().decodeToString() } }.getOrNull()
            // ADR 71: a "This phone's settings" file (.abdevice) holds the phone's settings inside a backup file; older files are them bare.
            text = if (read != null && com.accountabilibuddy.companion.data.BackupFile.isBackup(read)) {
                runCatching {
                    val content = com.accountabilibuddy.companion.data.BackupFile.open(read)
                    val app = context.applicationContext as com.accountabilibuddy.companion.CompanionApp
                    com.accountabilibuddy.companion.backup.PhoneKindBackup.problem(app, content, com.accountabilibuddy.companion.backup.PhoneKindBackup.Kind.Device)
                        ?.let { throw com.accountabilibuddy.companion.data.ImportException(it) }
                    content["device"]?.toString() ?: throw com.accountabilibuddy.companion.data.ImportException("The backup has no settings.")
                }.getOrElse { onMessage(it.message ?: "Couldn't open it."); null }
            } else {
                read.also { if (it == null) onMessage("Couldn't read that file.") }
            }
        }
    }

    text?.let { file ->
        val problem = PhoneBackup.importProblem(context, file)
        if (problem != null) {
            onMessage(problem)
            text = null
        } else {
            RestoreChoicesDialog(
                file,
                onDismiss = { text = null },
                onRestore = { groups ->
                    text = null
                    val failed = PhoneBackup.import(context, file, groups)
                    if (failed != null) {
                        onMessage(failed)
                    } else {
                        // A fresh start reads the restored settings everywhere.
                        context.startActivity(Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK))
                        exitProcess(0)
                    }
                },
            )
        }
    }

    return { picker.launch(arrayOf("application/json", "text/plain", "*/*")) }
}

@Composable
private fun RestoreChoicesDialog(file: String, onDismiss: () -> Unit, onRestore: (Set<BackupGroup>) -> Unit) {
    val context = LocalContext.current
    val available = remember(file) { PhoneBackup.groupsIn(file) }
    val required = remember(file) { PhoneBackup.requiredGroups(file) }
    val keepsSync = remember(file) { PhoneBackup.keepsSyncSettings(context, file) }
    val about = remember(file) { PhoneBackup.describe(file) }
    var chosen by remember(file) { mutableStateOf(available.toSet()) }
    AbDialog(
        title = "What should come back?",
        onDismiss = onDismiss,
        confirmText = "Restore and restart",
        onConfirm = { onRestore(chosen + required) },
    ) {
        Column(
            Modifier.fillMaxWidth().heightIn(max = 520.dp).verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            Text("$about · ${available.size} parts", style = Ab.type.small, color = Ab.colors.muted)
            available.forEach { group ->
                val locked = group in required
                val ticked = locked || group in chosen
                fun toggle() {
                    chosen = if (group in chosen) chosen - group else chosen + group
                }

                Row(
                    Modifier.fillMaxWidth().heightIn(min = 48.dp).clickable(enabled = !locked) { toggle() }.padding(vertical = 4.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Checkbox(checked = ticked, onCheckedChange = { toggle() }, enabled = !locked)
                    Column(Modifier.weight(1f)) {
                        Text(group.title, style = Ab.type.bodyMedium, color = Ab.colors.ink)
                        Text(
                            when {
                                locked -> "It holds today's uses, so it comes back: leaving it out would reset today's allowance."
                                group == BackupGroup.SyncSettings && keepsSync -> "This phone's sync settings stay: the file would leave it with no way of syncing."
                                else -> group.description
                            },
                            style = Ab.type.small, color = Ab.colors.muted,
                        )
                    }
                }
            }
        }
    }
}
