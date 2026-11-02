package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbToggleRow
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The Pixel Watch link (ADR 21), on Devices & sync. The card's label stays "Pixel Watch" (the watch's own instructions call it that);
 * once a watch is linked its real name shows under it. Nothing goes to a watch until it is linked.
 */
@Composable
internal fun WatchSection(watch: WatchSectionState, actions: WatchSectionActions) {
    val settings = watch.settings
    LaunchedEffect(Unit) { actions.onFind() }

    val linkedName = settings.linkedName
    val linked = settings.linkedNodeId != null && linkedName != null
    AbCard(verticalSpacing = 8.dp) {
        AbSectionLabel("Pixel Watch")
        when {
            linked -> Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Text("Linked: $linkedName", style = Ab.type.body, color = Ab.colors.ink, modifier = Modifier.weight(1f))
                AbTonalButton("Unlink", onClick = actions.onUnlink)
            }
            watch.detected.isEmpty() -> {
                Text("No watch found.", style = Ab.type.bodyMedium, color = Ab.colors.ink)
                Hint("Install the watch app and keep the watch connected to this phone, then check again.", label = "How to find it")
                AbTonalButton("Check again", onClick = actions.onFind)
            }
            else -> watch.detected.forEach { found ->
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    Column(Modifier.weight(1f)) {
                        Text(found.name, style = Ab.type.body, color = Ab.colors.ink)
                        Text(if (found.isNearby) "Nearby" else "Not nearby right now", style = Ab.type.small, color = Ab.colors.muted)
                    }
                    AbTonalButton("Link this watch", onClick = { actions.onLink(found) })
                }
            }
        }

        AbSectionLabel("How it may reach the watch")
        AbToggleRow(
            "Through this phone", checked = settings.dataLayerOn, onCheckedChange = actions.onDataLayer,
            subtitle = "Over the watch's own connection to the phone (Wear OS Data Layer).",
        )
        AbToggleRow(
            "Google's encrypted relay", checked = settings.relayOn && settings.dataLayerOn, onCheckedChange = actions.onRelay,
            enabled = settings.dataLayerOn,
            subtitle = "When the watch is away from the phone's Bluetooth, Google's servers pass it along, end-to-end encrypted so Google can't " +
                "read it. Off: only while the watch is near the phone.",
        )
        Hint(
            "Nothing goes to a watch until you link it. The watch gets plan parts, focus session times and profile names, drift times, " +
                "totals and the daily goal. Never page titles, addresses, reasons or notes.",
            label = "What the watch gets",
        )
    }
}
