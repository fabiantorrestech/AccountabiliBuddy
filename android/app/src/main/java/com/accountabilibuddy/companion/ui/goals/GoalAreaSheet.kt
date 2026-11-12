package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.MoreVert
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
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
import androidx.compose.ui.graphics.compositeOver
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.GuardsRestNote
import com.accountabilibuddy.companion.ui.LocalGuardsLock
import com.accountabilibuddy.companion.ui.ReorderableRows
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbOutlinedButton
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalArea
import com.accountabilibuddy.shared.goals.GoalAreaRules
import com.accountabilibuddy.shared.goals.GoalDrafts
import com.accountabilibuddy.shared.goals.GoalKind
import com.accountabilibuddy.shared.goals.GoalOrderRules
import com.accountabilibuddy.shared.goals.GoalSourceType
import com.accountabilibuddy.shared.goals.GoalTexts
import java.time.LocalDate
import java.time.OffsetDateTime
import java.util.UUID

/**
 * Edit area (hold an area's title, its ✎ with a mouse, or Options › Areas; board Goals-Phone-Area) and a new area (Options › Areas ›
 * Add, or "+ Area" on a goal's step 1): the icon chip and name, the icon, the colour and the goals it holds, each with its target,
 * its kind chip and ⋮ (the goal's menu). Holding a goal drags it into a new order (saved at once; a screen reader has Move up / Move
 * down). Save keeps the area; Delete asks first and says where its goals go (the first other area, in their order). Names are
 * private text: drawn here only, never logged. [onSaved] gets the area's id (a new goal's step 1 picks the area just made).
 *
 * While a focus session runs the area rests (Save, the order and Delete), like Options › Areas and Settings › Goals › Areas.
 *
 * @param onGoalMenu ⋮ on a goal: the caller closes this sheet and opens that goal's menu. Null hides the ⋮.
 */
