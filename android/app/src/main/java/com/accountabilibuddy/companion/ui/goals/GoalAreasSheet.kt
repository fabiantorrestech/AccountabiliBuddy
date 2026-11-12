package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.Edit
import androidx.compose.material.icons.outlined.KeyboardArrowDown
import androidx.compose.material.icons.outlined.KeyboardArrowUp
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.shared.goals.GoalAreaRules
import java.util.UUID

/**
 * Options › Areas (brief §4.3; the PC's Areas dialog): every area with its icon, name and how many goals it holds, ↑ ↓ to reorder (each
 * move saves at once; only the areas that changed place are written), ✎ to edit one ([GoalAreaSheet]) and **+ Add area**. Deleting is
 * in Edit area, and only for an empty one.
 */
@Composable
fun GoalAreasSheet(onDismiss: () -> Unit, onEdit: (UUID) -> Unit, onNew: () -> Unit, onMessage: (String) -> Unit) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val lists by app.goals.lists.collectAsStateWithLifecycle()
    val areas = lists.liveAreas
    AbSheet(
        title = "Areas",
        subtitle = "Rename, pick icons, reorder, add",
        onDismiss = onDismiss,
        height = AbSheetHeight.Tall,
        footer = {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                AbPrimaryButton("Add area", onClick = onNew, icon = Icons.Outlined.Add, modifier = Modifier.weight(1f))
            }
        },
    ) {
        areas.forEachIndexed { index, area ->
            val count = GoalAreaRules.goalCount(lists.goals, area.id)
            fun move(to: Int) {
                // Each changed area is its own saved change (the PC's way): a failure on one stops and says so.
                for (changed in GoalAreaRules.moved(lists.areas, area.id, to)) {
                    app.goals.updateArea(changed)?.let { onMessage(it); return }
                }
            }
            AbSheetRow(
                area.name, onClick = { onEdit(area.id) }, icon = GoalIcons.of(area.icon),
                subtitle = if (count == 1) "1 goal" else "$count goals",
                trailing = {
                    Row {
                        AbQuietIconButton(Icons.Outlined.KeyboardArrowUp, "Move ${area.name} up", onClick = { move(index - 1) }, enabled = index > 0)
                        AbQuietIconButton(Icons.Outlined.KeyboardArrowDown, "Move ${area.name} down", onClick = { move(index + 1) }, enabled = index < areas.lastIndex)
                        AbQuietIconButton(Icons.Outlined.Edit, "Edit ${area.name}", onClick = { onEdit(area.id) })
                    }
                },
            )
        }
    }
}
