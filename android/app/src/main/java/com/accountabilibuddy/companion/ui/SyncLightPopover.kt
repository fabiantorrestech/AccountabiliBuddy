package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Popup
import androidx.compose.ui.window.PopupProperties
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * What the sync light's dot opens (board 20a): the headline ("Synced 1 min ago · Google Drive"), each device with when it last
 * came, "Sync now" and a link to Settings › Devices & sync. Anchored under the light, right edge to right edge.
 */
@Composable
fun SyncLightPopover(model: SyncLightModel, refreshing: Boolean, onSyncNow: () -> Unit, onOpenDevices: () -> Unit, onDismiss: () -> Unit) {
    val colors = Ab.colors
    val below = with(LocalDensity.current) { 46.dp.roundToPx() }
    Popup(alignment = Alignment.TopEnd, offset = IntOffset(0, below), onDismissRequest = onDismiss, properties = PopupProperties(focusable = true)) {
        Surface(
            shape = Ab.shapes.cardShape, color = colors.surface, contentColor = colors.ink,
            border = BorderStroke(1.dp, colors.line), modifier = Modifier.widthIn(min = 260.dp, max = 320.dp),
        ) {
            Column(Modifier.padding(horizontal = 16.dp, vertical = 14.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(model.headline, style = Ab.type.small, color = colors.muted, modifier = Modifier.padding(bottom = 6.dp))
                model.devices.forEach { device ->
                    Row(Modifier.heightIn(min = 40.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        SyncGlyph(device.kind, if (device.dimmed) colors.faint else colors.muted, size = 20.dp)
                        Text(device.name, style = Ab.type.bodyMedium, color = if (device.dimmed) colors.muted else colors.ink, modifier = Modifier.weight(1f))
                        Text(device.detail, style = Ab.type.small, color = colors.muted)
                    }
                }
                Row(Modifier.padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(20.dp)) {
                    Text(
                        if (refreshing) "Syncing…" else "Sync now", style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold,
                        modifier = Modifier.heightIn(min = 44.dp).clickable(enabled = !refreshing, role = Role.Button, onClick = onSyncNow).padding(vertical = 10.dp),
                    )
                    Text(
                        "Devices & sync", style = Ab.type.bodyMedium, color = colors.muted,
                        modifier = Modifier.heightIn(min = 44.dp).clickable(role = Role.Button, onClick = onOpenDevices).padding(vertical = 10.dp),
                    )
                }
            }
        }
    }
}
