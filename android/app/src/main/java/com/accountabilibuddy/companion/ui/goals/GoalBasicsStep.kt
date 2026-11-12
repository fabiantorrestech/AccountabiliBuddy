package com.accountabilibuddy.companion.ui.goals

import com.accountabilibuddy.companion.ui.kit.AbOutlinedButton
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbSegmented
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalArea
import com.accountabilibuddy.shared.goals.GoalDraft
import com.accountabilibuddy.shared.goals.GoalDrafts
import com.accountabilibuddy.shared.goals.GoalKind

/**
 * Step 1 of New goal / Edit goal ("1 of 2 · what it is", board Goals-Phone-New): the icon chip and the name, the kind (habit, amount,
 * time, milestone: not for Deep work, which is built in and always time), the icon, the colour and the area chips (each with its own
 * icon, and "+ Area"). Next stays off until [GoalDrafts.basicsProblem] is clear; the problem is said under the fields.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun GoalBasicsStep(
    title: String,
    draft: GoalDraft,
    onDraft: (GoalDraft) -> Unit,
    areas: List<GoalArea>,
    onNewArea: () -> Unit,
    onNext: () -> Unit,
    onDismiss: () -> Unit,
) {
    val colors = Ab.colors
    val problem = GoalDrafts.basicsProblem(draft)
    val tint = GoalIcons.color(draft.color) ?: colors.accent
    AbSheet(
        title = title,
        subtitle = if (draft.isDeepWork) "1 of 2 · Deep work is built in · its name, icon and colour are yours" else "1 of 2 · what it is. Next: target, what counts and reminders.",
        onDismiss = onDismiss,
        height = AbSheetHeight.Tall,
        footer = {
            problem?.let { Text(it, style = Ab.type.small, color = colors.muted) }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                AbOutlinedButton("Cancel", onClick = onDismiss, modifier = Modifier.weight(1f))
                AbPrimaryButton("Next", onClick = onNext, enabled = problem == null, modifier = Modifier.weight(1f))
            }
        },
    ) {
        Row(Modifier.padding(horizontal = 24.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Box(Modifier.size(56.dp).clip(RoundedCornerShape(16.dp)).background(tint.copy(alpha = 0.16f)), contentAlignment = Alignment.Center) {
                Icon(GoalIcons.of(draft.icon), contentDescription = null, tint = tint, modifier = Modifier.size(28.dp))
            }
            AbTextField(draft.name, { onDraft(draft.copy(name = it.take(GoalDrafts.MAX_NAME_LENGTH + 20))) }, "Name", modifier = Modifier.weight(1f))
        }

        if (!draft.isDeepWork) {
            GoalFieldLabel("Kind")
            val kinds = GoalDrafts.KINDS
            AbSegmented(
                listOf("Habit", "Amount", "Time", "Milestone"), kinds.indexOf(draft.kind).takeIf { it >= 0 },
                { onDraft(GoalDrafts.withKind(draft, kinds[it])) }, Modifier.padding(horizontal = 24.dp),
            )
            Text(kindHint(draft.kind), style = Ab.type.small, color = colors.muted, modifier = Modifier.padding(start = 24.dp, end = 24.dp, top = 6.dp))
        }

        GoalFieldLabel("Icon")
        GoalIconGrid(GoalDrafts.ICONS, draft.icon, { onDraft(draft.copy(icon = it)) }, tint = tint)

        GoalFieldLabel("Colour")
        GoalColorRow(GoalDrafts.COLORS, draft.color, { onDraft(draft.copy(color = it)) })

        GoalFieldLabel("Area")
        FlowRow(Modifier.padding(horizontal = 24.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            areas.forEach { area ->
                AbChip(area.name, selected = area.id == draft.areaId, onClick = { onDraft(draft.copy(areaId = area.id)) }, icon = GoalIcons.of(area.icon))
            }
            AbChip("Area", selected = false, onClick = onNewArea, icon = Icons.Outlined.Add)
        }
    }
}

private fun kindHint(kind: GoalKind): String = when (kind) {
    GoalKind.Habit -> "Done or not, each day or a few times a week."
    GoalKind.Amount -> "A value over time: a weight, steps, a count."
    GoalKind.Time -> "Minutes: focus on a label, or time you add."
    GoalKind.Milestone -> "One thing to finish; done by hand for now."
}
