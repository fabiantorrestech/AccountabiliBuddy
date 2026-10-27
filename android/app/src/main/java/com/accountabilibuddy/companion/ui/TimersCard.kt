package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.Edit
import androidx.compose.material.icons.outlined.Pause
import androidx.compose.material.icons.outlined.PlayArrow
import androidx.compose.material.icons.outlined.Replay
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.timers.AlarmItem
import com.accountabilibuddy.companion.timers.TimerItem
import com.accountabilibuddy.companion.timers.TimerState
import com.accountabilibuddy.companion.timers.TimersStore
import java.time.DayOfWeek
import java.time.Instant
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.ZoneId
import java.time.format.TextStyle
import java.util.Locale

/**
 * Today's timers and alarms (the phone's own, V1): quick timers (5/10/15/25 min) or one named and timed by hand,
 * each with its time left, a bar, pause/resume, +1 min, reset and edit (delete in the edit dialog); alarms with their
 * time (12-hour), days, on/off and edit.
 */
@Composable
fun TimersCard(store: TimersStore, clock: ClockFormat) {
    val timers by store.timers.collectAsStateWithLifecycle()
    val alarms by store.alarms.collectAsStateWithLifecycle()
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    val ticking = timers.any { it.state == TimerState.RUNNING }
    LaunchedEffect(ticking) {
        while (ticking) {
            now = System.currentTimeMillis()
            kotlinx.coroutines.delay(1_000)
        }
    }
    var editingTimer by remember { mutableStateOf<TimerItem?>(null) }
    var addingTimer by remember { mutableStateOf(false) }
    var editingAlarm by remember { mutableStateOf<AlarmItem?>(null) }
    var addingAlarm by remember { mutableStateOf(false) }

    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            IconTitle("Timers", style = MaterialTheme.typography.titleMedium)
            QuickTimers(onPreset = { minutes -> store.addTimer(TimerItem.DEFAULT_NAME, minutes * 60_000L) }, onCustom = { addingTimer = true })
            timers.forEach { timer -> TimerRow(timer, now, store, onEdit = { editingTimer = timer }) }

            IconTitle("Alarms", style = MaterialTheme.typography.titleSmall, modifier = Modifier.padding(top = 8.dp))
            alarms.forEach { alarm -> AlarmRow(alarm, clock, store, onEdit = { editingAlarm = alarm }) }
            TextButton(onClick = { addingAlarm = true }) { Text("Add alarm") }
        }
    }

    if (addingTimer) {
        TimerDialog(title = "New timer", initialName = "", initialMs = 10 * 60_000L, onDismiss = { addingTimer = false }) { name, ms ->
            store.addTimer(name, ms)
            addingTimer = false
        }
    }
    editingTimer?.let { timer ->
        TimerDialog(
            title = "Edit timer", initialName = timer.name, initialMs = timer.lengthMs, onDismiss = { editingTimer = null },
            onDelete = { store.deleteTimer(timer.id); editingTimer = null },
        ) { name, ms ->
            store.editTimer(timer.id, name, ms)
            editingTimer = null
        }
    }
    if (addingAlarm) {
        AlarmDialog(title = "New alarm", initialName = "", initialTime = LocalTime.of(7, 0), initialDays = emptySet(), onDismiss = { addingAlarm = false }) { name, time, days ->
            store.addAlarm(name, time, days)
            addingAlarm = false
        }
    }
    editingAlarm?.let { alarm ->
        AlarmDialog(
            title = "Edit alarm", initialName = alarm.name, initialTime = alarm.time, initialDays = alarm.days, onDismiss = { editingAlarm = null },
            onDelete = { store.deleteAlarm(alarm.id); editingAlarm = null },
        ) { name, time, days ->
            store.editAlarm(alarm.id, name, time, days)
            editingAlarm = null
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun QuickTimers(onPreset: (Int) -> Unit, onCustom: () -> Unit) {
    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        TimersStore.PRESETS.forEach { minutes ->
            OutlinedButton(onClick = { onPreset(minutes) }) { Text("$minutes min") }
        }
        OutlinedButton(onClick = onCustom) { Text("Custom…") }
    }
}

@Composable
private fun TimerRow(timer: TimerItem, now: Long, store: TimersStore, onEdit: () -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(timer.name, style = MaterialTheme.typography.bodyLarge)
                val status = when (timer.state) {
                    TimerState.RUNNING -> "${TimerItem.formatLeft(timer.leftMs(now))} left"
                    TimerState.PAUSED -> "Paused · ${TimerItem.formatLeft(timer.leftMs(now))} left"
                    TimerState.IDLE -> TimerItem.formatLength(timer.lengthMs)
                    TimerState.DONE -> "Time's up"
                }
                Text(status, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            when (timer.state) {
                TimerState.RUNNING -> IconButton(onClick = { store.pause(timer.id) }) { Icon(Icons.Outlined.Pause, contentDescription = "Pause") }
                TimerState.PAUSED -> IconButton(onClick = { store.resume(timer.id) }) { Icon(Icons.Outlined.PlayArrow, contentDescription = "Resume") }
                TimerState.IDLE -> IconButton(onClick = { store.start(timer.id) }) { Icon(Icons.Outlined.PlayArrow, contentDescription = "Start") }
                TimerState.DONE -> TextButton(onClick = { store.reset(timer.id) }) { Text("Dismiss") }
            }
            if (timer.state != TimerState.IDLE) {
                TextButton(onClick = { store.addMinute(timer.id) }) { Text("+1 min") }
            }
            if (timer.state == TimerState.RUNNING || timer.state == TimerState.PAUSED) {
                IconButton(onClick = { store.reset(timer.id) }) { Icon(Icons.Outlined.Replay, contentDescription = "Reset") }
            }
            IconButton(onClick = onEdit) { Icon(Icons.Outlined.Edit, contentDescription = "Edit") }
        }
        if (timer.state != TimerState.IDLE) {
            LinearProgressIndicator(progress = { timer.progress(now) }, modifier = Modifier.fillMaxWidth())
        }
    }
}

