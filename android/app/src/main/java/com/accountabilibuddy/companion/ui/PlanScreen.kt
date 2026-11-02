package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.MaterialTheme
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
import com.accountabilibuddy.companion.engine.DayBarModel
import com.accountabilibuddy.companion.engine.DayTimeline
import com.accountabilibuddy.companion.engine.DayWorkWindows
import com.accountabilibuddy.companion.engine.LegendEntry
import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.model.PendingDayPlan
import com.accountabilibuddy.companion.model.PendingStatus
import com.accountabilibuddy.companion.model.PlannedFocus
import com.accountabilibuddy.companion.model.ProfileDocument
import com.accountabilibuddy.companion.notify.TimelineNotifier
import com.accountabilibuddy.companion.ui.kit.AbPageHeader
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter

/**
 * Plan: the day bar with its sleep line and legend, today's blocks and events (tomorrow's late in the evening), the week
 * of the cycle, and the buttons that edit the plan or plan days ahead. Moved off Today by the five-tab shell.
 */
@Composable
fun PlanScreen(
    state: CompanionState,
    model: DayBarModel,
    clock: ClockFormat,
    /** Opens the day plan editor (ADR 19); null hides the button. */
    onEditPlan: (() -> Unit)? = null,
    onDiscardPlanEdit: () -> Unit = {},
    /** Opens "Plan a day" (ADR 37); null hides the button. */
    onPlanDay: (() -> Unit)? = null,
    /** "This is week N of M" (ADR 47): picks the week of the cycle as a plan edit; null hides the card. */
    onSetWeek: ((week: Int) -> Unit)? = null,
) {
    val dark = isSystemInDarkTheme()
    val now = LocalDateTime.now()
    val day = model.day
    val tomorrowDate = day.start.toLocalDate().plusDays(1)
    val tomorrow = LocalWindow(maxOf(day.end, tomorrowDate.atStartOfDay()), tomorrowDate.plusDays(1).atStartOfDay())
    // Whole blocks touching the day (not cut to it), so an early block shows its real start. Worked out again only
    // when the data or the day changes, not on every 30-second tick of the bar.
    val todayBlocks = remember(state.document, day) { blocksTouching(state.document, state.sleep, day) }
    val todayEvents = remember(state.events, day) { eventsIn(state.events, day) }
    val nextBlocks = remember(state.document, tomorrow) { blocksTouching(state.document, state.sleep, tomorrow) }
    val nextEvents = remember(state.events, tomorrow) { eventsIn(state.events, tomorrow) }

    LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        item {
            AbPageHeader("Plan") {
                // Ink, not the accent: the accent is for progress and the one main button.
                val ink = androidx.compose.material3.ButtonDefaults.textButtonColors(contentColor = Ab.colors.ink)
                onPlanDay?.let { TextButton(onClick = it, colors = ink) { Text("Plan day(s)") } }
                onEditPlan?.let { TextButton(onClick = it, colors = ink) { Text("Edit plan") } }
            }
        }
        item {
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(model.dateText, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                model.sleepLine?.let { line ->
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Box(Modifier.size(8.dp).background(Color(TimelineNotifier.sleepColor(line.amount, dark)), CircleShape))
                        Spacer(Modifier.width(6.dp))
                        Text(line.text, color = Color(TimelineNotifier.sleepColor(line.amount, dark)), style = MaterialTheme.typography.bodyMedium)
                    }
                }
                DayBar(model)
                FocusLegend(model.focusLegend)
                state.pendingPlan?.let { PlanEditStatus(it, onEditPlan, onDiscardPlanEdit) }
                if (model.summaryText.isNotEmpty()) {
                    Text(model.summaryText, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }

        if (onSetWeek != null && state.document.dayPlan.cycle.weeks.size > 1) {
            item { ThisWeekCard(state.document.dayPlan.cycle.weekNumberFor(now.toLocalDate()), state.document.dayPlan.cycle.weeks.size, onSetWeek) }
        }

        if (todayBlocks.isNotEmpty() || todayEvents.isNotEmpty()) {
            item { IconTitle("Today", style = MaterialTheme.typography.titleSmall) }
            todayBlocks.forEach { focus -> item { BlockRow(focus, clock, now) } }
            todayEvents.forEach { event -> item { EventRow(event, clock) } }
        }

        // Nothing left today (e.g. late in the evening): what tomorrow holds.
        if (todayBlocks.none { it.window.end.isAfter(now) }) {
            if (nextBlocks.isNotEmpty() || nextEvents.isNotEmpty()) {
                item {
                    IconTitle(
                        "Tomorrow · ${tomorrowDate.format(TOMORROW_FORMAT)}",
                        style = MaterialTheme.typography.titleSmall,
                    )
                }
                nextBlocks.forEach { focus -> item { BlockRow(focus, clock, now) } }
                nextEvents.forEach { event -> item { EventRow(event, clock) } }
            }
        }
    }
}

private val TOMORROW_FORMAT: DateTimeFormatter = DateTimeFormatter.ofPattern("EEE M/d")

private fun blocksTouching(document: ProfileDocument, sleep: com.accountabilibuddy.companion.model.SleepSettings, window: LocalWindow): List<PlannedFocus> {
    val around = LocalWindow(window.start.minusDays(1), window.end.plusDays(1))
    return DayWorkWindows.workWindows(document.dayPlan, around, LocalDateTime.now(), nightFallback = sleep::fallbackNightBefore).filter { DayTimeline.clip(it.window, window) != null }
}

private fun eventsIn(events: List<CalendarEvent>, window: LocalWindow): List<CalendarEvent> =
    events.filter { DayTimeline.clip(LocalWindow(it.localStart, maxOf(it.localEnd, it.localStart.plusMinutes(1))), window) != null }
        .sortedBy { it.localStart }

@Composable
private fun BlockRow(focus: PlannedFocus, clock: ClockFormat, now: LocalDateTime) {
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

/** Where a day plan edit made here stands (ADR 19), with what can be done about it. */
@Composable
private fun PlanEditStatus(pending: PendingDayPlan, onEdit: (() -> Unit)?, onDiscard: () -> Unit) {
    val text = when (pending.status) {
        PendingStatus.Waiting -> "Your plan edit is shown here and goes to the PC when they connect or sync."
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

/** Under the bar, like the PC's legend: each total with a dot in its colour on the bar. */
@OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
@Composable
internal fun FocusLegend(entries: List<LegendEntry>) {
    if (entries.isEmpty()) {
        return
    }

    androidx.compose.foundation.layout.FlowRow(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
        for (entry in entries) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.size(8.dp).background(Color(entry.color), CircleShape))
                Spacer(Modifier.width(5.dp))
                Text(entry.text, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}
