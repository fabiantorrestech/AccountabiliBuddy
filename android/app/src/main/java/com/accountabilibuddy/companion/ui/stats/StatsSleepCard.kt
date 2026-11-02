package com.accountabilibuddy.companion.ui.stats

import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import com.accountabilibuddy.companion.ui.HealthStatus
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The night itself, the content the old Sleep card held (it moved here from Stats' first version): the planned night, and the sleep
 * the watch measured, read from Health Connect on this phone. It stays on the phone.
 */
@Composable
fun StatsSleepCard(model: SleepNightModel, onConnect: () -> Unit, onRefresh: () -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    AbCard(modifier) {
        Text("Last night", style = Ab.type.section, color = colors.ink)
        Text(model.plannedText ?: "Sleep mode is off (Settings › Sleep).", style = Ab.type.bodyMedium, color = colors.ink)
        when (model.health) {
            HealthStatus.Checking -> Unit
            HealthStatus.NotAvailable -> Text("No Health Connect: measured sleep can't show.", style = Ab.type.small, color = colors.muted)
            HealthStatus.NeedsPermission -> {
                AbTonalButton("Show measured sleep", onClick = onConnect)
                Text(
                    "The sleep your watch measured (Pixel Watch, Fitbit), read from Health Connect. It stays on the phone.",
                    style = Ab.type.small, color = colors.muted,
                )
            }
            HealthStatus.Connected -> {
                Text(model.lastSleepText ?: "No sleep recorded in the last two days.", style = Ab.type.bodyMedium, color = if (model.lastSleepText == null) colors.muted else colors.ink)
                AbTonalButton("Check again", onClick = onRefresh)
            }
        }
    }
}
