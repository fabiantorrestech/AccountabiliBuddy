package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp

/** The migration assistant's page for the themes (ADR 49, since 0.1.67): what changed in the look and where to find it. */
@Composable
internal fun AppearanceSetupPage() {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Appearance", style = MaterialTheme.typography.headlineSmall)
        Text("The app now has three themes, the same ones as on your PC: Instrument (the default), Night Desk and Paper, each in a light and a dark version.")
        Text("Settings → App → Appearance:", style = MaterialTheme.typography.titleSmall)
        Text("• Light or dark: Light, Dark, System, At a set time, or After sunset.")
        Text("• After sunset needs a place. You type a latitude and longitude or pick a city from a list on the phone. There is no location permission.")
        Text("• Colour: Quiet keeps colour for progress and the one main button; Standard also colours what has a meaning.")
        Text(
            "Your old Light, Dark or Phone's setting choice carried over as the light or dark choice. The theme and the place stay on this phone; they are never synced.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}
