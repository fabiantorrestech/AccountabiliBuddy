package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp

/** The migration assistant's page for calendar events in the day plan (ADR 58, since 0.1.73): what it does and where to turn it on. */
@Composable
internal fun CalendarEventsSetupPage() {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Calendar events in the day plan", style = MaterialTheme.typography.headlineSmall)
        Text("A calendar event can give part of your day a plan category, so the guards follow your real day when a meeting moves or is cancelled. It is off until you turn it on.")
        Text("Settings → Your day → Calendar events in the day plan:", style = MaterialTheme.typography.titleSmall)
        Text("• Turn it on; the phone asks to read your calendar then, and not before. What it reads stays on this phone.")
        Text("• For each calendar choose Off, Keywords (a word in the title) or Every event, and the category.")
        Text("• A stricter event applies at once. A looser one (a Rest event over work) counts only if the phone knew of it 10 minutes before it starts, and never during a session.")
        Text("• Events your PC classified come in its sync file when the file is under a day old.")
        Text(
            "Changing these rules waits 10 minutes and is not possible during a session.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}
