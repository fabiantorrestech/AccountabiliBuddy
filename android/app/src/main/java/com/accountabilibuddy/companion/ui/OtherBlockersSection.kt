package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import com.accountabilibuddy.companion.blockers.OtherBlockers

/**
 * Settings → Permissions & setup: other blockers on this phone (AppBlock, StayFocused, Freedom), status only.
 * Checked again whenever the app comes back to the front, like the permissions above it.
 */
@Composable
fun OtherBlockersSection() {
    val context = LocalContext.current
    var checks by remember { mutableIntStateOf(0) }
    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) { checks++ }
    val found = remember(checks) { OtherBlockers.detect(context) }

    Section("Other blockers", summary = if (found.isEmpty()) "None found" else found.joinToString(", ") { it.name }) {
        Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(
                "AppBlock, StayFocused and Freedom, if installed. AccountabiliBuddy only notices them: their lists and blocks are theirs.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            if (found.isEmpty()) {
                Text("None found on this phone.", style = MaterialTheme.typography.bodyMedium)
            } else {
                found.forEach { Text(OtherBlockers.line(it), style = MaterialTheme.typography.bodyMedium) }
            }
        }
    }
}
