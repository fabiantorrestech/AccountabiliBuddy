package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayPlanEditing
import com.accountabilibuddy.companion.model.DayCategory
import com.accountabilibuddy.companion.model.DayMode
import com.accountabilibuddy.companion.model.DayTemplate
import com.accountabilibuddy.companion.model.WorkWindow
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbChipRow
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbValueButton
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.LocalDate

private const val BUDGET_STEP_MINUTES = 30
private const val MIN_BUDGET_MINUTES = 15
private const val MAX_BUDGET_MINUTES = 24 * 60

/**
 * One day template with its mode (schema v12, ADR 47), the same editor for a day type and a one-off day:
 * - Schedule: "Just a work window" (two times, the other parts optional underneath) or the full timeline;
 * - Budget: hours of focus, and the non-productive windows (rest, sleep, pre-work) the guard keeps out of the day;
 * - Free: nothing guarded.
 * Switching to something that drops work parts asks first.
 */
@Composable
fun TemplateModeEditor(
    template: DayTemplate,
    categories: List<DayCategory>,
    clock: ClockFormat,
    onChange: (DayTemplate) -> Unit,
    onMessage: (String) -> Unit,
    smartNote: String? = null,
) {
    // The change waiting for the user's yes because it drops work parts: the new template and what it drops.
    var confirming by remember { mutableStateOf<Pair<DayTemplate, String>?>(null) }
    val workParts = DayPlanEditing.workPartCount(template, categories)

    fun change(next: DayTemplate, dropsWhat: String?) {
        if (dropsWhat != null && workParts > 0) confirming = next to dropsWhat else onChange(next)
    }

    confirming?.let { (next, what) ->
        AbDialog(
            title = "Replace the work parts?",
            onDismiss = { confirming = null },
            confirmText = "Replace",
            onConfirm = { confirming = null; onChange(next) },
            dismissText = "Keep them",
        ) {
            Text("This removes its $workParts work part${if (workParts == 1) "" else "s"}: $what", style = Ab.type.bodyMedium, color = Ab.colors.muted)
        }
    }

    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        AbChipRow {
            for (mode in DayMode.entries) {
                AbChip(
                    mode.name,
                    selected = template.mode == mode,
                    onClick = {
                        if (mode == template.mode) return@AbChip
                        change(
                            DayPlanEditing.withMode(template, mode, categories),
                            if (mode == DayMode.Budget) "a Budget day only keeps rest, sleep and pre-work windows." else null,
                        )
                    },
                )
            }
        }

        when (template.mode) {
            DayMode.Schedule -> ScheduleEditor(template, categories, clock, onChange, ::change, onMessage, smartNote)
            DayMode.Budget -> BudgetEditor(template, categories, clock, onChange, onMessage, smartNote)
            DayMode.Free -> Text(
                "Free day: the buddy doesn't guard it. Sleep and other parts still shape the night.",
                style = Ab.type.small,
                color = Ab.colors.muted,
            )
        }
    }
}

@Composable
private fun ScheduleEditor(
    template: DayTemplate,
    categories: List<DayCategory>,
    clock: ClockFormat,
    onChange: (DayTemplate) -> Unit,
    change: (DayTemplate, String?) -> Unit,
    onMessage: (String) -> Unit,
    smartNote: String?,
) {
    val window = template.workWindow
    AbChipRow {
        AbChip(
            "Just a work window",
            selected = window != null,
            onClick = {
                if (window == null) change(DayPlanEditing.asWorkWindow(template, categories), "they become one work window.")
            },
        )
        AbChip(
            "Full timeline",
            selected = window == null,
            onClick = { if (window != null) onChange(DayPlanEditing.asFullTimeline(template, categories)) },
        )
    }

    if (window == null) {
        DayTemplateEditor(template, categories, clock, onChange, onMessage, smartNote)
        return
    }

    WorkWindowRow(window, clock) { onChange(template.copy(workWindow = it)) }
    Text(
        "The buddy guards this window. Add lunch, sleep or evenings below only if you want them on the bar.",
        style = Ab.type.small,
        color = Ab.colors.muted,
    )

    var showParts by rememberSaveable { mutableStateOf(template.parts.isNotEmpty()) }
    AbTextButton(if (showParts) "Hide other parts" else "Other parts (${template.parts.size})", onClick = { showParts = !showParts })
    if (showParts) DayTemplateEditor(template, categories, clock, onChange, onMessage, smartNote)
}

@Composable
private fun WorkWindowRow(window: WorkWindow, clock: ClockFormat, onChange: (WorkWindow) -> Unit) {
    var picking by remember { mutableStateOf<String?>(null) }
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Text("Work", style = Ab.type.bodyMedium, color = Ab.colors.ink)
        AbValueButton(clock.time(window.start.atDate(LocalDate.now())), onClick = { picking = "start" })
        Text("–", style = Ab.type.bodyMedium, color = Ab.colors.muted)
        AbValueButton(clock.time(window.end.atDate(LocalDate.now())), onClick = { picking = "end" })
    }

    picking?.let { which ->
        TimeDialog(
            title = if (which == "start") "Work starts at" else "Work ends at",
            initial = if (which == "start") window.start else window.end,
            is24Hour = clock.is24Hour,
            isStart = which == "start",
            onPick = { time ->
                onChange(if (which == "start") window.copy(start = time) else window.copy(end = time))
                picking = null
            },
            onDismiss = { picking = null },
        )
    }
}

@Composable
private fun BudgetEditor(
    template: DayTemplate,
    categories: List<DayCategory>,
    clock: ClockFormat,
    onChange: (DayTemplate) -> Unit,
    onMessage: (String) -> Unit,
    smartNote: String?,
) {
    val minutes = template.budgetMinutes ?: MIN_BUDGET_MINUTES
    fun set(value: Int) = onChange(template.copy(budgetMinutes = value.coerceIn(MIN_BUDGET_MINUTES, MAX_BUDGET_MINUTES)))

    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Text("Focus hours", style = Ab.type.bodyMedium, color = Ab.colors.ink)
        AbValueButton("−", onClick = { set(minutes - BUDGET_STEP_MINUTES) }, enabled = minutes > MIN_BUDGET_MINUTES)
        Text(hoursText(minutes), style = Ab.type.section, color = Ab.colors.ink)
        AbValueButton("+", onClick = { set(minutes + BUDGET_STEP_MINUTES) }, enabled = minutes < MAX_BUDGET_MINUTES)
    }
    Text(
        "The buddy guards from wake to bedtime, minus the windows below (lunch counts as one), until this much focus is counted. " +
            "Test sessions, side tasks and planning don't count.",
        style = Ab.type.small,
        color = Ab.colors.muted,
    )
    Text("Non-productive windows", style = Ab.type.bodyMedium, color = Ab.colors.ink)
    // Only rest, sleep and pre-work categories: a budget day's parts are never work.
    DayTemplateEditor(template, categories.filter { !it.behavior.isWork }, clock, onChange, onMessage, smartNote)
}

/** Minutes as "6h", "6h 30m" or "45m". */
internal fun hoursText(minutes: Int): String = when {
    minutes < 60 -> "${minutes}m"
    minutes % 60 == 0 -> "${minutes / 60}h"
    else -> "${minutes / 60}h ${minutes % 60}m"
}
