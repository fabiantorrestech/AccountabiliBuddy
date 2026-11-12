package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalArea
import com.accountabilibuddy.shared.goals.GoalAreaRules
import com.accountabilibuddy.shared.goals.GoalDrafts
import com.accountabilibuddy.shared.goals.GoalTexts
import java.time.LocalDate
import java.time.OffsetDateTime
import java.util.UUID

/**
 * Edit area (hold an area's title, its ✎ with a mouse, or Options › Areas; board Goals-Phone-Area) and a new area (Options › Areas ›
 * Add, or "+ Area" on a goal's step 1): the icon chip and name, the icon, the colour and the goals it holds. Save keeps it; Delete asks
 * first and only an area with no goals can be deleted (a goal is moved from its own menu first). Names are private text: drawn here
 * only, never logged. [onSaved] gets the area's id (a new goal's step 1 picks the area just made).
 */
@Composable
fun GoalAreaSheet(areaId: UUID?, onDismiss: () -> Unit, onMessage: (String) -> Unit, onSaved: (UUID) -> Unit = {}) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val lists by app.goals.lists.collectAsStateWithLifecycle()
    val area = areaId?.let { id -> lists.liveAreas.firstOrNull { it.id == id } }
    LaunchedEffect(areaId != null && area == null) { if (areaId != null && area == null) onDismiss() }
    if (areaId != null && area == null) return

    var name by rememberSaveable(areaId) { mutableStateOf(area?.name.orEmpty()) }
    var icon by rememberSaveable(areaId) { mutableStateOf(area?.icon ?: "track_changes") }
    var color by rememberSaveable(areaId) { mutableStateOf(area?.color ?: GoalDrafts.COLORS[0]) }
    var confirmDelete by rememberSaveable { mutableStateOf(false) }
    val colors = Ab.colors
    val tint = GoalIcons.color(color) ?: colors.accent
    val problem = GoalAreaRules.nameProblem(name)
    val goals = lists.liveGoals.filter { it.areaId == areaId }
    val deleteProblem = if (areaId == null) null else GoalAreaRules.deleteProblem(lists.goals, areaId)

    AbSheet(
        title = if (area == null) "New area" else "Edit area",
        subtitle = "The icon shows on the area chip and its section header.",
        onDismiss = onDismiss,
        height = AbSheetHeight.Tall,
        footer = {
            problem?.takeIf { name.isNotEmpty() }?.let { Text(it, style = Ab.type.small, color = colors.muted) }
            deleteProblem?.let { Text(it, style = Ab.type.small, color = colors.muted) }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                if (area != null) {
                    AbTonalButton("Delete area", onClick = { confirmDelete = true }, enabled = deleteProblem == null, modifier = Modifier.weight(1f))
                } else {
                    AbTonalButton("Cancel", onClick = onDismiss, modifier = Modifier.weight(1f))
                }
                AbPrimaryButton(
                    "Save",
                    onClick = {
                        val saved = area?.copy(name = name.trim(), icon = icon, color = color)
                            ?: GoalArea(UUID.randomUUID(), name.trim(), icon, color, GoalAreaRules.nextPosition(lists.areas), OffsetDateTime.now())
                        val error = if (area == null) app.goals.addArea(saved) else app.goals.updateArea(saved)
                        if (error != null) onMessage(error) else { onSaved(saved.id); onDismiss() }
                    },
                    enabled = problem == null, modifier = Modifier.weight(1f),
                )
            }
        },
    ) {
        Row(Modifier.padding(horizontal = 24.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Box(Modifier.size(56.dp).clip(RoundedCornerShape(16.dp)).background(tint.copy(alpha = 0.16f)), contentAlignment = Alignment.Center) {
                Icon(GoalIcons.of(icon), contentDescription = null, tint = tint, modifier = Modifier.size(28.dp))
            }
            AbTextField(name, { name = it }, "Name", modifier = Modifier.weight(1f))
        }
        GoalFieldLabel("Icon")
        GoalIconGrid(GoalAreaRules.ICONS, icon, { icon = it }, tint = tint)
        GoalFieldLabel("Colour")
        GoalColorRow(GoalDrafts.COLORS, color, { color = it })

        if (area != null) {
            GoalFieldLabel("Goals in this area · ${goals.size}")
            if (goals.isEmpty()) {
                Text("No goals here yet.", style = Ab.type.small, color = colors.faint, modifier = Modifier.padding(horizontal = 24.dp))
            }
            goals.forEach { goal ->
                AbSheetRow(goal.name, onClick = null, icon = GoalIcons.of(goal.icon), subtitle = GoalTexts.targetLine(goal, LocalDate.now()))
            }
        }
    }

    if (confirmDelete && area != null) {
        AbDialog(
            title = "Delete this area?",
            onDismiss = { confirmDelete = false },
            confirmText = "Delete",
            onConfirm = {
                confirmDelete = false
                val error = app.goals.deleteArea(area.id)
                if (error != null) onMessage(error) else onDismiss()
            },
        ) {
            Text("It holds no goals. The area itself can't be brought back.", style = Ab.type.small, color = colors.muted)
        }
    }
}
