package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarBuilder
import com.accountabilibuddy.companion.engine.DayBarModel
import com.accountabilibuddy.companion.engine.DayTimeline
import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.model.PendingDayPlan
import com.accountabilibuddy.companion.model.PendingStatus
import com.accountabilibuddy.companion.model.PlannedFocus
import com.accountabilibuddy.companion.model.ProfileDocument
import com.accountabilibuddy.companion.notify.TimelineNotifier
import com.accountabilibuddy.companion.usage.PhoneUsage
import java.time.Duration
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter

/** Today: the bar, what's now and next, the sleep line, last night's measured sleep, and today's blocks and events. */
@Composable
fun TodayScreen(
    state: CompanionState,
    model: DayBarModel,
    clock: ClockFormat,
    health: HealthStatus,
    onImport: () -> Unit,
    onConnectHealth: () -> Unit,
    onRefreshSleep: () -> Unit,
    phoneUsage: PhoneUsage?,
    onAllowUsage: () -> Unit,
    showsPcCard: Boolean = false,
    pcCard: @Composable () -> Unit = {},
    /** Opens the day plan editor (ADR 19); null hides the button. */
    onEditPlan: (() -> Unit)? = null,
    onDiscardPlanEdit: () -> Unit = {},
) {
    val dark = isSystemInDarkTheme()
    val now = LocalDateTime.now()
    val day = model.day
    val tomorrowDate = day.start.toLocalDate().plusDays(1)
    val tomorrow = LocalWindow(maxOf(day.end, tomorrowDate.atStartOfDay()), tomorrowDate.plusDays(1).atStartOfDay())
    // Whole blocks touching the day (not cut to it), so an early block shows its real start. Worked out again only
    // when the data or the day changes, not on every 30-second tick of the bar.
    val todayBlocks = remember(state.document, day) { blocksTouching(state.document, day) }
    val todayEvents = remember(state.events, day) { eventsIn(state.events, day) }
    val nextBlocks = remember(state.document, tomorrow) { blocksTouching(state.document, tomorrow) }
    val nextEvents = remember(state.events, tomorrow) { eventsIn(state.events, tomorrow) }

    LazyColumn(contentPadding = androidx.compose.foundation.layout.PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        item {
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(model.dateText, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                    Spacer(Modifier.width(10.dp))
                    Text(model.statusText, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.weight(1f))
                    onEditPlan?.let { TextButton(onClick = it) { Text("Edit plan") } }
                }
                model.sleepLine?.let { line ->
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Box(Modifier.size(8.dp).background(Color(TimelineNotifier.sleepColor(line.amount, dark)), CircleShape))
                        Spacer(Modifier.width(6.dp))
                        Text(line.text, color = Color(TimelineNotifier.sleepColor(line.amount, dark)), style = MaterialTheme.typography.bodyMedium)
                    }
                }
                DayBar(model)
                state.pendingPlan?.let { PlanEditStatus(it, onEditPlan, onDiscardPlanEdit) }
                if (model.summaryText.isNotEmpty()) {
                    Text(model.summaryText, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }

        if (!state.hasImport) {
            item {
                Card(Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text("Bring your PC's profiles", style = MaterialTheme.typography.titleMedium)
                        Text(
                            "On the PC: Settings → Backup → Export (without secrets, or with a password). Put the .abbackup file on the phone, then import it here.",
                            style = MaterialTheme.typography.bodyMedium,
                        )
                        Button(onClick = onImport) { Text("Import backup file") }
                    }
                }
            }
        }

        if (showsPcCard) {
            item { pcCard() }
        }
        item { SleepCard(state, clock, health, onConnectHealth, onRefreshSleep) }
        item { PhoneCard(phoneUsage, onAllowUsage) }

        if (todayBlocks.isNotEmpty() || todayEvents.isNotEmpty()) {
            item { Text("Today", style = MaterialTheme.typography.titleSmall) }
            todayBlocks.forEach { focus -> item { BlockRow(state, focus, clock, now) } }
            todayEvents.forEach { event -> item { EventRow(event, clock) } }
        }

        // Nothing left today (e.g. late in the evening): what tomorrow holds.
        if (todayBlocks.none { it.window.end.isAfter(now) }) {
            if (nextBlocks.isNotEmpty() || nextEvents.isNotEmpty()) {
                item {
                    Text(
                        "Tomorrow · ${tomorrowDate.format(TOMORROW_FORMAT)}",
                        style = MaterialTheme.typography.titleSmall,
                    )
                }
                nextBlocks.forEach { focus -> item { BlockRow(state, focus, clock, now) } }
                nextEvents.forEach { event -> item { EventRow(event, clock) } }
            }
        }
    }
}

