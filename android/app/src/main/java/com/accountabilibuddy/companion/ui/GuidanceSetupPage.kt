package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp

/** What's new (ADR 68): AccountabiliBuddy or ProductaBuddy, for a phone upgraded from before the choice existed. */
@Composable
internal fun GuidanceSetupPage() {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("ProductaBuddy", style = MaterialTheme.typography.headlineSmall)
        Text("The app can now be a companion only: sessions, your day, tasks, stats, steps, sounds and Curbox's blocks, with nothing else nudging or locking, on all your devices. Or keep AccountabiliBuddy guarding you as it does now.")
        Text("Switching to ProductaBuddy waits 10 minutes, like any looser change. You can change it any time in Settings › App & data › App mode.")
        GuidanceSection()
    }
}
