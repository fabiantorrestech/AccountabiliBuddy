package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.KeyboardArrowLeft
import androidx.compose.material.icons.automirrored.outlined.KeyboardArrowRight
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.DayOfWeek
import java.time.LocalDate
import java.time.YearMonth
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * A month calendar for picking days: tap a day to pick it, tap again to drop it (one day or several, any spread).
 * Days before [from] can't be picked. ‹ › move a month at a time. A picked day is ink on the page ground (never the accent);
 * today has a hairline ring.
 */
@Composable
fun MonthCalendar(selected: Set<LocalDate>, onToggle: (LocalDate) -> Unit, from: LocalDate, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    var month by remember { mutableStateOf(YearMonth.from(selected.minOrNull() ?: from)) }
    val title = remember(month) { month.format(DateTimeFormatter.ofPattern("MMMM yyyy", Locale.US)) }
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            AbQuietIconButton(
                Icons.AutoMirrored.Outlined.KeyboardArrowLeft, "Previous month",
                onClick = { month = month.minusMonths(1) }, enabled = month.isAfter(YearMonth.from(from)),
            )
            Text(title, style = Ab.type.section, color = colors.ink, modifier = Modifier.weight(1f), textAlign = TextAlign.Center)
            AbQuietIconButton(Icons.AutoMirrored.Outlined.KeyboardArrowRight, "Next month", onClick = { month = month.plusMonths(1) })
        }
        // Weeks start on Monday, like the day plan.
        Row(Modifier.fillMaxWidth()) {
            listOf("M", "T", "W", "T", "F", "S", "S").forEach { day ->
                Text(day, Modifier.weight(1f).padding(vertical = 2.dp), style = Ab.type.micro, color = colors.muted, textAlign = TextAlign.Center)
            }
        }
        val first = month.atDay(1)
        val lead = first.dayOfWeek.value - DayOfWeek.MONDAY.value
        val cells = (List(lead) { null } + (1..month.lengthOfMonth()).map { month.atDay(it) }).chunked(7)
        cells.forEach { week ->
            Row(Modifier.fillMaxWidth()) {
                (week + List(7 - week.size) { null }).forEach { date ->
                    Box(Modifier.weight(1f).height(48.dp).padding(2.dp), contentAlignment = Alignment.Center) {
                        if (date != null) {
                            val picked = date in selected
                            val enabled = !date.isBefore(from)
                            Box(
                                Modifier.size(44.dp).clip(CircleShape)
                                    .background(if (picked) colors.ink else colors.surface)
                                    .let { if (date == from && !picked) it.border(BorderStroke(1.dp, colors.muted), CircleShape) else it }
                                    .semantics { this.selected = picked; role = Role.Checkbox }
                                    .clickable(enabled = enabled) { onToggle(date) }
                                    .alpha(if (enabled) 1f else 0.35f),
                                contentAlignment = Alignment.Center,
                            ) {
                                Text(
                                    date.dayOfMonth.toString(),
                                    style = Ab.type.bodyMedium,
                                    color = if (picked) colors.bg else colors.ink,
                                    fontWeight = if (date == from) FontWeight.Bold else FontWeight.Normal,
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}
