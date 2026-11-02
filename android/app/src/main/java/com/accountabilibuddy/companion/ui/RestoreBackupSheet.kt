package com.accountabilibuddy.companion.ui

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Restore
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbInfoBanner
import com.accountabilibuddy.companion.ui.kit.AbInsetRow
import com.accountabilibuddy.companion.ui.kit.AbPageSheet
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * "Restore from an older device" (ADR 55) as a full-height sheet: the backups on Google Drive (newest first), each with Restore, and
 * "Choose a backup file…" through the system file picker. The work and the refusals are in [RestoreBackupFlow]; this only draws its state.
 */
@Composable
internal fun RestoreBackupSheet(state: RestoreState, flow: RestoreBackupFlow) {
    val chooseFile = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) flow.restore(uri)
    }

    AbPageSheet(
        title = "Restore from an older device",
        icon = Icons.Outlined.Restore,
        onDismiss = flow::close,
        footer = {
            AbTonalButton("Choose a backup file…", onClick = { chooseFile.launch(arrayOf("*/*")) }, enabled = !state.working, modifier = Modifier.fillMaxWidth())
            AbTextButton("Close", onClick = flow::close, modifier = Modifier.align(androidx.compose.ui.Alignment.CenterHorizontally))
        },
    ) {
        Text(
            "Pick the backup of a device you removed. Its history comes back under its old name, marked (removed). " +
                "A backup opens only in the group it was made in.",
            style = Ab.type.bodyMedium, color = Ab.colors.ink,
        )
        state.message?.let { AbInfoBanner(it, icon = Icons.Outlined.Restore) }
        AbSectionLabel("On Google Drive")
        when {
            state.loading -> Text("Looking on Google Drive…", style = Ab.type.small, color = Ab.colors.muted)
            state.problem != null -> Text(state.problem, style = Ab.type.small, color = Ab.colors.muted)
            state.backups.isEmpty() -> Text("No backups on Google Drive.", style = Ab.type.small, color = Ab.colors.muted)
            else -> Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                state.backups.forEach { backup ->
                    AbInsetRow {
                        Column(Modifier.weight(1f)) {
                            Text(backup.deviceName, style = Ab.type.body, color = Ab.colors.ink)
                            Text(backup.fileName.removeSuffix(".absync"), style = Ab.type.small, color = Ab.colors.muted)
                        }
                        AbTextButton("Restore", onClick = { flow.restore(backup) }, enabled = !state.working)
                    }
                }
            }
        }
        if (state.working) {
            Text("Working…", style = Ab.type.small, color = Ab.colors.muted)
        }
    }
}
