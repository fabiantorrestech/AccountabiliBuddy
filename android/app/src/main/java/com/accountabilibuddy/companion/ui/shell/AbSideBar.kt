package com.accountabilibuddy.companion.ui.shell

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/** The sidebar's width: wide enough for the noise player's controls, slim enough to leave a tablet's pane its room. */
val SideBarWidth = 232.dp

/**
 * The tablet's sidebar: the five places in the bar's order, each a row with its icon and label (the active one in the same second-tone
 * pill as the bottom bar, never the accent colour), and under them [footer], where the noise player and the sync light live.
 */
@Composable
fun AbSideBar(selected: AppDestination, onSelect: (AppDestination) -> Unit, modifier: Modifier = Modifier, footer: (@Composable () -> Unit)? = null) {
    val colors = Ab.colors
    Row(modifier.fillMaxHeight().width(SideBarWidth + 1.dp).background(colors.bg)) {
        Column(Modifier.weight(1f).fillMaxHeight().statusBarsPadding().navigationBarsPadding().padding(horizontal = 12.dp, vertical = 12.dp)) {
            Text("AccountabiliBuddy", style = Ab.type.section, color = colors.ink, maxLines = 1, modifier = Modifier.padding(start = 12.dp, top = 8.dp, bottom = 16.dp))
            Column(Modifier.weight(1f).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                AppDestination.entries.forEach { destination ->
                    val active = destination == selected
                    Row(
                        Modifier.fillMaxWidth().heightIn(min = 48.dp).clip(RoundedCornerShape(24.dp))
                            .let { if (active) it.background(colors.surface2) else it }
                            .selectable(selected = active, role = Role.Tab, onClick = { onSelect(destination) })
                            .padding(horizontal = 16.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(14.dp),
                    ) {
                        Icon(destination.icon, contentDescription = null, tint = if (active) colors.ink else colors.muted, modifier = Modifier.size(24.dp))
                        Text(
                            destination.label, style = Ab.type.bodyMedium, color = if (active) colors.ink else colors.muted,
                            fontWeight = if (active) FontWeight.SemiBold else FontWeight.Normal,
                        )
                    }
                }
            }
            if (footer != null) {
                Column(Modifier.fillMaxWidth().padding(top = 8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) { footer() }
            }
        }
        Box(Modifier.width(1.dp).fillMaxHeight().background(colors.line))
    }
}