private val TOMORROW_FORMAT: DateTimeFormatter = DateTimeFormatter.ofPattern("EEE M/d")

private fun blocksTouching(document: ProfileDocument, window: LocalWindow): List<PlannedFocus> {
    val around = LocalWindow(window.start.minusDays(1), window.end.plusDays(1))
    return DayTimeline.planned(document.dayPlan, around).filter { DayTimeline.clip(it.window, window) != null }
}

private fun eventsIn(events: List<CalendarEvent>, window: LocalWindow): List<CalendarEvent> =
    events.filter { DayTimeline.clip(LocalWindow(it.localStart, maxOf(it.localEnd, it.localStart.plusMinutes(1))), window) != null }
        .sortedBy { it.localStart }

@Composable
private fun BlockRow(state: CompanionState, focus: PlannedFocus, clock: ClockFormat, now: LocalDateTime) {
    val running = focus.window.contains(now)
    val past = !focus.window.end.isAfter(now)
    val color = if (past) MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.onSurface
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
        Box(Modifier.size(10.dp).background(Color(focus.argb), CircleShape))
        Spacer(Modifier.width(12.dp))
        Column(Modifier.weight(1f)) {
            Text(
                focus.name,
                fontWeight = if (running) FontWeight.SemiBold else FontWeight.Normal,
                color = color,
            )
            Text(
                "${clock.time(focus.window.start)} – ${clock.time(focus.window.end)}",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        if (running) {
            Text("now", color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.labelMedium)
        }
    }
}

@Composable
private fun EventRow(event: CalendarEvent, clock: ClockFormat) {
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
        Box(Modifier.size(width = 10.dp, height = 3.dp).background(Color(0xFF64748B)))
        Spacer(Modifier.width(12.dp))
        Column(Modifier.weight(1f)) {
            Text(event.title)
            Text(
                if (event.isAllDay) "All day" else "${clock.time(event.localStart)} – ${clock.time(event.localEnd)}",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
private fun PhoneCard(usage: PhoneUsage?, onAllow: () -> Unit) {
    Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerHigh)) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text("Phone today", style = MaterialTheme.typography.titleMedium)
            if (usage == null) {
                Text(
                    "See how long you were on the phone, and how much of it was during focus blocks. Needs Usage access (it stays on the phone).",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                OutlinedButton(onClick = onAllow) { Text("Allow usage access") }
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
            Text("Sleep", style = MaterialTheme.typography.titleMedium)
            val night = state.sleep.toSchedule(state.document.dayPlan).nightAround(LocalDateTime.now())
            if (state.sleep.enabled) {
                Text(
                    "Planned: ${clock.time(night.start)} – ${clock.time(night.end)} (${DayBarBuilder.duration(Duration.between(night.start, night.end))})",
                    style = MaterialTheme.typography.bodyMedium,
                )
            } else {
                Text("Sleep mode is off (Settings).", style = MaterialTheme.typography.bodyMedium)
            }

            when (health) {
                HealthStatus.Checking -> Unit
                HealthStatus.NotAvailable -> Text(
                    "Health Connect isn't available on this phone, so measured sleep can't be shown.",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                HealthStatus.NeedsPermission -> {
                    Text(
                        "Show the sleep your watch measured (Pixel Watch, Fitbit) by letting the app read sleep from Health Connect. It stays on the phone.",
                        style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    OutlinedButton(onClick = onConnect) { Text("Connect Health Connect") }
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

/** Where a day plan edit made here stands (ADR 19), with what can be done about it. */
@Composable
private fun PlanEditStatus(pending: PendingDayPlan, onEdit: (() -> Unit)?, onDiscard: () -> Unit) {
    val text = when (pending.status) {
        PendingStatus.Waiting -> "Your plan edit is shown here and goes to the PC when they connect."
        PendingStatus.Locked -> pending.message ?: "It goes to the PC when its focus session ends."
        PendingStatus.Invalid -> "The PC didn't take your plan edit: ${pending.message ?: "it broke a rule"}."
        PendingStatus.NeedsPcUpdate -> pending.message ?: "Update AccountabiliBuddy on the PC to change the plan from here."
    }
    val problem = pending.status == PendingStatus.Invalid || pending.status == PendingStatus.NeedsPcUpdate
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text(
            text,
            style = MaterialTheme.typography.bodySmall,
            color = if (problem) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary,
            modifier = Modifier.weight(1f),
        )
        if (pending.status == PendingStatus.Invalid && onEdit != null) {
            TextButton(onClick = onEdit) { Text("Fix") }
        }
        TextButton(onClick = onDiscard) { Text("Discard") }
    }
}
