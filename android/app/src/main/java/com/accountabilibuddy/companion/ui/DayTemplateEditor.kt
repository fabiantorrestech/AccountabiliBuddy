package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Text
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
import com.accountabilibuddy.companion.ui.kit.AbCheckRow
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbChipRow
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.kit.AbValueButton
import com.accountabilibuddy.companion.ui.kit.abTimePickerColors
import com.accountabilibuddy.companion.ui.theme.Ab
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
    /** The smart bedtime's line for this template's nights (ADR 34), shown under its Sleep part; null when it's off. */
    smartNote: String? = null,
) {
    var selected by remember { mutableStateOf<UUID?>(null) }
    if (selected != null && template.parts.none { it.id == selected }) {
        selected = null
    }

    fun update(part: PlanPart) = onChange(template.copy(parts = template.parts.map { if (it.id == part.id) part else it }))

    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        DayPlanBarEditor(template, categories, selected, onSelect = { selected = it }, onChange = onChange, onRefused = onMessage)
        Text(
            "Tap empty space to add a part, tap a part to pick it, drag its edges or middle. A Sleep part that starts before midnight is the night before this day.",
            style = Ab.type.small,
            color = Ab.colors.muted,
        )

        val rows = if (selected != null) template.parts.filter { it.id == selected } else template.parts.sortedBy { if (it.crossesMidnight) -1 else DayTemplateEditing.minutesOf(it.start) }
        for (part in rows) {
            PartRow(
                part, categories, clock, smartNote,
                onChange = ::update,
                onDelete = {
                    onChange(template.copy(parts = template.parts.filter { it.id != part.id }))
                    selected = null
                },
            )
        }

        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            if (selected != null) {
                AbTextButton("Show all parts", onClick = { selected = null })
            }
            AbTextButton("Add a part", onClick = {
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
            })
        }

        DayPlanValidator.validate(template, categories)?.let {
            Text(it, color = Ab.colors.drift, style = Ab.type.bodyMedium)
        }
    }
}

/** One part: its name (optional, else the category's), category, start and end. */
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun PartRow(part: PlanPart, categories: List<DayCategory>, clock: ClockFormat, smartNote: String?, onChange: (PlanPart) -> Unit, onDelete: () -> Unit) {
    var categoryMenu by remember { mutableStateOf(false) }
    var picking by remember { mutableStateOf<String?>(null) }
    val category = categories.firstOrNull { it.id == part.categoryId }
    val isSleep = category?.behavior == CategoryBehavior.Sleep
    // The sleep goal, for a part woken by the alarm (its end is bedtime plus this).
    val sleepGoal = (androidx.compose.ui.platform.LocalContext.current.applicationContext as com.accountabilibuddy.companion.CompanionApp)
        .store.state.value.sleep.goal

    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        AbTextField(
            value = part.label.orEmpty(),
            onValueChange = { onChange(part.copy(label = it.ifBlank { null })) },
            label = "Name (optional, else ${category?.name ?: "its category"})",
        )
        FlowRow(verticalArrangement = Arrangement.spacedBy(2.dp), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Box {
                AbValueButton(category?.name ?: "Category", onClick = { categoryMenu = true })
                DropdownMenu(expanded = categoryMenu, onDismissRequest = { categoryMenu = false }, containerColor = Ab.colors.surface) {
                    for (choice in categories) {
                        DropdownMenuItem(text = { Text(choice.name, style = Ab.type.bodyMedium, color = Ab.colors.ink) }, onClick = {
                            onChange(part.copy(categoryId = choice.id, autoStart = part.autoStart && choice.behavior == CategoryBehavior.Rest))
                            categoryMenu = false
                        })
                    }
                }
            }
            AbValueButton(clock.time(part.start.atDate(LocalDate.now())), onClick = { picking = "start" })
            Row(Modifier.padding(vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) { Text("–", style = Ab.type.bodyMedium, color = Ab.colors.muted) }
            AbValueButton(
                if (isSleep && part.wakeByAlarm) "alarm" else clock.time(part.end.atDate(LocalDate.now())),
                onClick = { picking = "end" }, enabled = !(isSleep && part.wakeByAlarm),
            )
            AbQuietIconButton(Icons.Outlined.Close, "Delete this part", onClick = onDelete)
        }
        // A Sleep part woken by the alarm (ADR 33): only the bedtime is set; the end follows bedtime plus the goal.
        if (isSleep) {
            AbCheckRow(
                "Wake = my alarm (none set: bedtime + sleep goal)", part.wakeByAlarm,
                { on -> onChange(part.copy(wakeByAlarm = on, end = if (on) part.start.plus(sleepGoal) else part.end)) },
            )
        }
        if (isSleep && smartNote != null) {
            Text("Smart bedtime: $smartNote. This bedtime is used while it learns.", style = Ab.type.small, color = Ab.colors.muted)
        }
        // A flexible break (e.g. lunch): the times are its window, it starts when you press Start lunch.
        AbCheckRow(
            "Flexible break", part.isFlexible,
            { on -> onChange(part.copy(flexMinutes = if (on) 60 else null, hintAt = if (on) LocalTime.of(13, 0) else null, autoStart = part.autoStart && !on)) },
        )
        if (part.isFlexible) {
            AbChipRow {
                Text("Lasts", style = Ab.type.bodyMedium, color = Ab.colors.muted)
                listOf(30, 45, 60, 90).forEach { minutes ->
                    AbChip("$minutes min", selected = part.flexMinutes == minutes, onClick = { onChange(part.copy(flexMinutes = minutes)) })
                }
            }
        }
        // A fixed lunch period (schema v13): a Rest part with fixed hours starts lunch by itself at its start, for as long as it lasts.
        if (category?.behavior == CategoryBehavior.Rest && !part.isFlexible && !part.crossesMidnight) {
            AbCheckRow(
                "Starts by itself (lunch starts at its start, or at the first chance after, and ends when this part does)", part.autoStart,
                { on -> onChange(part.copy(autoStart = on)) },
            )
        }
    }

    picking?.let { which ->
        TimeDialog(
            title = if (which == "start") "Starts at" else "Ends at",
            initial = if (which == "start") part.start else part.end,
            is24Hour = clock.is24Hour,
            isStart = which == "start",
            onPick = { time ->
                val edited = when {
                    which != "start" -> part.copy(end = time)
                    isSleep && part.wakeByAlarm -> part.copy(start = time, end = time.plus(sleepGoal))
                    else -> part.copy(start = time)
                }
                // A fixed lunch can't cross midnight: the times that make it do turn "Starts by itself" off.
                onChange(if (edited.crossesMidnight) edited.copy(autoStart = false) else edited)
                picking = null
            },
            onDismiss = { picking = null },
        )
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun TimeDialog(title: String, initial: LocalTime, is24Hour: Boolean, isStart: Boolean, onPick: (LocalTime) -> Unit, onDismiss: () -> Unit) {
    val state = rememberTimePickerState(initial.hour, initial.minute, is24Hour)
    AbDialog(
        title = title,
        onDismiss = onDismiss,
        confirmText = "OK",
        onConfirm = {
            // Snapped to 15 minutes, like the bar; a start stays before midnight (23:53 would round to 00:00).
            val snapped = DayTemplateEditing.snap((state.hour * 60 + state.minute).toDouble())
            onPick(DayTemplateEditing.timeOf(if (isStart) minOf(snapped, 23 * 60 + 45) else snapped))
        },
    ) {
        TimePicker(state, colors = abTimePickerColors())
    }
}
