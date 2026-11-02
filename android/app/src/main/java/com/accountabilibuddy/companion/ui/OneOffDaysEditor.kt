package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Text
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.DayTemplate
import com.accountabilibuddy.companion.model.OneOffDay
import com.accountabilibuddy.companion.ui.kit.AbInsetRow
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.kit.abDatePickerColors
import com.accountabilibuddy.companion.ui.theme.Ab
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
            Text(open.date.format(DayTitle), style = Ab.type.section, color = Ab.colors.ink)
            AbTextField(
                value = open.note.orEmpty(),
                onValueChange = { replace(open.copy(note = it.ifBlank { null })) },
                label = "Note (optional), e.g. Day off",
            )
            TemplateModeEditor(open.template, plan.categories, clock, onChange = { replace(open.copy(template = it)) }, onMessage = onMessage)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                AbTextButton("Done", onClick = { editingText = null })
                AbTextButton("Back to the usual plan", onClick = {
                    onChange(plan.copy(oneOffDays = plan.oneOffDays.filter { it.id != open.id }))
                    editingText = null
                })
            }
        }
        return
    }

    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        if (plan.oneOffDays.isEmpty()) {
            Text("No days planned differently.", style = Ab.type.bodyMedium, color = Ab.colors.muted)
        }

        for (day in plan.oneOffDays.sortedBy { it.date }) {
            AbInsetRow(
                modifier = Modifier.alpha(if (day.date.isBefore(today)) 0.5f else 1f),
                onClick = { editingText = day.date.toString() },
            ) {
                Column(Modifier.weight(1f)) {
                    Text(day.date.format(DayTitle), style = Ab.type.bodyMedium, fontWeight = FontWeight.Medium, color = Ab.colors.ink)
                    day.note?.let { Text(it, style = Ab.type.small, color = Ab.colors.muted) }
                }
                Text("Edit", style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold, color = Ab.colors.ink)
                AbQuietIconButton(Icons.Outlined.Close, "Remove ${day.date.format(DayTitle)}", onClick = {
                    onChange(plan.copy(oneOffDays = plan.oneOffDays.filter { it.id != day.id }))
                })
            }
        }

        AbTonalButton("Plan a day differently", onClick = { picking = true }, modifier = Modifier.fillMaxWidth())
    }

    if (picking) {
        val state = rememberDatePickerState(initialSelectedDateMillis = today.atStartOfDay().toInstant(ZoneOffset.UTC).toEpochMilli())
        DatePickerDialog(
            onDismissRequest = { picking = false },
            confirmButton = {
                AbTextButton("Plan this day", onClick = {
                    picking = false
                    val millis = state.selectedDateMillis ?: return@AbTextButton
                    val date = Instant.ofEpochMilli(millis).atZone(ZoneOffset.UTC).toLocalDate()
                    if (plan.oneOffDays.none { it.date == date }) {
                        // Starts from that day's usual plan (a work window comes as the window, not also as its stand-in part).
                        val usual = plan.templateFor(date)
                        val start = if (usual.workWindow == null) usual else usual.copy(parts = usual.parts.filter { !DayTemplate.isWorkWindowPartId(it.id) })
                        onChange(plan.copy(oneOffDays = plan.oneOffDays + OneOffDay(UUID.randomUUID(), date, start)))
                    }
                    editingText = date.toString()
                })
            },
            dismissButton = { AbTextButton("Cancel", onClick = { picking = false }) },
            colors = abDatePickerColors(),
        ) {
            DatePicker(state, colors = abDatePickerColors())
        }
    }
}
