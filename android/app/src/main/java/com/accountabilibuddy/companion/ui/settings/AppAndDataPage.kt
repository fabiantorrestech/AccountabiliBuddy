package com.accountabilibuddy.companion.ui.settings

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.ui.BackupSection
import com.accountabilibuddy.companion.ui.Hint
import com.accountabilibuddy.companion.ui.Note
import com.accountabilibuddy.companion.ui.Section
import com.accountabilibuddy.companion.ui.StartupSection
import com.accountabilibuddy.companion.ui.kit.AbTonalButton

/** Settings › App & data: start-up, the settings backup, and the old way of bringing the PC's profiles (a backup file: Import). */
@Composable
fun AppAndDataPage(anchor: String?, state: CompanionState, importing: Boolean, onImport: () -> Unit, onClearImport: () -> Unit) {
    SettingsPageList(
        listOf(
            PageRow(SettingsAnchors.STARTUP) { StartupSection() },
            PageRow(SettingsAnchors.BACKUP) { BackupSection() },
            PageRow(SettingsAnchors.PROFILES_FROM_PC) {
                Section("Profiles from the PC", summary = if (state.hasImport) "Imported ${state.importedAt}" else "Old way: a backup file") {
                    Note(
                        if (state.hasImport) "${state.importedFrom}; imported ${state.importedAt}." else "Nothing imported. Linking your devices brings the PC's profiles by itself.",
                    )
                    Hint("On the PC: Settings › App & data › Backup › Export, without secrets or with a password. Open the .abbackup file here. The phone keeps it to itself.")
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        AbTonalButton(if (importing) "Importing…" else "Import backup file", onClick = onImport, enabled = !importing)
                        if (state.hasImport) {
                            AbTonalButton("Remove", onClick = onClearImport)
                        }
                    }
                }
            },
        ),
        anchor,
    )
}
