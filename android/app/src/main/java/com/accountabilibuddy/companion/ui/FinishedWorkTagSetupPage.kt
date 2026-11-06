package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp

/** The migration assistant's page for "I've finished work for the day" and its NFC tag (ADR 67), with the set-up itself. */
@Composable
internal fun FinishedWorkTagSetupPage() {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Finished work for the day", style = MaterialTheme.typography.headlineSmall)
        Text("The full-screen work card's \"Not working today?\" has a new choice: \"I've finished work for the day\". It ends what is left of today's work: on this phone at once, and on your PCs after their own wait for looser changes.")
        Text("• Tap your finished-work tag on that screen to end it now, or wait 10 minutes, like a day off.")
        Text("• Keep the tag where your work really ends, e.g. stuck in your car. It is read only on that screen and on this set-up page; tapping it anywhere else does nothing.")
        Text("• Set-up writes a secret code to a writable tag (NTAG213, 215 or 216); only that tag counts. You can set it up later in Settings › Guards › Work-start nudge.")
        WorkTagControls()
    }
}
