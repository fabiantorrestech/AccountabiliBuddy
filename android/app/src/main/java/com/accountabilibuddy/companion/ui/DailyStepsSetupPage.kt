package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp

/** The migration assistant's page for daily steps (since 0.1.73): what it does, what it asks for and where to turn it on. */
@Composable
internal fun DailyStepsSetupPage() {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Daily steps", style = MaterialTheme.typography.headlineSmall)
        Text("A ring on Today fills to your daily step goal, with the count and when it was last read. It is off until you turn it on.")
        Text("Settings → Your day → Daily steps:", style = MaterialTheme.typography.titleSmall)
        Text("• Turn on \"Track daily steps\"; Health Connect then asks to share your steps with this app, and not before.")
        Text("• Set the goal (8,000 by default).")
        Text("• The count is read only while the app is open, so \"updated\" shows the real time of the last read, not a live count.")
        Text("• Your paired PCs show the same ring from this phone's sealed sync file. The steps go to your own devices and nowhere else, and not to the watch.")
        Text(
            "Turning it off forgets the counts on this phone.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}
