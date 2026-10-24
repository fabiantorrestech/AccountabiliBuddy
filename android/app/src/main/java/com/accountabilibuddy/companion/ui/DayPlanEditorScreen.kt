package com.accountabilibuddy.companion.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Tab
import androidx.compose.material3.TabRow
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayPlanValidator
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.DayPlanDraft
import java.time.DayOfWeek
import java.time.format.TextStyle
import java.util.Locale

/**
 * Edits the whole day plan on the phone (ADR 19): Workdays (with which days are workdays), Off-Days, one-off days
 * and categories. The draft lives in the view model (it survives rotation); Save checks it by the PC's rules and
 * sends it to the PC (now, or when they next connect); leaving with changes asks first.
 */
@Composable
fun DayPlanEditorScreen(
    draft: DayPlanDraft,
    startTab: Int = 0,
    pcName: String?,
    clock: ClockFormat,
    onChange: (DayPlan) -> Unit,
    onSave: () -> Boolean,
    onClose: () -> Unit,
    onMessage: (String) -> Unit,
) {
    val plan = draft.plan
    var tab by rememberSaveable { mutableIntStateOf(startTab) }
    var confirmingDiscard by remember { mutableStateOf(false) }
    val leave = { if (draft.changed) confirmingDiscard = true else onClose() }
    BackHandler(onBack = leave)

    if (confirmingDiscard) {
        AlertDialog(
            onDismissRequest = { confirmingDiscard = false },
            title = { Text("Discard your changes?") },
            text = { Text("The plan stays as it was.") },
            confirmButton = { TextButton(onClick = { confirmingDiscard = false; onClose() }) { Text("Discard") } },
            dismissButton = { TextButton(onClick = { confirmingDiscard = false }) { Text("Keep editing") } },
        )
    }

    Column(Modifier.fillMaxSize()) {
        Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            TextButton(onClick = leave) { Text("Cancel") }
            Text("Day plan", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 8.dp))
            Spacer(Modifier.weight(1f))
            Button(onClick = { onSave() }, enabled = draft.changed) { Text("Save") }
        }
        Text(
            "Saved changes go to ${pcName ?: "the PC"}, which checks them like its own Day plan tab; during a focus session there they wait until it ends.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(horizontal = 16.dp),
        )
        TabRow(selectedTabIndex = tab, modifier = Modifier.padding(top = 8.dp)) {
            listOf("Workdays", "Off-Days", "One-off", "Categories").forEachIndexed { index, title ->
                Tab(selected = tab == index, onClick = { tab = index }, text = { Text(title) })
            }
        }

        Column(
            Modifier.weight(1f).verticalScroll(rememberScrollState()).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            when (tab) {
                0 -> {
                    WorkdayChips(plan, onChange)
                    DayTemplateEditor(plan.workday, plan.categories, clock, onChange = { onChange(plan.copy(workday = it)) }, onMessage = onMessage)
                }
                1 -> DayTemplateEditor(plan.otherDay, plan.categories, clock, onChange = { onChange(plan.copy(otherDay = it)) }, onMessage = onMessage)
                2 -> OneOffDaysEditor(plan, clock, onChange = onChange, onMessage = onMessage)
                else -> CategoriesEditor(plan, onChange = onChange, onMessage = onMessage)
            }

            DayPlanValidator.validate(plan)?.let {
                Text("Can't save yet: $it", color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodyMedium)
            }
        }
    }
}

/** Which days use the Workdays plan (the rest use Off-Days). */
@Composable
private fun WorkdayChips(plan: DayPlan, onChange: (DayPlan) -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Text("Workdays", style = MaterialTheme.typography.labelLarge)
        Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            for (day in DayOfWeek.entries) {
                val on = day in plan.workdays
                FilterChip(
                    selected = on,
                    onClick = { onChange(plan.copy(workdays = if (on) plan.workdays - day else plan.workdays + day)) },
                    label = { Text(day.getDisplayName(TextStyle.NARROW, Locale.getDefault())) },
                )
            }
        }
    }
}
