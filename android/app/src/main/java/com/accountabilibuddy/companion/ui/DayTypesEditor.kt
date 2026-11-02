package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayPlanEditing
import com.accountabilibuddy.companion.engine.NightKind
import com.accountabilibuddy.companion.model.DayMode
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.PlanTemplate
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbInsetRow
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
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
            style = Ab.type.small,
            color = Ab.colors.muted,
        )
        for (named in plan.templates) {
            AbInsetRow(onClick = { openText = named.id.toString() }) {
                Column(Modifier.weight(1f)) {
                    Text(named.name, style = Ab.type.body, fontWeight = androidx.compose.ui.text.font.FontWeight.SemiBold, color = Ab.colors.ink)
                    Text(summary(plan, named), style = Ab.type.small, color = Ab.colors.muted)
                }
                Text("Edit", style = Ab.type.bodyMedium, fontWeight = androidx.compose.ui.text.font.FontWeight.SemiBold, color = Ab.colors.ink)
            }
        }

        AbTonalButton("Add a day type", modifier = Modifier.fillMaxWidth(), onClick = {
            val (added, id) = DayPlanEditing.addTemplate(plan)
            onChange(added)
            openText = id.toString()
        })
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
        AbDialog(
            title = "Delete ${named.name}?",
            onDismiss = { deleting = false },
            confirmText = "Delete",
            onConfirm = {
                deleting = false
                onClose()
                onChange(DayPlanEditing.removeTemplate(plan, named.id))
            },
            dismissText = "Keep it",
        ) {
            Text("The weekdays that follow it will follow Off day instead.", style = Ab.type.bodyMedium, color = Ab.colors.muted)
        }
    }

    fun update(next: PlanTemplate) = onChange(plan.copy(templates = plan.templates.map { if (it.id == next.id) next else it }))

    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        AbTextField(value = named.name, onValueChange = { update(named.copy(name = it)) }, label = "Name")
        TemplateModeEditor(named.template, plan.categories, clock, onChange = { update(named.copy(template = it)) }, onMessage = onMessage, smartNote = smartNote)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            AbTextButton("Done", onClose)
            AbTextButton("Duplicate", onClick = {
                DayPlanEditing.duplicateTemplate(plan, named.id)?.let { (copied, id) ->
                    onChange(copied)
                    onMessage("Duplicated as ${copied.templates.first { it.id == id }.name}.")
                }
            })
            if (named.id != DayPlan.OFF_DAY_TEMPLATE_ID) {
                AbTextButton("Delete", onClick = { deleting = true }, danger = true)
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
