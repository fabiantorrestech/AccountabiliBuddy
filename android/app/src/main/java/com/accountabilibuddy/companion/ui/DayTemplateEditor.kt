package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.width
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TimePicker
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayPlanValidator
import com.accountabilibuddy.companion.engine.DayTemplateEditing
import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.DayCategory
import com.accountabilibuddy.companion.model.DayTemplate
import com.accountabilibuddy.companion.model.PlanPart
import java.time.LocalDate
import java.time.LocalTime
import java.util.UUID

/**
 * Edits one day template on the phone: the drag bar, and under it the selected part (or all parts) with its name,
 * category and exact times, plus "Add a part". Problems (overlaps, parts under 15 minutes) show under the list.
 */
@Composable
fun DayTemplateEditor(
    template: DayTemplate,
    categories: List<DayCategory>,
    clock: ClockFormat,
    onChange: (DayTemplate) -> Unit,
    onMessage: (String) -> Unit,
) {
    var selected by remember { mutableStateOf<UUID?>(null) }
    if (selected != null && template.parts.none { it.id == selected }) {
        selected = null
    }

    fun update(part: PlanPart) = onChange(DayTemplate(template.parts.map { if (it.id == part.id) part else it }))

    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        DayPlanBarEditor(template, categories, selected, onSelect = { selected = it }, onChange = onChange, onRefused = onMessage)
        Text(
            "Tap empty space to add a part, tap a part to pick it, drag its edges or middle. A Sleep part that starts before midnight is the night before this day.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )

        val rows = if (selected != null) template.parts.filter { it.id == selected } else template.parts.sortedBy { if (it.crossesMidnight) -1 else DayTemplateEditing.minutesOf(it.start) }
        for (part in rows) {
            PartRow(
                part, categories, clock,
                onChange = ::update,
                onDelete = {
                    onChange(DayTemplate(template.parts.filter { it.id != part.id }))
                    selected = null
                },
            )
        }

        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            if (selected != null) {
                TextButton(onClick = { selected = null }) { Text("Show all parts") }
            }
            TextButton(onClick = {
                // The first free quarter hour from 8 AM on (wrapping past midnight).
                val category = categories.firstOrNull { it.behavior == CategoryBehavior.Rest } ?: categories.firstOrNull()
                val added = category?.let { chosen ->
                    (0 until 96).asSequence().map { (8 * 60 + it * 15) % (24 * 60) }.firstNotNullOfOrNull { DayTemplateEditing.addAt(template, it, chosen.id, categories) }
                }
                if (added == null) {
                    onMessage("There's no free time left in this day.")
                } else {
                    onChange(added)
                    selected = added.parts.last().id
                }
            }) { Text("Add a part") }
        }

        DayPlanValidator.validate(template, categories)?.let {
            Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodyMedium)
        }
    }
}

/** One part: its name (optional, else the category's), category, start and end. */
@Composable
private fun PartRow(part: PlanPart, categories: List<DayCategory>, clock: ClockFormat, onChange: (PlanPart) -> Unit, onDelete: () -> Unit) {
    var categoryMenu by remember { mutableStateOf(false) }
    var picking by remember { mutableStateOf<String?>(null) }
    val category = categories.firstOrNull { it.id == part.categoryId }

    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        OutlinedTextField(
            value = part.label.orEmpty(),
            onValueChange = { onChange(part.copy(label = it.ifBlank { null })) },
            label = { Text("Name (optional, else ${category?.name ?: "its category"})") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
        )
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Box {
                OutlinedButton(onClick = { categoryMenu = true }) { Text(category?.name ?: "Category") }
                DropdownMenu(expanded = categoryMenu, onDismissRequest = { categoryMenu = false }) {
                    for (choice in categories) {
                        DropdownMenuItem(text = { Text(choice.name) }, onClick = {
                            onChange(part.copy(categoryId = choice.id))
                            categoryMenu = false
                        })
                    }
                }
            }
            OutlinedButton(onClick = { picking = "start" }) { Text(clock.time(part.start.atDate(LocalDate.now()))) }
            Text("–")
            OutlinedButton(onClick = { picking = "end" }) { Text(clock.time(part.end.atDate(LocalDate.now()))) }
            TextButton(onClick = onDelete, modifier = Modifier.width(48.dp)) { Text("✕") }
        }
        // A flexible break (e.g. lunch): the times are its window, it starts when you press Start lunch.
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            androidx.compose.material3.Checkbox(
                checked = part.isFlexible,
                onCheckedChange = { on -> onChange(part.copy(flexMinutes = if (on) 60 else null, hintAt = if (on) LocalTime.of(13, 0) else null)) },
            )
            Text("Flexible, for", style = androidx.compose.material3.MaterialTheme.typography.bodyMedium)
            listOf(30, 45, 60, 90).forEach { minutes ->
                androidx.compose.material3.FilterChip(
                    selected = part.flexMinutes == minutes, enabled = part.isFlexible,
                    onClick = { onChange(part.copy(flexMinutes = minutes)) }, label = { Text("$minutes") },
                )
            }
        }
    }

    picking?.let { which ->
        TimeDialog(
            title = if (which == "start") "Starts at" else "Ends at",
            initial = if (which == "start") part.start else part.end,
            is24Hour = clock.is24Hour,
            isStart = which == "start",
            onPick = { time ->
                onChange(if (which == "start") part.copy(start = time) else part.copy(end = time))
                picking = null
            },
            onDismiss = { picking = null },
        )
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun TimeDialog(title: String, initial: LocalTime, is24Hour: Boolean, isStart: Boolean, onPick: (LocalTime) -> Unit, onDismiss: () -> Unit) {
    val state = rememberTimePickerState(initial.hour, initial.minute, is24Hour)
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = { TimePicker(state) },
        confirmButton = {
            TextButton(onClick = {
                // Snapped to 15 minutes, like the bar; a start stays before midnight (23:53 would round to 00:00).
                val snapped = DayTemplateEditing.snap((state.hour * 60 + state.minute).toDouble())
                onPick(DayTemplateEditing.timeOf(if (isStart) minOf(snapped, 23 * 60 + 45) else snapped))
            }) { Text("OK") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}
