package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp

/** The migration assistant's page for the own relay (ADR 46); the switch, address and pin are in Devices. */
@Composable
internal fun RelaySetupPage() {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Own relay", style = MaterialTheme.typography.headlineSmall)
        Text(
            "Optional: sync through a small relay you run yourself, without a Google account. Changes reach this phone in " +
                "about a second while a session runs or the timeline is pinned. Set it up in Devices, under \"How this phone shares\" -> Own relay.",
            style = MaterialTheme.typography.bodyMedium,
        )
        Text(RELAY_STEPS, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}
