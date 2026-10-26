package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.sync.BeeminderGoals
import com.accountabilibuddy.shared.sync.SyncBeeminderGoal
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * "Beeminder" (ADR 43): the user's goals as the PC last read them, soonest due first, the same card as the PC's Today.
 * Read only; red when due today.
 */
@Composable
fun BeeminderCard(goals: BeeminderGoals, clock: ClockFormat) {
    val zone = ZoneId.systemDefault()
    val today = LocalDate.now(zone)
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            IconTitle("Beeminder · ${goals.source}", style = MaterialTheme.typography.titleSmall)
            goals.goals.forEach { goal -> GoalRow(goal, LocalDateTime.ofInstant(goal.dueAt, zone), today, clock) }
        }
    }
}

@Composable
private fun GoalRow(goal: SyncBeeminderGoal, due: LocalDateTime, today: LocalDate, clock: ClockFormat) {
    // The safe days are from the PC's last read; a deadline that falls today is due today whatever they said.
    val dueToday = goal.safeDays <= 0 || !due.toLocalDate().isAfter(today)
    val safeText = when {
        dueToday -> "Due today"
        goal.safeDays == 1 -> "1 safe day"
        else -> "${goal.safeDays} safe days"
    }
    val dueText = when {
        due.toLocalDate() == today -> "by ${clock.time(due)}"
        due.toLocalDate() == today.plusDays(1) -> "by tomorrow ${clock.time(due)}"
        due.toLocalDate().isBefore(today) -> "was due ${weekday(due)} ${clock.time(due)}"
        else -> "by ${weekday(due)} ${clock.time(due)}"
    }
    Row(verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(goal.title, style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.SemiBold)
            if (goal.needed.isNotEmpty()) {
                Text(goal.needed, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
        Column(horizontalAlignment = Alignment.End) {
            Text(
                safeText,
                style = MaterialTheme.typography.bodyMedium,
                fontWeight = if (dueToday) FontWeight.SemiBold else null,
                color = if (dueToday) Color(0xFFEF4444) else Color.Unspecified,
                textAlign = TextAlign.End,
            )
            Text(dueText, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

private val weekdayFormat: DateTimeFormatter = DateTimeFormatter.ofPattern("EEE", Locale.getDefault())

private fun weekday(time: LocalDateTime): String = time.format(weekdayFormat)
