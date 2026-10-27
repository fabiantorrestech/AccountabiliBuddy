package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.unit.dp

/**
 * Settings → Permissions & setup: Beeminder (ADR 43). The token lives on the PC (encrypted there, never synced); this
 * row only says where to put it and opens Beeminder's token page in the browser, where a signed-in user sees it at once.
 */
@Composable
fun BeeminderSection(connected: Boolean) {
    val links = LocalUriHandler.current
    Section("Beeminder", summary = if (connected) "Goals from your PC" else "Set up on your PC") {
        Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(
                "Your goals show on Today through your PC's sync file. The personal auth token goes on the PC: " +
                    "Settings → Integrations → Beeminder. Signed in to Beeminder, the page below shows it at once.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            OutlinedButton(onClick = { links.openUri(BEEMINDER_TOKEN_URL) }) {
                Text("Get my token (beeminder.com)")
            }
        }
    }
}

/** Beeminder's page that shows the signed-in user's personal auth token. */
const val BEEMINDER_TOKEN_URL = "https://www.beeminder.com/api/v1/auth_token.json"
