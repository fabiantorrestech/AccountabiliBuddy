package com.accountabilibuddy.companion.ui

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Restore
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

/**
 * "Restore from an older device" (ADR 55), minimal until the setup assistant is built in the phone UI run: the backups on Google
 * Drive (newest first), each with Restore, and "Choose a backup file…" through the system file picker. The work is in
 * [RestoreBackupFlow].
 */
@Composable
internal fun RestoreBackupDialog(state: RestoreState, flow: RestoreBackupFlow) {
    val chooseFile = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) flow.restore(uri)
    }

    AlertDialog(
        onDismissRequest = flow::close,
        icon = { Icon(Icons.Outlined.Restore, contentDescription = null) },
        title = { Text("Restore from an older device") },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(
                    "Pick the backup of a device you removed. Its history comes back under its old name, marked (removed). " +
                        "A backup opens only in the group it was made in.",
                )
                state.message?.let { Text(it, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.primary) }
                Text("On Google Drive", style = MaterialTheme.typography.titleSmall)
                when {
                    state.loading -> Text("Looking on Google Drive…", style = MaterialTheme.typography.bodySmall)
                    state.problem != null -> Text(state.problem, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    state.backups.isEmpty() -> Text("No backups on Google Drive.", style = MaterialTheme.typography.bodySmall)
                    else -> state.backups.forEach { backup ->
                        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                            Column(Modifier.weight(1f)) {
                                Text(backup.deviceName)
                                Text(backup.fileName.removeSuffix(".absync"), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                            TextButton(onClick = { flow.restore(backup) }, enabled = !state.working) { Text("Restore") }
                        }
                    }
                }

                OutlinedButton(onClick = { chooseFile.launch(arrayOf("*/*")) }, enabled = !state.working) { Text("Choose a backup file…") }
                if (state.working) {
                    Text("Working…", style = MaterialTheme.typography.bodySmall)
                }
            }
        },
        confirmButton = { TextButton(onClick = flow::close) { Text("Close") } },
    )
}
