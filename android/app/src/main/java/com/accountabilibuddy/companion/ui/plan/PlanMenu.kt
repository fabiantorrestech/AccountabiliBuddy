package com.accountabilibuddy.companion.ui.plan

import androidx.compose.foundation.layout.Box
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.MoreVert
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The ⋮ menu of the Plan header: moving between weeks, "This week is A/B" (only for a plan that repeats in weeks and a phone
 * that can change it), the categories, and discarding an edit that hasn't reached the PC. Items that change the plan are left
 * out, not greyed, when no PC takes the edit.
 */
@Composable
fun PlanMenu(model: PlanPaneModel, actions: PlanPaneActions, onWeekSheet: () -> Unit) {
    var open by remember { mutableStateOf(false) }
    Box {
        AbQuietIconButton(Icons.Outlined.MoreVert, "Plan menu", onClick = { open = true })
        DropdownMenu(expanded = open, onDismissRequest = { open = false }, containerColor = Ab.colors.surface) {
            fun pick(action: () -> Unit): () -> Unit = { open = false; action() }
            if (!model.isToday) MenuRow("Back to today", pick(actions.onToday))
            MenuRow("Previous week", pick { actions.onShiftWeek(-1) })
            MenuRow("Next week", pick { actions.onShiftWeek(1) })
            if (model.canChange && model.weekCount > 1) MenuRow("This week is A/B…", pick(onWeekSheet))
            if (model.canChange) MenuRow("Categories", pick(actions.onCategories))
            if (model.pending != null) MenuRow("Discard my edit", pick(actions.onDiscardEdit))
        }
    }
}

@Composable
private fun MenuRow(text: String, onClick: () -> Unit) {
    DropdownMenuItem(text = { Text(text, style = Ab.type.bodyMedium, color = Ab.colors.ink) }, onClick = onClick)
}
