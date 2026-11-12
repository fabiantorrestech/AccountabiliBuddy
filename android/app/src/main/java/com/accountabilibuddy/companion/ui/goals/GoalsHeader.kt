package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Insights
import androidx.compose.material.icons.outlined.Tune
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.kit.AbPageHeader
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The phone's Goals header (brief §3.3, §4.3): the shared page header ([AbPageHeader], so "Goals" sits where every tab's title sits),
 * the subtitle "Week of Nov 8 · 5 of 8 on track" (the day the week starts on), then a **Stats** pill (the Stats screen, which left the
 * bottom bar for Goals) and **Options** (tune). No gear.
 */
@Composable
fun GoalsHeader(subtitle: String, onStats: () -> Unit, onOptions: () -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    AbPageHeader(
        "Goals",
        modifier,
        subtitle = { Text(subtitle, style = Ab.type.small.copy(fontSize = 13.5.sp), color = colors.muted, maxLines = 1, overflow = TextOverflow.Ellipsis) },
    ) {
        Surface(
            onClick = onStats,
            shape = Ab.shapes.pill,
            color = colors.surface2,
            contentColor = colors.ink,
            modifier = Modifier.minimumInteractiveComponentSize().semantics { role = Role.Button },
        ) {
            Row(
                Modifier.heightIn(min = 40.dp).padding(horizontal = 16.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                Icon(Icons.Outlined.Insights, contentDescription = null, modifier = Modifier.size(18.dp))
                Text("Stats", style = Ab.type.small, fontWeight = FontWeight.SemiBold)
            }
        }
        IconButton(onClick = onOptions) {
            Icon(Icons.Outlined.Tune, contentDescription = "Goals options", tint = colors.ink)
        }
    }
}
