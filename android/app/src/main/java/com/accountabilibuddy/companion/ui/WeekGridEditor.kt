package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.SelectableDates
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
import com.accountabilibuddy.companion.model.PlanCycle
import java.time.DayOfWeek
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.time.format.TextStyle
import java.util.Locale
import java.util.UUID

private val StartTitle = DateTimeFormatter.ofPattern("EEE, MMM d, yyyy")

private val WeekColumnWidth = 140.dp

/**
 * Which day type each weekday follows (schema v13, ADR 47): Monday to Sunday, one column per week of a repeating cycle of
 * 1 to 8 weeks (scrolling sideways when they don't fit), buttons to add or remove a week, and the Monday week 1 starts on
 * once there are several. A one-off day still replaces whatever its date follows. [basePlan] is the plan being edited from:
 * removing weeks down to one puts its week 1 start back.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun WeekGridEditor(plan: DayPlan, basePlan: DayPlan, onChange: (DayPlan) -> Unit) {
    val weekCount = plan.cycle.weeks.size
    var pickingStart by remember { mutableStateOf(false) }
    val today = LocalDate.now()

    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(
                if (weekCount > 1) "Repeats every $weekCount weeks" else "The same every week",
                style = MaterialTheme.typography.bodyLarge,
                modifier = Modifier.weight(1f),
            )
            OutlinedButton(onClick = { onChange(DayPlanEditing.withWeekAdded(plan, today)) }, enabled = weekCount < PlanCycle.MAX_WEEKS) { Text("Add week") }
            OutlinedButton(onClick = { onChange(DayPlanEditing.withLastWeekRemoved(plan, basePlan.cycle.startsOn)) }, enabled = weekCount > 1) { Text("Remove last") }
        }
        if (weekCount > 1) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("Week 1 starts", style = MaterialTheme.typography.bodyMedium)
                OutlinedButton(onClick = { pickingStart = true }) { Text(plan.cycle.startsOn.format(StartTitle)) }
            }
            Text(
                "The weeks repeat from that Monday: it is week 1, the next week 2, and so on. \"This is week N of M\" on Today moves this start.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }

        Column(Modifier.horizontalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Text("", Modifier.width(44.dp))
                for (index in 0 until weekCount) {
                    Text(if (weekCount > 1) "Week ${index + 1}" else "Every week", style = MaterialTheme.typography.labelLarge, modifier = Modifier.width(WeekColumnWidth))
                }
            }
            for (day in DayPlan.MONDAY_FIRST) {
                WeekRow(plan, day, onChange)
            }
        }
    }

    if (pickingStart) {
        val state = rememberDatePickerState(
            initialSelectedDateMillis = plan.cycle.startsOn.atStartOfDay().toInstant(ZoneOffset.UTC).toEpochMilli(),
            selectableDates = object : SelectableDates {
                // Week 1 starts on a Monday (the PC's rule).
                override fun isSelectableDate(utcTimeMillis: Long): Boolean =
                    Instant.ofEpochMilli(utcTimeMillis).atZone(ZoneOffset.UTC).dayOfWeek == DayOfWeek.MONDAY
            },
        )
        DatePickerDialog(
            onDismissRequest = { pickingStart = false },
            confirmButton = {
                TextButton(onClick = {
                    pickingStart = false
                    state.selectedDateMillis?.let { onChange(DayPlanEditing.withStartsOn(plan, Instant.ofEpochMilli(it).atZone(ZoneOffset.UTC).toLocalDate())) }
                }) { Text("Week 1 starts here") }
            },
            dismissButton = { TextButton(onClick = { pickingStart = false }) { Text("Cancel") } },
        ) {
            DatePicker(state)
        }
    }
}

@Composable
private fun WeekRow(plan: DayPlan, day: DayOfWeek, onChange: (DayPlan) -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(day.getDisplayName(TextStyle.SHORT, Locale.getDefault()), Modifier.width(44.dp), style = MaterialTheme.typography.bodyMedium)
        for ((index, week) in plan.cycle.weeks.withIndex()) {
            TemplatePicker(plan, DayPlanEditing.templateIdOn(week, day), Modifier.width(WeekColumnWidth)) { onChange(DayPlanEditing.withWeekday(plan, index, day, it)) }
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
