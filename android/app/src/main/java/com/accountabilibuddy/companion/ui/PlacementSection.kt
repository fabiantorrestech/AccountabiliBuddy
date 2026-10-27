package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.usage.PhoneUsageReader

/** What phone placement needs and does, for Settings and the migration assistant. */
@Composable
private fun PlacementText() {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val timelineOn = app.store.state.collectAsState().value.timelineNotificationOn
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(
            "During a focus session on your PC the phone tells it where it is (in a bag, face down, face up, in a pocket or in use), " +
                "from its own sensors, so the PC can ask you to put it away. Only that label, times and counts leave the phone, never sensor " +
                "values. Nothing vibrates or sounds. If the phone is out for 3 minutes, the essentials screen opens.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        if (!timelineOn) {
            Text(
                "Turn on the timeline notification for phone placement.",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.error,
            )
        }
        if (!PhoneUsageReader(context).hasAccess()) {
            Text(
                "Without Usage access the phone can't tell an essential app from another one, so any unlocked use counts as in use.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

/** Settings → Essentials → Phone placement. */
@Composable
internal fun PlacementSection() {
    val app = LocalContext.current.applicationContext as CompanionApp
    val timelineOn = app.store.state.collectAsState().value.timelineNotificationOn
    Section("Phone placement", summary = if (timelineOn) "On during PC sessions" else "Needs the timeline notification") {
        PlacementText()
    }
}

/** The migration assistant's page for phone placement. */
@Composable
internal fun PlacementSetupPage() {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Phone placement", style = MaterialTheme.typography.headlineSmall)
        PlacementText()
    }
}
