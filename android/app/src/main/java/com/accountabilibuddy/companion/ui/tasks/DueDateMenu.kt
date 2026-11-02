package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CalendarToday
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.MenuDefaults
import androidx.compose.material3.Surface
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
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset

/**
 * The due date of a Later item: a pill ("Due Fri" or "No due date") that opens quick choices (today, tomorrow, next week), a
 * calendar for any other day, and "No due date". [dueOn] and the value handed to [onChange] are local dates as "yyyy-MM-dd".
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DueDateMenu(dueOn: String?, onChange: (String?) -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    val today = LocalDate.now()
    val due = dueOn?.let { runCatching { LocalDate.parse(it) }.getOrNull() }
    var open by remember { mutableStateOf(false) }
    var picking by remember { mutableStateOf(false) }
    Box(modifier) {
        Surface(onClick = { open = true }, shape = Ab.shapes.pill, color = colors.surface2, contentColor = colors.ink) {
            Row(
                Modifier.heightIn(min = 44.dp).padding(horizontal = 14.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                Icon(Icons.Outlined.CalendarToday, contentDescription = null, tint = colors.muted, modifier = Modifier.size(18.dp))
                Text(if (due == null) "No due date" else "Due ${TaskText.dayLabel(due, today)}", style = Ab.type.small, maxLines = 1)
            }
        }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }, containerColor = colors.surface2, shape = Ab.shapes.mediumShape) {
            listOf("Today" to today, "Tomorrow" to today.plusDays(1), "Next week" to today.plusDays(7)).forEach { (label, date) ->
                DropdownMenuItem(
                    text = { Text(label, style = Ab.type.bodyMedium, color = colors.ink) }, colors = MenuDefaults.itemColors(textColor = colors.ink),
                    onClick = { open = false; onChange(date.toString()) },
                )
            }
            DropdownMenuItem(
                text = { Text("Pick a date…", style = Ab.type.bodyMedium, color = colors.ink) }, colors = MenuDefaults.itemColors(textColor = colors.ink),
                onClick = { open = false; picking = true },
            )
            if (due != null) {
                DropdownMenuItem(
                    text = { Text("No due date", style = Ab.type.bodyMedium, color = colors.ink) }, colors = MenuDefaults.itemColors(textColor = colors.ink),
                    onClick = { open = false; onChange(null) },
                )
            }
        }
    }

    if (picking) {
        // The picker counts days as UTC midnights; a local date is converted both ways at that midnight so it never moves a day.
        val state = rememberDatePickerState(initialSelectedDateMillis = (due ?: today).atStartOfDay().toInstant(ZoneOffset.UTC).toEpochMilli())
        DatePickerDialog(
            onDismissRequest = { picking = false },
            confirmButton = {
                TextButton(onClick = {
                    state.selectedDateMillis?.let { millis -> onChange(Instant.ofEpochMilli(millis).atZone(ZoneOffset.UTC).toLocalDate().toString()) }
                    picking = false
                }) { Text("Set", color = colors.ink) }
            },
            dismissButton = { TextButton(onClick = { picking = false }) { Text("Cancel", color = colors.muted) } },
        ) { DatePicker(state = state) }
    }
}
