package com.accountabilibuddy.companion.ui

import androidx.compose.runtime.Composable
import com.accountabilibuddy.companion.ui.kit.AbTonalButton

/**
 * Health Connect: whether the phone reads your measured sleep, and the button that asks for it. Shown on Settings › Sleep (where it is
 * used) and on Settings › Connections (where the connected things are).
 */
@Composable
internal fun HealthConnectSection(health: HealthStatus, onConnectHealth: () -> Unit) {
    Section("Health Connect", summary = if (health == HealthStatus.Connected) "Reading your sleep" else "Not connected") {
        Note(
            when (health) {
                HealthStatus.Connected -> "Health Connect: reading your measured sleep."
                HealthStatus.NeedsPermission -> "Health Connect: not connected (the timeline's sleep strip and smart bedtime use it)."
                HealthStatus.NotAvailable -> "Health Connect isn't available on this phone. It may need installing or updating."
                HealthStatus.Checking -> "Health Connect: checking…"
            },
        )
        if (health == HealthStatus.NeedsPermission) {
            AbTonalButton("Connect Health Connect", onClick = onConnectHealth)
        }
    }
}
