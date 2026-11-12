package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/** A muted small heading above a group of fields in a goal sheet ("Kind", "Icon", "Colour", "Area"). */
@Composable
fun GoalFieldLabel(text: String, modifier: Modifier = Modifier) {
    Text(text, style = Ab.type.small, color = Ab.colors.muted, modifier = modifier.padding(start = 24.dp, end = 24.dp, top = 14.dp, bottom = 6.dp))
}

/**
 * The icons a goal or area can take (brief §4.3, boards Goals-Phone-New and -Area): a grid of 48 dp cells, the picked one ringed in the
 * accent. [names] are the stored icon names; [GoalIcons] draws them.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun GoalIconGrid(names: List<String>, selected: String, onPick: (String) -> Unit, tint: Color = Ab.colors.muted, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    val shown = if (selected in names) names else listOf(selected) + names
    FlowRow(modifier.padding(horizontal = 24.dp), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        shown.forEach { name ->
            val picked = name == selected
            Surface(
                onClick = { onPick(name) },
                shape = RoundedCornerShape(14.dp),
                color = if (picked) colors.surface2 else colors.surface,
                border = BorderStroke(if (picked) 2.dp else 1.dp, if (picked) colors.accent else colors.line),
                modifier = Modifier.size(48.dp).semantics { this.selected = picked; role = Role.RadioButton; contentDescription = name.replace('_', ' ') },
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(GoalIcons.of(name), contentDescription = null, tint = if (picked) tint else colors.muted, modifier = Modifier.size(22.dp))
                }
            }
        }
    }
}

/** The goal's or area's colour: round swatches, the picked one with a check on a ring (brief §4.3). [colors] are "#RRGGBB". */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun GoalColorRow(colors: List<String>, selected: String, onPick: (String) -> Unit, modifier: Modifier = Modifier) {
    val shown = if (colors.any { it.equals(selected, ignoreCase = true) }) colors else listOf(selected) + colors
    FlowRow(modifier.padding(horizontal = 24.dp), horizontalArrangement = Arrangement.spacedBy(12.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        shown.forEach { hex ->
            val color = GoalIcons.color(hex) ?: return@forEach
            val picked = hex.equals(selected, ignoreCase = true)
            Box(
                Modifier.size(40.dp).clip(CircleShape)
                    .background(if (picked) Ab.colors.ink.copy(alpha = 0.9f) else Color.Transparent, CircleShape)
                    .padding(3.dp).clip(CircleShape).background(color, CircleShape)
                    .clickable(role = Role.RadioButton) { onPick(hex) }
                    .semantics { this.selected = picked; contentDescription = "Colour $hex" },
                contentAlignment = Alignment.Center,
            ) {
                if (picked) Icon(Icons.Outlined.Check, contentDescription = null, tint = Color.White, modifier = Modifier.size(18.dp))
            }
        }
    }
}