@Composable
fun GoalAreaSheet(areaId: UUID?, onDismiss: () -> Unit, onMessage: (String) -> Unit, onSaved: (UUID) -> Unit = {}, onGoalMenu: ((UUID) -> Unit)? = null) {
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
    val locked = LocalGuardsLock.current != null
    val goals = if (areaId == null) emptyList() else GoalOrderRules.inArea(lists.goals, areaId)
    // Where its goals go when it is deleted; with goals and no other area there is nowhere, so Delete stays off and says why.
    val heir = if (areaId == null) null else GoalOrderRules.heirOf(lists.areas, areaId)
    val deleteProblem = when {
        areaId == null -> null
        locked -> "Areas rest while a focus session runs."
        goals.isNotEmpty() && heir == null -> "Add another area first: its goals need somewhere to go."
        else -> null
    }

    AbSheet(
        title = if (area == null) "New area" else "Edit area",
        subtitle = "The icon shows on the area chip and its section header.",
        onDismiss = onDismiss,
        height = AbSheetHeight.Tall,
        footer = {
            problem?.takeIf { name.isNotEmpty() }?.let { Text(it, style = Ab.type.small, color = colors.muted) }
            // R6 milestone carry (f81bb32b item 5): every area edit rests in a session, like Settings › Goals › Areas and Options › Areas,
            // whether it was opened by holding an area or from "+ Area" in the goal editor.
            if (locked) GuardsRestNote()
            deleteProblem?.let { Text(it, style = Ab.type.small, color = colors.muted) }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                if (area != null) {
                    AbOutlinedButton("Delete area", onClick = { confirmDelete = true }, enabled = deleteProblem == null, danger = true, modifier = Modifier.weight(1f))
                } else {
                    AbOutlinedButton("Cancel", onClick = onDismiss, modifier = Modifier.weight(1f))
                }
                AbPrimaryButton(
                    "Save",
                    onClick = {
                        val saved = area?.copy(name = name.trim(), icon = icon, color = color)
                            ?: GoalArea(UUID.randomUUID(), name.trim(), icon, color, GoalAreaRules.nextPosition(lists.areas), OffsetDateTime.now())
                        val error = if (area == null) app.goals.addArea(saved) else app.goals.updateArea(saved)
                        if (error != null) onMessage(error) else { onSaved(saved.id); onDismiss() }
                    },
                    enabled = problem == null && !locked, modifier = Modifier.weight(1f),
                )
            }
        },
    ) {
        Row(Modifier.padding(horizontal = 24.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Box(Modifier.size(56.dp).clip(Ab.shapes.mediumShape).background(tint.copy(alpha = 0.16f)), contentAlignment = Alignment.Center) {
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
            val today = LocalDate.now()
            ReorderableRows(
                items = goals.map { it.id },
                onReorder = { order -> GoalOrderRules.reordered(lists.goals, area.id, order).takeIf { it.isNotEmpty() }?.let { app.goals.reorderGoals(it)?.let(onMessage) } },
                label = { id -> goals.firstOrNull { it.id == id }?.name.orEmpty() },
                draggable = !locked && goals.size > 1,
                holdToDrag = true,
            ) { id, _ ->
                goals.firstOrNull { it.id == id }?.let { goal -> AreaGoalRow(goal, today, onMenu = onGoalMenu?.let { open -> { open(goal.id) } }) }
            }
            if (goals.size > 1) {
                Text(
                    if (locked) "The order rests while a focus session runs." else "Press and hold a goal to drag it into a new order.",
                    style = Ab.type.small, color = colors.faint, modifier = Modifier.padding(horizontal = 24.dp, vertical = 10.dp),
                )
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
                val error = if (goals.isEmpty() || heir == null) app.goals.deleteArea(area.id) else app.goals.deleteAreaMovingGoals(area.id, heir.id)
                if (error != null) onMessage(error) else onDismiss()
            },
        ) {
            Text(
                when {
                    goals.isEmpty() || heir == null -> "It holds no goals. The area itself can't be brought back."
                    goals.size == 1 -> "Its goal moves to ${heir.name} and keeps its history. The area itself can't be brought back."
                    else -> "Its ${goals.size} goals move to ${heir.name} and keep their history. The area itself can't be brought back."
                },
                style = Ab.type.small, color = colors.muted,
            )
        }
    }
}

// One goal in Edit area (board Goals-Phone-Area): its icon chip, name, "↳ 15 min a day" with the kind chip, and ⋮.
@Composable
private fun AreaGoalRow(goal: Goal, today: LocalDate, onMenu: (() -> Unit)?) {
    val colors = Ab.colors
    val tint = GoalIcons.color(goal.color) ?: colors.accent
    Row(
        Modifier.fillMaxWidth().heightIn(min = 56.dp).padding(start = 24.dp, end = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Box(Modifier.size(36.dp).clip(RoundedCornerShape(12.dp)).background(tint.copy(alpha = 0.18f).compositeOver(colors.surface2)), contentAlignment = Alignment.Center) {
            Icon(GoalIcons.of(goal.icon), contentDescription = null, tint = tint, modifier = Modifier.size(20.dp))
        }
        Column(Modifier.weight(1f)) {
            Text(goal.name, style = Ab.type.bodyMedium.copy(fontSize = 15.5.sp), fontWeight = FontWeight.Medium, color = colors.ink, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Text("↳ ${GoalTexts.targetLine(goal, today)}", style = Ab.type.small, color = colors.muted, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
                MutedPill(kindChip(goal))
            }
        }
        if (onMenu != null) {
            IconButton(onClick = onMenu) { Icon(Icons.Outlined.MoreVert, contentDescription = "Goal menu: ${goal.name}", tint = colors.muted) }
        }
    }
}

/** "Habit", "Time", "Amount", "Milestone"; "Built in" for Deep work; "Linked" for a goal read from Beeminder. */
internal fun kindChip(goal: Goal): String = when {
    goal.isDeepWork -> "Built in"
    goal.sources.any { it.type == GoalSourceType.Beeminder } -> "Linked"
    else -> when (goal.kind) {
        GoalKind.Habit -> "Habit"
        GoalKind.Time -> "Time"
        GoalKind.Amount -> "Amount"
        GoalKind.Milestone -> "Milestone"
    }
}
