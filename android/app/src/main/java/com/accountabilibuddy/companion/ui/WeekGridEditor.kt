package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.width
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.SelectableDates
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.DayPlanEditing
import com.accountabilibuddy.companion.model.DayPlan
import java.time.DayOfWeek
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.time.format.TextStyle
import java.util.Locale
import java.util.UUID

private val StartTitle = DateTimeFormatter.ofPattern("EEE, MMM d, yyyy")

/**
 * Which day type each weekday follows (schema v12, ADR 47): Monday to Sunday, week A in one column and, with "Alternate
 * weeks" on, week B in a second, plus the Monday week B starts on. A one-off day still replaces whatever its date follows.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun WeekGridEditor(plan: DayPlan, onChange: (DayPlan) -> Unit) {
    val alternating = plan.week.b != null
    var pickingStart by remember { mutableStateOf(false) }
    val today = LocalDate.now()

    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Alternate weeks (A / B)", style = MaterialTheme.typography.bodyLarge, modifier = Modifier.weight(1f))
            Switch(checked = alternating, onCheckedChange = { onChange(DayPlanEditing.withAlternating(plan, it, today)) })
        }
        if (alternating) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("Week B starts", style = MaterialTheme.typography.bodyMedium)
                OutlinedButton(onClick = { pickingStart = true }) { Text(plan.week.bStartsOn?.format(StartTitle) ?: "Pick a Monday") }
            }
            Text(
                "Weeks alternate from that Monday: it is week B, the next is A, and so on. \"This week is A/B\" on Today moves this start.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }

        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Text("", Modifier.width(44.dp))
            Text(if (alternating) "Week A" else "Every week", style = MaterialTheme.typography.labelLarge, modifier = Modifier.weight(1f))
            if (alternating) Text("Week B", style = MaterialTheme.typography.labelLarge, modifier = Modifier.weight(1f))
        }
        for (day in DayPlan.MONDAY_FIRST) {
            WeekRow(plan, day, onChange)
        }
    }

    if (pickingStart) {
        val start = plan.week.bStartsOn ?: today
        val state = rememberDatePickerState(
            initialSelectedDateMillis = start.atStartOfDay().toInstant(ZoneOffset.UTC).toEpochMilli(),
            selectableDates = object : SelectableDates {
                // Week B starts on a Monday (the PC's rule).
                override fun isSelectableDate(utcTimeMillis: Long): Boolean =
                    Instant.ofEpochMilli(utcTimeMillis).atZone(ZoneOffset.UTC).dayOfWeek == DayOfWeek.MONDAY
            },
        )
        DatePickerDialog(
            onDismissRequest = { pickingStart = false },
            confirmButton = {
                TextButton(onClick = {
                    pickingStart = false
                    state.selectedDateMillis?.let { onChange(DayPlanEditing.withBStartsOn(plan, Instant.ofEpochMilli(it).atZone(ZoneOffset.UTC).toLocalDate())) }
                }) { Text("Week B starts here") }
            },
            dismissButton = { TextButton(onClick = { pickingStart = false }) { Text("Cancel") } },
        ) {
            DatePicker(state)
        }
    }
}

@Composable
private fun WeekRow(plan: DayPlan, day: DayOfWeek, onChange: (DayPlan) -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp), modifier = Modifier.fillMaxWidth()) {
        Text(day.getDisplayName(TextStyle.SHORT, Locale.getDefault()), Modifier.width(44.dp), style = MaterialTheme.typography.bodyMedium)
        TemplatePicker(plan, DayPlanEditing.templateIdOn(plan.week.a, day), Modifier.weight(1f)) { onChange(DayPlanEditing.withWeekday(plan, false, day, it)) }
        plan.week.b?.let { b ->
            TemplatePicker(plan, DayPlanEditing.templateIdOn(b, day), Modifier.weight(1f)) { onChange(DayPlanEditing.withWeekday(plan, true, day, it)) }
        }
    }
}

/** A button naming the chosen day type that opens the list of them. */
@Composable
private fun TemplatePicker(plan: DayPlan, selected: UUID, modifier: Modifier, onPick: (UUID) -> Unit) {
    var open by remember { mutableStateOf(false) }
    Box(modifier) {
        OutlinedButton(onClick = { open = true }, modifier = Modifier.fillMaxWidth()) {
            Text(plan.templates.firstOrNull { it.id == selected }?.name ?: "Off day", maxLines = 1)
        }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            for (named in plan.templates) {
                DropdownMenuItem(text = { Text(named.name) }, onClick = {
                    open = false
                    onPick(named.id)
                })
            }
        }
    }
}
