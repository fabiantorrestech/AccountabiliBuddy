package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarModel

/**
 * Today: the date line with the sync light, the PC's card, the step-away and work-use rows, Beeminder, the timers and the noise
 * player. The tasks moved to Tasks, the day bar and blocks to Plan, the sleep and phone numbers to Stats, and the test session
 * to Settings › Focus sessions (the five-tab shell). No Scaffold and no fixed width: it fills the pane it is given.
 */
@Composable
fun TodayScreen(
    state: CompanionState,
    model: DayBarModel,
    clock: ClockFormat,
    onImport: () -> Unit,
    showsPcCard: Boolean = false,
    pcCard: @Composable () -> Unit = {},
    /** The user's Beeminder goals from a PC's sync file (ADR 43); null hides the card. */
    beeminder: com.accountabilibuddy.companion.sync.BeeminderGoals? = null,
    /** The phone's own timers and alarms. */
    timersCard: @Composable () -> Unit = {},
    /** The noise player, when a sound folder is set; null hides it. */
    noiseCard: (@Composable () -> Unit)? = null,
    /** The sync light and its refresh, at the end of the date line. */
    syncLight: @Composable () -> Unit = {},
) {
    LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        item {
            Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(model.dateText, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                    syncLight()
                }
                Text(model.statusText, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }

        if (!state.hasImport) {
            item {
                Card(Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        IconTitle("Bring your PC's profiles", style = MaterialTheme.typography.titleMedium)
                        Text("Sign in to Google Drive in Settings › Devices & sync, or import a backup.", style = MaterialTheme.typography.bodyMedium)
                        Button(onClick = onImport) { Text("Import backup file") }
                        Hint("On the PC: Settings → Backup → Export (without secrets, or with a password). Put the .abbackup file on the phone, then import it here.", label = "How to export")
                    }
                }
            }
        }

        if (showsPcCard) {
            item { pcCard() }
        }
        item { PcStepAwayRow(clock) }
        item { WorkUseCard() }
        beeminder?.let { goals ->
            item { BeeminderCard(goals, clock) }
        }
        item { timersCard() }
        noiseCard?.let { card -> item { card() } }
    }
}
