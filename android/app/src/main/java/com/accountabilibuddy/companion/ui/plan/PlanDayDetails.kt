package com.accountabilibuddy.companion.ui.plan

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Info
import androidx.compose.material.icons.outlined.Lock
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbInfoBanner
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * What to know about the picked day before its timeline: how the day is guarded when it isn't an ordinary schedule (Budget,
 * Free, day off: the same words as Today's headline), why it can't be changed here, that a focus session holds changes
 * back, a change a PC is holding, and the state of an edit that hasn't reached the PC. Each is one calm line; nothing here
 * changes anything.
 */
@Composable
fun PlanDayDetails(model: PlanPaneModel, actions: PlanPaneActions, modifier: Modifier = Modifier) {
    val any = model.modeLine != null || model.readOnlyNote != null || model.lockNote != null || model.waitNote != null || model.pending != null
    if (!any) return
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        model.modeLine?.let { Text(it, style = Ab.type.bodyMedium, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = 4.dp)) }
        model.readOnlyNote?.let { AbInfoBanner(it, icon = Icons.Outlined.Info) }
        model.lockNote?.let { AbInfoBanner(it, icon = Icons.Outlined.Lock) }
        model.waitNote?.let { AbInfoBanner(it, icon = Icons.Outlined.Schedule) }
        model.pending?.let { PlanEditStatus(it, onFix = actions.onTemplates, onDiscard = actions.onDiscardEdit) }
    }
}
