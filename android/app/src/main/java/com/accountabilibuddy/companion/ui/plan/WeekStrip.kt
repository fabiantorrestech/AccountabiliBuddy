package com.accountabilibuddy.companion.ui.plan

import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * Monday to Sunday of the week being looked at, each day with its date and day type (board 5c). The picked day is ink on the
 * second tone (never the accent) and today's date is bold. A horizontal swipe moves a week ([onShiftWeek], -1 or 1). It fills
 * the width it is given: seven equal cells, no fixed size.
 */
@Composable
fun WeekStrip(cells: List<PlanDayCell>, onPick: (PlanDayCell) -> Unit, onShiftWeek: (Int) -> Unit, modifier: Modifier = Modifier) {
    val shift by rememberUpdatedState(onShiftWeek)
    Row(
        modifier.fillMaxWidth().pointerInput(Unit) {
            var travelled = 0f
            detectHorizontalDragGestures(
                onDragStart = { travelled = 0f },
                onDragEnd = {
                    if (travelled <= -SWIPE_PX) shift(1) else if (travelled >= SWIPE_PX) shift(-1)
                },
                onDragCancel = { travelled = 0f },
                onHorizontalDrag = { _, delta -> travelled += delta },
            )
        },
        horizontalArrangement = Arrangement.spacedBy(2.dp),
    ) {
        for (cell in cells) {
            DayCell(cell, onClick = { onPick(cell) }, Modifier.weight(1f))
        }
    }
}

private const val SWIPE_PX = 90f

@Composable
private fun DayCell(cell: PlanDayCell, onClick: () -> Unit, modifier: Modifier) {
    val colors = Ab.colors
    Surface(
        onClick = onClick,
        modifier = modifier.semantics { contentDescription = cell.description; selected = cell.isSelected; role = Role.Tab },
        shape = Ab.shapes.mediumShape,
        color = if (cell.isSelected) colors.surface2 else Color.Transparent,
        contentColor = if (cell.isSelected) colors.ink else colors.muted,
    ) {
        Column(
            Modifier.heightIn(min = 72.dp).padding(horizontal = 2.dp, vertical = 8.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(2.dp, Alignment.CenterVertically),
        ) {
            Text(cell.weekday, style = Ab.type.micro, maxLines = 1, color = if (cell.isSelected) colors.ink else colors.muted)
            Text(
                cell.number, style = Ab.type.body, maxLines = 1,
                fontWeight = if (cell.isToday) FontWeight.Bold else FontWeight.Medium,
                color = if (cell.isSelected || cell.isToday) colors.ink else colors.muted,
            )
            Text(cell.type, style = Ab.type.micro, maxLines = 1, overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center, color = colors.muted)
        }
    }
}
