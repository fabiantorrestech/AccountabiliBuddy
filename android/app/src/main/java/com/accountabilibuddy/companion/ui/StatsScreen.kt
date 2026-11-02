package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarBuilder
import com.accountabilibuddy.companion.ui.kit.AbPageHeader
import com.accountabilibuddy.companion.usage.PhoneUsage
import java.time.Duration
import java.time.LocalDateTime

/**
 * Stats: for now the two cards that told the day's numbers on Today, last night's sleep and the time on the phone (the real
 * Stats views come in a later part).
 */
@Composable
fun StatsScreen(
    state: CompanionState,
    clock: ClockFormat,
    health: HealthStatus,
    onConnectHealth: () -> Unit,
    onRefreshSleep: () -> Unit,
    phoneUsage: PhoneUsage?,
    onAllowUsage: () -> Unit,
) {
    LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        item { AbPageHeader("Stats") }
        item { SleepCard(state, clock, health, onConnectHealth, onRefreshSleep) }
        item { PhoneCard(phoneUsage, onAllowUsage) }
    }
}

@Composable
private fun PhoneCard(usage: PhoneUsage?, onAllow: () -> Unit) {
    Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerHigh)) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            IconTitle("Phone today", style = MaterialTheme.typography.titleMedium)
            if (usage == null) {
                Text("Time on the phone, and during focus.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                OutlinedButton(onClick = onAllow) { Text("Allow usage access") }
                Hint("Needs Usage access. What it reads stays on the phone.", label = "Why the permission")
                return@Column
            }

            Row {
                Text("${DayBarBuilder.duration(usage.total)} on the phone", style = MaterialTheme.typography.bodyLarge)
                if (!usage.inFocus.isZero) {
                    Text(
                        " · ${DayBarBuilder.duration(usage.inFocus)} during focus",
                        style = MaterialTheme.typography.bodyLarge,
                        color = Color(0xFFF87171),
                    )
                }
            }
            usage.apps.take(6).forEach { app ->
                Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                    Text(app.label, modifier = Modifier.weight(1f), maxLines = 1)
                    if (!app.inFocus.isZero) {
                        Text(
                            "${DayBarBuilder.duration(app.inFocus)} in focus  ",
                            style = MaterialTheme.typography.bodySmall,
                            color = Color(0xFFF87171),
                        )
                    }
                    Text(DayBarBuilder.duration(app.total), style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
            if (usage.apps.isEmpty()) {
                Text("Nothing yet today.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}

@Composable
private fun SleepCard(state: CompanionState, clock: ClockFormat, health: HealthStatus, onConnect: () -> Unit, onRefresh: () -> Unit) {
    Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerHigh)) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            IconTitle("Sleep", style = MaterialTheme.typography.titleMedium)
            val night = state.sleep.toSchedule(state.document.dayPlan).nightAround(LocalDateTime.now())
            if (state.sleep.enabled) {
                Text(
                    "Planned: ${clock.time(night.start)} – ${clock.time(night.end)} (${DayBarBuilder.duration(Duration.between(night.start, night.end))})",
                    style = MaterialTheme.typography.bodyMedium,
                )
            } else {
                Text("Sleep mode is off (Settings › Sleep).", style = MaterialTheme.typography.bodyMedium)
            }

            when (health) {
                HealthStatus.Checking -> Unit
                HealthStatus.NotAvailable -> Text(
                    "No Health Connect: measured sleep can't show.",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                HealthStatus.NeedsPermission -> {
                    OutlinedButton(onClick = onConnect) { Text("Show measured sleep") }
                    Hint("The sleep your watch measured (Pixel Watch, Fitbit), read from Health Connect. It stays on the phone.", label = "What this reads")
                }
                HealthStatus.Connected -> {
                    val last = state.slept.lastOrNull()
                    if (last == null) {
                        Text("No sleep recorded in the last two days.", style = MaterialTheme.typography.bodySmall)
                    } else {
                        val total = Duration.between(last.start, last.end)
                        Text(
                            "Last sleep: ${DayBarBuilder.duration(total)} (${clock.time(last.start)} – ${clock.time(last.end)})",
                            style = MaterialTheme.typography.bodyMedium,
                        )
                    }
                    OutlinedButton(onClick = onRefresh) { Text("Check again") }
                }
            }
        }
    }
}
