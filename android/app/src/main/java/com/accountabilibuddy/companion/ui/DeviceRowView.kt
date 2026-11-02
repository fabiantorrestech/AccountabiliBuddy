package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.LinkOff
import androidx.compose.material.icons.outlined.MoreHoriz
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.theme.Ab
import java.util.UUID

/**
 * One device's row (board 5f): its outline icon, the name, "AB 3.4.2 · PC · Google Drive" under it, and at the right a dot with how
 * long ago it synced. A device that can be removed (ADR 54) has a ⋯ menu with "Remove this device…"; during a focus session the user
 * started the item is off and says why ([removalLocked]). A hairline under the row when [divider].
 */
@Composable
internal fun DeviceRowView(row: DeviceRow, divider: Boolean, removalLocked: String? = null, onRemove: (UUID) -> Unit = {}) {
    val colors = Ab.colors
    Column(Modifier.fillMaxWidth()) {
        Row(
            Modifier.fillMaxWidth().heightIn(min = 64.dp).padding(start = if (row.indented) 28.dp else 4.dp, top = 8.dp, bottom = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            // One spoken item (the menu button stays its own): "Work PC, Up to date, 1 min ago".
            Row(
                Modifier.weight(1f).semantics(mergeDescendants = true) { contentDescription = "${row.name}, ${row.stateLabel}, ${row.status}" },
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                SyncGlyph(row.kind, colors.muted, size = 22.dp)
                Column(Modifier.weight(1f)) {
                    Text(row.name, style = Ab.type.body, fontWeight = FontWeight.Medium, color = colors.ink)
                    Text(row.detail, style = Ab.type.small, color = colors.muted)
                }
                // The state in words and a dot, never the colour alone.
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    Box(Modifier.size(7.dp).background(toneColor(row.tone), CircleShape))
                    Text(row.status, style = Ab.type.small, color = colors.muted)
                }
            }
            row.removableId?.let { id -> RemoveMenu(row.name, id, removalLocked, onRemove) }
        }
        if (divider) Box(Modifier.fillMaxWidth().padding(start = 38.dp).height(1.dp).background(colors.line))
    }
}

@Composable
private fun RemoveMenu(name: String, id: UUID, removalLocked: String?, onRemove: (UUID) -> Unit) {
    var open by remember { mutableStateOf(false) }
    Box {
        AbQuietIconButton(Icons.Outlined.MoreHoriz, contentDescription = "More for $name", onClick = { open = true })
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            DropdownMenuItem(
                text = {
                    Column {
                        Text("Remove this device…")
                        if (removalLocked != null) {
                            Text("(Locked: $removalLocked)", style = Ab.type.small)
                        }
                    }
                },
                leadingIcon = { Icon(Icons.Outlined.LinkOff, contentDescription = null) },
                enabled = removalLocked == null,
                onClick = {
                    open = false
                    onRemove(id)
                },
            )
        }
    }
}

/** The dot's colour for a row's tone, from the theme's meaning colours. */
@Composable
private fun toneColor(tone: DeviceTone): Color {
    val colors = Ab.colors
    return when (tone) {
        DeviceTone.UP_TO_DATE, DeviceTone.THIS_DEVICE -> colors.met
        DeviceTone.BEHIND -> colors.sleep
        DeviceTone.WAITING -> colors.faint
        DeviceTone.UNLINKED -> colors.drift
    }
}
