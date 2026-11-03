package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp

/** The migration assistant's page for "Tell Autosync to sync" (since 0.1.73): what it does and where to turn it on. */
@Composable
internal fun AutosyncSetupPage() {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Tell Autosync to sync", style = MaterialTheme.typography.headlineSmall)
        Text("Lunch timers on your PC, phone and watch stay closer together when Autosync moves the sync folder right after a lunch change. It is off until you set it up.")
        Text("In Autosync:", style = MaterialTheme.typography.titleSmall)
        Text("• Settings → Automation: switch it on and set a Secret code.")
        Text("Here, Settings → Devices & sync → Shared folder:", style = MaterialTheme.typography.titleSmall)
        Text("• Turn on Tell Autosync to sync, type the same code and tap Save code, then Test.")
        Text("• Test only sends the message; it can't tell if Autosync got it. Look for a sync starting in Autosync.")
        Text(
            "Nothing leaves this phone because of this. The code stays on this phone: not in the sync file, not in a backup.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}
