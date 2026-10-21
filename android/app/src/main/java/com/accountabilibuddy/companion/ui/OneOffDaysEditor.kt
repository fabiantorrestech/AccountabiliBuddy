package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Button
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.OneOffDay
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.util.UUID

private val DayTitle = DateTimeFormatter.ofPattern("EEE, MMM d, yyyy")

/**
 * Single days planned differently (a day off, a half day), like the PC's Exceptions tab: the list (past ones faded),
 * "Plan a day differently" from that day's usual plan, edit one with the same editor, or go back to the usual plan.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun OneOffDaysEditor(plan: DayPlan, clock: ClockFormat, onChange: (DayPlan) -> Unit, onMessage: (String) -> Unit) {
    // Kept as text so the day being edited survives rotation.
    var editingText by rememberSaveable { mutableStateOf<String?>(null) }
    val editing = editingText?.let(LocalDate::parse)
    var picking by remember { mutableStateOf(false) }
    val today = LocalDate.now()

    fun replace(day: OneOffDay) = onChange(plan.copy(oneOffDays = plan.oneOffDays.map { if (it.id == day.id) day else it }))

    val open = editing?.let { date -> plan.oneOffDays.firstOrNull { it.date == date } }
    if (open != null) {
        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(open.date.format(DayTitle), style = MaterialTheme.typography.titleMedium)
            OutlinedTextField(
                value = open.note.orEmpty(),
                onValueChange = { replace(open.copy(note = it.ifBlank { null })) },
                label = { Text("Note (optional), e.g. Day off") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
            DayTemplateEditor(open.template, plan.categories, clock, onChange = { replace(open.copy(template = it)) }, onMessage = onMessage)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                TextButton(onClick = { editingText = null }) { Text("Done") }
                TextButton(onClick = {
                    onChange(plan.copy(oneOffDays = plan.oneOffDays.filter { it.id != open.id }))
                    editingText = null
                }) { Text("Back to the usual plan") }
            }
        }
        return
    }

    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        if (plan.oneOffDays.isEmpty()) {
            Text("No days planned differently.", color = MaterialTheme.colorScheme.onSurfaceVariant)
        }

        for (day in plan.oneOffDays.sortedBy { it.date }) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                modifier = Modifier.fillMaxWidth().alpha(if (day.date.isBefore(today)) 0.5f else 1f),
            ) {
                Column(Modifier.weight(1f)) {
                    Text(day.date.format(DayTitle))
                    day.note?.let { Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant) }
                }
                TextButton(onClick = { editingText = day.date.toString() }) { Text("Edit") }
                TextButton(onClick = { onChange(plan.copy(oneOffDays = plan.oneOffDays.filter { it.id != day.id })) }) { Text("✕") }
            }
        }

        Button(onClick = { picking = true }) { Text("Plan a day differently") }
    }

    if (picking) {
        val state = rememberDatePickerState(initialSelectedDateMillis = today.atStartOfDay().toInstant(ZoneOffset.UTC).toEpochMilli())
        DatePickerDialog(
            onDismissRequest = { picking = false },
            confirmButton = {
                TextButton(onClick = {
                    picking = false
                    val millis = state.selectedDateMillis ?: return@TextButton
                    val date = Instant.ofEpochMilli(millis).atZone(ZoneOffset.UTC).toLocalDate()
                    if (plan.oneOffDays.none { it.date == date }) {
                        // Starts from that day's usual plan.
                        onChange(plan.copy(oneOffDays = plan.oneOffDays + OneOffDay(UUID.randomUUID(), date, plan.templateFor(date))))
                    }
                    editingText = date.toString()
                }) { Text("Plan this day") }
            },
            dismissButton = { TextButton(onClick = { picking = false }) { Text("Cancel") } },
        ) {
            DatePicker(state)
        }
    }
}
