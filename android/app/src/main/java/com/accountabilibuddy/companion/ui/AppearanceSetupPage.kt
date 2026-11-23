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
        Text("Settings › Appearance:", style = MaterialTheme.typography.titleSmall)
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

/** What's new in 0.1.83 (AB-Improvements #29): the E-Ink theme, and the e-ink settings that go with it. */
@Composable
internal fun EInkThemeSetupPage() {
    val context = androidx.compose.ui.platform.LocalContext.current
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("E-Ink theme", style = MaterialTheme.typography.headlineSmall)
        Text("A fourth theme for e-ink screens: black on white (or white on black in dark), no tinted fills, firm edges and bold text. Only this phone has it.")
        Text("Settings › Appearance › Theme › E-Ink. Its \"Use e-ink settings\" button also turns Animations Off and Step scrolling on, so the screen redraws once per change.")
        androidx.compose.material3.OutlinedButton(onClick = {
            UiPrefs.setThemeName(context, com.accountabilibuddy.companion.ui.theme.ThemeName.EInk)
            UiPrefs.setMotion(context, MotionSpeed.Off)
            UiPrefs.setStepScroll(context, true)
        }) { Text("Use E-Ink with e-ink settings") }
        Text(
            "The theme stays on this phone; it is never synced. Pick another theme any time in Settings › Appearance.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}
