package com.accountabilibuddy.companion.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayPlanValidator
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.DayPlanDraft
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbChipRow
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * Edits the whole day plan on the phone (ADR 19, ADR 47): day types (each a Schedule, Budget or Free day), the week map
 * (Mon–Sun, one column per week of the cycle), one-off days and categories. The draft lives in the view model (it survives rotation); Save checks it by the PC's rules and
 * sends it to the PC (now, or when they next connect); leaving with changes asks first. This is "Templates" on the Plan page.
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
        AbDialog(
            title = "Discard your changes?",
            onDismiss = { confirmingDiscard = false },
            confirmText = "Discard",
            onConfirm = { confirmingDiscard = false; onClose() },
            dismissText = "Keep editing",
        ) {
            Text("The plan stays as it was.", style = Ab.type.bodyMedium, color = Ab.colors.muted)
        }
    }

    Column(Modifier.fillMaxSize()) {
        Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            AbTextButton("Cancel", leave)
            Text("Day plan", style = Ab.type.section, color = Ab.colors.ink, modifier = Modifier.weight(1f).padding(start = 8.dp))
            AbPrimaryButton("Save", onClick = { onSave() }, enabled = draft.changed)
        }
        Text(
            "Saved changes go to ${pcName ?: "the PC"}, which checks them like its own Plan page; during a focus session there they wait until it ends.",
            style = Ab.type.small,
            color = Ab.colors.muted,
            modifier = Modifier.padding(horizontal = 16.dp),
        )
        AbChipRow(Modifier.padding(top = 8.dp), contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 16.dp)) {
            listOf("Day types", "Week", "One-off", "Categories").forEachIndexed { index, title ->
                AbChip(title, selected = tab == index, onClick = { tab = index })
            }
        }

        Column(
            Modifier.weight(1f).verticalScroll(rememberScrollState()).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            when (tab) {
                0 -> DayTypesEditor(plan, clock, smartNote = { smartNote(it, clock) }, onChange = onChange, onMessage = onMessage)
                1 -> WeekGridEditor(plan, draft.initial, onChange)
                2 -> OneOffDaysEditor(plan, clock, onChange = onChange, onMessage = onMessage)
                else -> CategoriesEditor(plan, onChange = onChange, onMessage = onMessage)
            }

            DayPlanValidator.validate(plan)?.let {
                Text("Can't save yet: $it", color = Ab.colors.drift, style = Ab.type.bodyMedium)
            }
        }
    }
}

/** The smart bedtime's status for a kind of night (ADR 34); null when it's off. */
@Composable
private fun smartNote(kind: com.accountabilibuddy.companion.engine.NightKind, clock: ClockFormat): String? {
    val app = androidx.compose.ui.platform.LocalContext.current.applicationContext as com.accountabilibuddy.companion.CompanionApp
    return if (app.store.state.value.sleep.smartOn(kind)) app.smartBedtime.status(kind, clock) else null
}