@Composable
private fun AlarmRow(alarm: AlarmItem, clock: ClockFormat, store: TimersStore, onEdit: () -> Unit) {
    val zone = ZoneId.systemDefault()
    fun at(ms: Long) = LocalDateTime.ofInstant(Instant.ofEpochMilli(ms), zone)
    Row(verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text("${clock.time(ANY_DATE.atTime(alarm.time))} · ${alarm.name}", style = MaterialTheme.typography.bodyLarge)
            val next = alarm.snoozeUntilMs?.let { "Snoozed until ${clock.time(at(it))}" }
                ?: alarm.nextMs?.takeIf { alarm.on }?.let { "Next ${at(it).dayOfWeek.getDisplayName(TextStyle.SHORT, Locale.getDefault())} ${clock.time(at(it))}" }
                ?: "Off"
            Text("${daysText(alarm.days)} · $next", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Switch(checked = alarm.on, onCheckedChange = { store.setAlarmOn(alarm.id, it) })
        IconButton(onClick = onEdit) { Icon(Icons.Outlined.Edit, contentDescription = "Edit") }
    }
}

// Any date: only the time of day is shown.
private val ANY_DATE = java.time.LocalDate.of(2000, 1, 1)

private val WEEK = listOf(DayOfWeek.MONDAY, DayOfWeek.TUESDAY, DayOfWeek.WEDNESDAY, DayOfWeek.THURSDAY, DayOfWeek.FRIDAY, DayOfWeek.SATURDAY, DayOfWeek.SUNDAY)
private val WEEKDAYS = WEEK.take(5).toSet()

private fun daysText(days: Set<DayOfWeek>): String = when {
    days.isEmpty() -> "Once"
    days.size == 7 -> "Every day"
    days == WEEKDAYS -> "Weekdays"
    days == setOf(DayOfWeek.SATURDAY, DayOfWeek.SUNDAY) -> "Weekends"
    else -> WEEK.filter { it in days }.joinToString(", ") { it.getDisplayName(TextStyle.SHORT, Locale.getDefault()) }
}

/** A timer's name and length: hours (0-23) and minutes (0-59) on wheels; at least a minute. */
@Composable
private fun TimerDialog(
    title: String,
    initialName: String,
    initialMs: Long,
    onDismiss: () -> Unit,
    onDelete: (() -> Unit)? = null,
    onSave: (String, Long) -> Unit,
) {
    var name by remember { mutableStateOf(initialName) }
    val initialMinutes = (initialMs / 60_000).toInt().coerceAtLeast(1)
    var hours by remember { mutableStateOf((initialMinutes / 60).coerceIn(0, 23)) }
    var minutes by remember { mutableStateOf(initialMinutes % 60) }
    val total = hours * 60 + minutes
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                OutlinedTextField(name, { name = it }, label = { Text("Name") }, placeholder = { Text(TimerItem.DEFAULT_NAME) }, singleLine = true)
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    WheelPicker((0..23).map { it.toString() }, hours, { hours = it }, width = 56.dp)
                    Text("h", style = MaterialTheme.typography.titleMedium)
                    WheelPicker((0..59).map { "%02d".format(it) }, minutes, { minutes = it }, width = 56.dp)
                    Text("min", style = MaterialTheme.typography.titleMedium)
                }
            }
        },
        confirmButton = { TextButton(onClick = { onSave(name, total * 60_000L) }, enabled = total > 0) { Text("Save") } },
        dismissButton = {
            Row {
                onDelete?.let { TextButton(onClick = it) { Icon(Icons.Outlined.Delete, contentDescription = null); Text("Delete") } }
                TextButton(onClick = onDismiss) { Text("Cancel") }
            }
        },
    )
}

/** An alarm's name, time (12-hour wheels, every minute) and days (none = once). */
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun AlarmDialog(
    title: String,
    initialName: String,
    initialTime: LocalTime,
    initialDays: Set<DayOfWeek>,
    onDismiss: () -> Unit,
    onDelete: (() -> Unit)? = null,
    onSave: (String, LocalTime, Set<DayOfWeek>) -> Unit,
) {
    var name by remember { mutableStateOf(initialName) }
    var time by remember { mutableStateOf(initialTime) }
    var days by remember { mutableStateOf(initialDays) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                OutlinedTextField(name, { name = it }, label = { Text("Name") }, placeholder = { Text(AlarmItem.DEFAULT_NAME) }, singleLine = true)
                TimeWheel(time, { time = it }, minuteStep = 1)
                FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    WEEK.forEach { day ->
                        FilterChip(
                            selected = day in days,
                            onClick = { days = if (day in days) days - day else days + day },
                            label = { Text(day.getDisplayName(TextStyle.SHORT, Locale.getDefault())) },
                        )
                    }
                }
                Text(
                    if (days.isEmpty()) "No days: rings once." else daysText(days),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        },
        confirmButton = { TextButton(onClick = { onSave(name, time, days) }) { Text("Save") } },
        dismissButton = {
            Row {
                onDelete?.let { TextButton(onClick = it) { Icon(Icons.Outlined.Delete, contentDescription = null); Text("Delete") } }
                TextButton(onClick = onDismiss) { Text("Cancel") }
            }
        },
    )
}
