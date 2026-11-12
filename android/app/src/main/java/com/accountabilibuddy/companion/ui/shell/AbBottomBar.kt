package com.accountabilibuddy.companion.ui.shell

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The bottom bar: 72 dp, five items in the same order everywhere. The active item sits in a 64 x 32 pill on the second
 * tone with ink on it, never in the accent colour (the accent is for progress and the one main button).
 */
@Composable
fun AbBottomBar(selected: AppDestination, onSelect: (AppDestination) -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    Column(modifier.fillMaxWidth().background(colors.bg)) {
        Box(Modifier.fillMaxWidth().height(1.dp).background(colors.line))
        Row(
            Modifier.fillMaxWidth().navigationBarsPadding().height(72.dp),
            horizontalArrangement = Arrangement.SpaceEvenly,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            AppDestination.bar.forEach { destination ->
                val active = destination == selected.barItem
                Column(
                    Modifier.weight(1f).height(72.dp).selectable(selected = active, role = Role.Tab, onClick = { onSelect(destination) }),
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.Center,
                ) {
                    Box(
                        Modifier.width(64.dp).height(32.dp).clip(RoundedCornerShape(16.dp)).let { if (active) it.background(colors.surface2) else it },
                        contentAlignment = Alignment.Center,
                    ) {
                        Icon(destination.icon, contentDescription = null, tint = if (active) colors.ink else colors.muted, modifier = Modifier.size(24.dp))
                    }
                    Text(
                        destination.label, style = Ab.type.small.copy(lineHeight = 16.sp), color = if (active) colors.ink else colors.muted,
                        fontWeight = if (active) FontWeight.SemiBold else FontWeight.Normal, modifier = Modifier.padding(top = 4.dp),
                    )
                }
            }
        }
    }
}
