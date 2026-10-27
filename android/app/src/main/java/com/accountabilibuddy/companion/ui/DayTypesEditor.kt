package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayPlanEditing
import com.accountabilibuddy.companion.engine.NightKind
import com.accountabilibuddy.companion.model.DayMode
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.PlanTemplate
import java.time.format.TextStyle
import java.util.Locale

/**
 * The plan's day types (schema v12, ADR 47): the list with a one-line summary and the weekdays each one follows, "Add a
 * day type", and, for the one opened, its name, mode and contents ([TemplateModeEditor]). A day type can be duplicated
 * or deleted from its page (its weekdays then follow Off day); Off day stays, since it's what any unmapped date follows.
 */
@Composable
fun DayTypesEditor(
    plan: DayPlan,
    clock: ClockFormat,
    smartNote: @Composable (NightKind) -> String?,
    onChange: (DayPlan) -> Unit,
    onMessage: (String) -> Unit,
) {
    // Kept as text so the day type being edited survives rotation.
    var openText by rememberSaveable { mutableStateOf<String?>(null) }
    val open = openText?.let { text -> plan.templates.firstOrNull { it.id.toString() == text } }

    if (open != null) {
        DayTypeDetail(plan, open, clock, smartNote(if (plan.isWorkdayTemplate(open.id)) NightKind.Workday else NightKind.OffDay), onChange, onMessage) { openText = null }
        return
    }

    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(
            "A day type is how a day is planned: a schedule, a budget of focus hours, or free. The Week tab says which weekdays follow which.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        for (named in plan.templates) {
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                Column(Modifier.weight(1f)) {
                    Text(named.name, style = MaterialTheme.typography.titleSmall)
                    Text(
                        summary(plan, named),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                TextButton(onClick = { openText = named.id.toString() }) { Text("Edit") }
            }
        }

        Button(onClick = {
            val (added, id) = DayPlanEditing.addTemplate(plan)
            onChange(added)
            openText = id.toString()
        }) { Text("Add a day type") }
    }
}

@Composable
private fun DayTypeDetail(
    plan: DayPlan,
    named: PlanTemplate,
    clock: ClockFormat,
    smartNote: String?,
    onChange: (DayPlan) -> Unit,
    onMessage: (String) -> Unit,
    onClose: () -> Unit,
) {
    var deleting by remember { mutableStateOf(false) }
    if (deleting) {
        AlertDialog(
            onDismissRequest = { deleting = false },
            title = { Text("Delete ${named.name}?") },
            text = { Text("The weekdays that follow it will follow Off day instead.") },
            confirmButton = {
                TextButton(onClick = {
                    deleting = false
                    onClose()
                    onChange(DayPlanEditing.removeTemplate(plan, named.id))
                }) { Text("Delete") }
            },
            dismissButton = { TextButton(onClick = { deleting = false }) { Text("Keep it") } },
        )
    }

    fun update(next: PlanTemplate) = onChange(plan.copy(templates = plan.templates.map { if (it.id == next.id) next else it }))

    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        OutlinedTextField(
            value = named.name,
            onValueChange = { update(named.copy(name = it)) },
            label = { Text("Name") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
        )
        TemplateModeEditor(named.template, plan.categories, clock, onChange = { update(named.copy(template = it)) }, onMessage = onMessage, smartNote = smartNote)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            TextButton(onClick = onClose) { Text("Done") }
            TextButton(onClick = {
                DayPlanEditing.duplicateTemplate(plan, named.id)?.let { (copied, id) ->
                    onChange(copied)
                    onMessage("Duplicated as ${copied.templates.first { it.id == id }.name}.")
                }
            }) { Text("Duplicate") }
            if (named.id != DayPlan.OFF_DAY_TEMPLATE_ID) {
                TextButton(onClick = { deleting = true }) { Text("Delete") }
            }
        }
    }
}

/** "Schedule · work 9:00 AM–5:00 PM · Mon, Tue" style line under a day type's name. */
private fun summary(plan: DayPlan, named: PlanTemplate): String {
    val template = named.template
    val mode = when (template.mode) {
        DayMode.Schedule -> if (template.workWindow != null) "Schedule · work window" else "Schedule · ${template.parts.size} parts"
        DayMode.Budget -> "Budget · ${hoursText(template.budgetMinutes ?: 0)}"
        DayMode.Free -> "Free"
    }
    val days = DayPlanEditing.weekdaysOf(plan, named.id)
    val used = if (days.isEmpty()) "no weekdays" else days.joinToString(", ") { it.getDisplayName(TextStyle.SHORT, Locale.getDefault()) }
    return "$mode · $used"
}
