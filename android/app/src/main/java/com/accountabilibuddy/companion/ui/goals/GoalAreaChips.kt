package com.accountabilibuddy.companion.ui.goals

import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.draw.drawBehind
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbChipRow
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalArea
import java.util.UUID

/**
 * The area filter under the Goals header (brief §4.3): + (a new goal), All, then one chip per area with its icon. The chosen area
 * filters the list on this screen only (not remembered).
 */
@Composable
fun GoalAreaChips(areas: List<GoalArea>, selected: UUID?, onSelect: (UUID?) -> Unit, onNewGoal: () -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    AbChipRow(modifier, contentPadding = PaddingValues(horizontal = 16.dp)) {
        Surface(
            onClick = onNewGoal,
            shape = Ab.shapes.pill,
            color = colors.bg.copy(alpha = 0f),
            contentColor = colors.ink,
            modifier = Modifier.minimumInteractiveComponentSize(),
        ) {
            // A dashed round outline: it adds something, it is not one of the areas (board Goals-Phone).
            val line = colors.line
            Box(
                Modifier.size(36.dp).drawBehind {
                    val stroke = 1.dp.toPx()
                    drawCircle(line, radius = (size.minDimension - stroke) / 2, style = Stroke(stroke, pathEffect = PathEffect.dashPathEffect(floatArrayOf(4.dp.toPx(), 3.dp.toPx()))))
                },
                contentAlignment = Alignment.Center,
            ) {
                Icon(Icons.Outlined.Add, contentDescription = "New goal", modifier = Modifier.size(20.dp))
            }
        }
        AbChip("All", selected = selected == null, onClick = { onSelect(null) })
        areas.forEach { area ->
            AbChip(area.name, selected = selected == area.id, onClick = { onSelect(area.id) }, icon = GoalIcons.of(area.icon))
        }
    }
}
