package com.accountabilibuddy.companion.ui.settings

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.blocks.BlockGuard
import com.accountabilibuddy.companion.curbox.CurboxStatus
import com.accountabilibuddy.companion.ui.GuardsRestNote
import com.accountabilibuddy.companion.ui.GuardsScope
import com.accountabilibuddy.companion.ui.Hint
import com.accountabilibuddy.companion.ui.ReenableTimes
import com.accountabilibuddy.companion.ui.Section
import com.accountabilibuddy.companion.ui.ToggleRow
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.LocalTime

/** Curbox (Connections): its status, and the block re-enable times. A guard section: view-only while a focus session runs. */
@Composable
internal fun CurboxSection(curbox: CurboxStatus, onConnect: () -> Unit, onReenableTime: (Boolean, LocalTime) -> String?) =
    GuardsScope { CurboxSectionBody(curbox, onConnect, onReenableTime) }

@Composable
private fun CurboxSectionBody(curbox: CurboxStatus, onConnect: () -> Unit, onReenableTime: (Boolean, LocalTime) -> String?) {
    val summary = when {
        !curbox.installed -> "Not installed"
        !curbox.granted -> "Not connected"
        curbox.focusActive -> "Focus running"
        else -> "Connected"
    }
    Section("Curbox", summary = summary) {
        GuardsRestNote()
        when {
            !curbox.installed -> Text("Not installed (or too old for its API).", style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
            !curbox.granted -> {
                Text("Allow AccountabiliBuddy in Curbox to show its status here.", style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                OutlinedButton(onClick = onConnect) { Text("Connect to Curbox") }
            }
            else -> {
                Text(
                    if (curbox.focusActive) "Focus running: ${curbox.focusGroup ?: ""}${curbox.focusRemainingMinutes?.let { ", $it min left" } ?: ""}" else "No Curbox focus running.",
                    style = MaterialTheme.typography.bodyMedium,
                )
                curbox.screenTimeMinutes?.let { Text("Screen time today (Curbox): ${it / 60}h ${it % 60}m", style = MaterialTheme.typography.bodySmall) }
                curbox.groups.forEach { group ->
                    Text(
                        "${group.kind}: ${group.name} · ${group.count} items${if (group.active == false) " · off" else ""}",
                        style = MaterialTheme.typography.bodySmall,
                    )
                }
                Hint("Importing the groups' apps and sites needs Curbox's API to share them; today it only shares counts.", label = "Why only counts")
                // ADR 37: the day plan's block rules turn Curbox blocks back on.
                val context = LocalContext.current
                var rearm by remember { mutableStateOf(BlockGuard.enabled(context)) }
                ToggleRow(
                    "Turn blocks back on",
                    "A block you turn off comes back on at the day's re-enable time (midnight unless the day plan says otherwise), with a heads-up 5 minutes before.",
                    rearm,
                ) {
                    rearm = it
                    BlockGuard.setEnabled(context, it)
                }
                if (rearm) ReenableTimes(onReenableTime)
                BlockGuard.history(context).take(5).forEach {
                    Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }
        curbox.error?.let { Text(it, style = MaterialTheme.typography.bodySmall, color = Ab.colors.drift) }
    }
}
