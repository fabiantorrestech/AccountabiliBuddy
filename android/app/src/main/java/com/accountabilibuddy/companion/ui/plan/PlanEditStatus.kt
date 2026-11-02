package com.accountabilibuddy.companion.ui.plan

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.model.PendingDayPlan
import com.accountabilibuddy.companion.model.PendingStatus
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * Where a day plan edit made on the phone stands (ADR 19), with what can be done about it. A problem (the PC refused it, or it
 * is too old to take it) is in the drift colour with "Fix"; anything else is a calm muted line. "Discard" drops the edit and
 * shows the PC's plan again.
 */
@Composable
fun PlanEditStatus(pending: PendingDayPlan, onFix: (() -> Unit)?, onDiscard: () -> Unit, modifier: Modifier = Modifier) {
    val text = when (pending.status) {
        PendingStatus.Waiting -> "Your plan edit is shown here and goes to the PC when they connect or sync."
        PendingStatus.Locked -> pending.message ?: "It goes to the PC when its focus session ends."
        PendingStatus.Invalid -> "The PC didn't take your plan edit: ${pending.message ?: "it broke a rule"}."
        PendingStatus.NeedsPcUpdate -> pending.message ?: "Update AccountabiliBuddy on the PC to change the plan from here."
    }
    val problem = pending.status == PendingStatus.Invalid || pending.status == PendingStatus.NeedsPcUpdate
    AbCard(modifier, contentPadding = androidx.compose.foundation.layout.PaddingValues(start = 16.dp, end = 8.dp, top = 12.dp, bottom = 4.dp), verticalSpacing = 0.dp) {
        Text(text, style = Ab.type.bodyMedium, color = if (problem) Ab.colors.drift else Ab.colors.muted)
        Row(Modifier.align(Alignment.End), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            if (pending.status == PendingStatus.Invalid && onFix != null) AbTextButton("Fix", onFix)
            AbTextButton("Discard", onDiscard)
        }
    }
}
