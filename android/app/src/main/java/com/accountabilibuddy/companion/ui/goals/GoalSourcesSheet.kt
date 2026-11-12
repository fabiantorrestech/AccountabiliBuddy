package com.accountabilibuddy.companion.ui.goals

import androidx.compose.material.icons.outlined.MonitorWeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.DirectionsWalk
import androidx.compose.material.icons.outlined.Bedtime
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.Checklist
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.CenterFocusStrong
import androidx.compose.material.icons.outlined.Computer
import androidx.compose.material.icons.outlined.PhoneAndroid
import androidx.compose.material.icons.outlined.Tag
import androidx.compose.material.icons.outlined.TouchApp
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.sheets.AbSheetValue
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalLabelId
import com.accountabilibuddy.shared.goals.GoalPhoneRules
import com.accountabilibuddy.shared.goals.GoalSourceRule
import com.accountabilibuddy.shared.goals.GoalSourceType
import java.util.UUID

/**
 * "+ Source" on New goal / Edit goal step 2 (brief §4.3, §6.1; plan §3b 2, 5, 7): what can count toward a goal on this phone.
 * - Working: **Focus on a label** (the task labels on this phone), **Tick by hand**, **Daily steps** (counts only while "Track daily
 *   steps" is on; the row says so), **Sleep** (the rule is saved; the reader comes in the next update).
 * - Greyed with a reason: **Screen time** ("Coming in the next update"), **#tag** and **Google Calendar** ("Measured on your PC": tags
 *   and calendar time are counted on the PC until ADR 79), **Beeminder** (read on the PC), **A task carries this goal** and
 *   **Milestone tasks** ("Coming soon": tasks have no goal link until ADR 79).
 * Hand, steps and sleep can be added once; a label once each. [onAdd] takes the new rule.
 *
 * @param labels Every task label name on this phone.
 * @param stepsOn "Track daily steps" is on.
 */
@Composable
fun GoalSourcesSheet(
    current: List<GoalSourceRule>,
    labels: List<String>,
    stepsOn: Boolean,
    onAdd: (GoalSourceRule) -> Unit,
    onDismiss: () -> Unit,
    onBack: () -> Unit,
) {
    var pickingLabel by rememberSaveable { mutableStateOf(false) }
    if (pickingLabel) {
        val taken = current.filter { it.type == GoalSourceType.FocusLabel }.mapNotNull { it.labelId }.toSet()
        AbSheet(title = "Focus on a label", subtitle = "Sessions with a task carrying it count", onDismiss = onDismiss, height = AbSheetHeight.Tall, onBack = { pickingLabel = false }) {
            if (labels.isEmpty()) {
                Text("No task labels yet. Give a task a label first, then pick it here.", style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = 24.dp, vertical = 12.dp))
            }
            labels.forEach { name ->
                val id = GoalLabelId.idFor(name)
                val added = id in taken
                AbSheetRow(
                    name, onClick = { onAdd(GoalSourceRule(UUID.randomUUID(), GoalSourceType.FocusLabel, labelId = id)); onBack() },
                    icon = Icons.Outlined.CenterFocusStrong, enabled = !added, subtitle = if (added) "Already counting" else null,
                )
            }
        }
        return
    }

    fun has(type: GoalSourceType) = current.any { it.type == type }
    fun add(type: GoalSourceType) { onAdd(GoalSourceRule(UUID.randomUUID(), type)); onBack() }

    AbSheet(title = "Add a source", subtitle = "Sources add up · overlapping time counts once · your edits win", onDismiss = onDismiss, height = AbSheetHeight.Tall, onBack = onBack) {
        Head("ON THIS PHONE")
        AbSheetRow("Focus · a label", onClick = { pickingLabel = true }, icon = Icons.Outlined.CenterFocusStrong, subtitle = "Time in sessions with a task carrying the label", trailing = { AbSheetValue(null) })
        AbSheetRow("Tick by hand", onClick = { add(GoalSourceType.Hand) }, icon = Icons.Outlined.TouchApp, enabled = !has(GoalSourceType.Hand), subtitle = if (has(GoalSourceType.Hand)) "Already counting" else "Tick, log or add time yourself")
        AbSheetRow(
            "Daily steps", onClick = { add(GoalSourceType.HealthSteps) }, icon = Icons.AutoMirrored.Outlined.DirectionsWalk, enabled = !has(GoalSourceType.HealthSteps),
            subtitle = when {
                has(GoalSourceType.HealthSteps) -> "Already counting"
                stepsOn -> "From Health Connect, on this phone"
                else -> "Counts once Track daily steps is on (Settings › Your day)"
            },
        )
        AbSheetRow(
            "Sleep", onClick = { add(GoalSourceType.HealthSleep) }, icon = Icons.Outlined.Bedtime, enabled = !has(GoalSourceType.HealthSleep),
            subtitle = if (has(GoalSourceType.HealthSleep)) "Already counting" else "Counts from the next update",
        )
        AbSheetRow("Screen time", onClick = null, icon = Icons.Outlined.PhoneAndroid, enabled = false, subtitle = "Coming in the next update")
        // Brief §7: shown as a placeholder until its permission is decided (ADR 81, Proposed); weight is typed in until then.
        AbSheetRow("Weight from Health Connect", onClick = null, icon = Icons.Outlined.MonitorWeight, enabled = false, subtitle = "Coming soon · typed in until then")

        Head("MEASURED ON YOUR PC")
        AbSheetRow("Focus · a #tag", onClick = null, icon = Icons.Outlined.Tag, enabled = !GoalPhoneRules.measuredOnPc(GoalSourceType.FocusTag), subtitle = GoalPhoneRules.MEASURED_ON_PC)
        AbSheetRow("Google Calendar", onClick = null, icon = Icons.Outlined.CalendarMonth, enabled = !GoalPhoneRules.measuredOnPc(GoalSourceType.CalendarCategory), subtitle = GoalPhoneRules.MEASURED_ON_PC)
        AbSheetRow("Beeminder", onClick = null, icon = Icons.Outlined.Computer, enabled = !GoalPhoneRules.measuredOnPc(GoalSourceType.Beeminder), subtitle = "Read on your PC")

        Head("COMING SOON")
        AbSheetRow("A task carries this goal", onClick = null, icon = Icons.Outlined.Checklist, enabled = false, subtitle = "Coming soon")
        AbSheetRow("Milestone tasks", onClick = null, icon = Icons.Outlined.Check, enabled = false, subtitle = "Coming soon")
    }
}

@Composable
private fun Head(text: String) {
    Text(text, style = Ab.type.micro, color = Ab.colors.muted, modifier = Modifier.fillMaxWidth().padding(start = 24.dp, top = 12.dp, bottom = 4.dp))
}
