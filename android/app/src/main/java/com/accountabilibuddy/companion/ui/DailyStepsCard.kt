package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.DirectionsWalk
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.steps.StepsState
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbRing
import com.accountabilibuddy.companion.ui.theme.Ab
import java.text.NumberFormat
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Today's step count with a ring that fills to the goal (More today, while "Track daily steps" is on). The count and "updated" time are
 * those of the last Health Connect read, which only happens while the app is in front: the time shown is real, not a live count.
 */
@Composable
fun DailyStepsCard(steps: StepsState, clock: ClockFormat) {
    val today = LocalDate.now()
    val count = steps.countOn(today)
    val done = count != null && count >= steps.goal
    AbCard(contentPadding = PaddingValues(horizontal = 20.dp, vertical = 16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp)) {
            Box(contentAlignment = Alignment.Center) {
                AbRing((count ?: 0).toFloat() / steps.goal, diameter = 64.dp, strokeWidth = 9.dp)
                Icon(
                    if (done) Icons.Outlined.Check else Icons.Outlined.DirectionsWalk,
                    contentDescription = null,
                    tint = if (done) Ab.colors.accent else Ab.colors.muted,
                    modifier = Modifier.size(24.dp),
                )
            }
            Column(Modifier.weight(1f)) {
                val numbers = NumberFormat.getIntegerInstance()
                Text(
                    if (count == null) "No steps yet today" else "${numbers.format(count)} of ${numbers.format(steps.goal)} steps",
                    style = Ab.type.section,
                    color = Ab.colors.ink,
                )
                val caption = when {
                    steps.readAt == null -> "Reading Health Connect…"
                    done -> "Goal met · updated ${updated(steps, clock)}"
                    else -> "updated ${updated(steps, clock)}"
                }
                Text(caption, style = Ab.type.small, color = Ab.colors.muted)
            }
        }
    }
}

private fun updated(steps: StepsState, clock: ClockFormat): String {
    val at = LocalDateTime.ofInstant(steps.readAt, ZoneId.systemDefault())
    val time = clock.time(at)
    return if (at.toLocalDate() == LocalDate.now()) time else "${at.toLocalDate()} $time"
}
