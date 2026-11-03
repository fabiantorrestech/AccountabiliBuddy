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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.steps.StepsProblem
import com.accountabilibuddy.companion.steps.StepsState
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbRing
import com.accountabilibuddy.companion.ui.theme.Ab
import java.text.NumberFormat
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter

/**
 * Today's step count with a ring that fills to the goal (above the fold while "Track daily steps" is on). The count and "updated" time
 * are those of the last Health Connect read: the time shown is real, not a live count. When nothing can be read the card says why
 * (no permission: tap to ask again; no Health Connect).
 */
@Composable
fun DailyStepsCard(steps: StepsState, clock: ClockFormat) {
    val store = (LocalContext.current.applicationContext as CompanionApp).steps
    val today = LocalDate.now()
    val count = steps.countOn(today)
    val done = count != null && count >= steps.goal
    val needsPermission = steps.problem == StepsProblem.NeedsPermission
    AbCard(onClick = if (needsPermission) store::askAgain else null, contentPadding = PaddingValues(horizontal = 20.dp, vertical = 16.dp)) {
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
                val title = when (steps.problem) {
                    StepsProblem.NeedsPermission -> "Steps need Health Connect's permission"
                    StepsProblem.NotAvailable -> "Health Connect isn't available"
                    StepsProblem.None -> when {
                        count != null -> "${numbers.format(count)} of ${numbers.format(steps.goal)} steps"
                        steps.readAt == null -> "Reading steps…"
                        else -> "No steps yet today"
                    }
                }
                Text(title, style = Ab.type.section, color = Ab.colors.ink)
                val caption = when {
                    needsPermission -> "Tap to allow it, or allow steps in Health Connect's settings" + steps.readAt?.let { " · last read ${updated(it, clock)}" }.orEmpty()
                    steps.problem == StepsProblem.NotAvailable -> "Install or update it to count steps"
                    steps.readAt == null -> "from Health Connect"
                    done -> "Goal met · updated ${updated(steps.readAt, clock)}"
                    else -> "updated ${updated(steps.readAt, clock)}"
                }
                Text(caption, style = Ab.type.small, color = Ab.colors.muted)
            }
        }
    }
}

private val DAY_FORMAT: DateTimeFormatter = DateTimeFormatter.ofPattern("MMM d")

// "4:05 PM" today, "Nov 2, 4:05 PM" another day.
private fun updated(readAt: java.time.Instant, clock: ClockFormat): String {
    val at = LocalDateTime.ofInstant(readAt, ZoneId.systemDefault())
    val time = clock.time(at)
    return if (at.toLocalDate() == LocalDate.now()) time else "${DAY_FORMAT.format(at)}, $time"
}
