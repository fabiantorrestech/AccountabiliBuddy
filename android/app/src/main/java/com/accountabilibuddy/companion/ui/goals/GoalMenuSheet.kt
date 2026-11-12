package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.Checklist
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.Edit
import androidx.compose.material.icons.outlined.Flag
import androidx.compose.material.icons.outlined.NotificationsNone
import androidx.compose.material.icons.automirrored.outlined.OpenInNew
import androidx.compose.material.icons.outlined.Pause
import androidx.compose.material.icons.outlined.PlayArrow
import androidx.compose.material.icons.outlined.PushPin
import androidx.compose.material.icons.outlined.SwapHoriz
import androidx.compose.material.icons.outlined.Timer
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbSwitch
import com.accountabilibuddy.companion.ui.kit.AbSegmented
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetCheck
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.sheets.AbSheetValue
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalKind
import com.accountabilibuddy.shared.goals.GoalMenuItem
import com.accountabilibuddy.shared.goals.GoalMenuItemId
import com.accountabilibuddy.shared.goals.GoalMenuPlan
import com.accountabilibuddy.shared.goals.GoalTexts
import java.time.LocalDate
import java.util.UUID

/**
 * The goal menu (hold a row, or ⋮ on a row or the goal page; brief §4.3, board Goals-Phone-Goal-Menu): an 80% sheet with the goal's
 * name and "Learning · 15 min a day", the same rows in the same order as the PC goal menu ([GoalMenuPlan]).
 *
 * Working here: Tick / Untick today, Log a value / Add time by hand (a hand entry for today), Show on Today, Move to area, Pause / Resume
 * (at once, from today; Deep work refuses), Delete goal… (asks first; Deep work refuses). Their screens come later, so they say
 * "Coming in the next update": Start a session, Reminders. Edit goal… opens the goal's sheets (R6b2). Greyed with a reason: Open its tasks (Coming soon), Open in
 * Beeminder (read on the PC), Deep work's Track time only (its target is the PC's, ADR 29).
 *
 * @param onDeleted Called after the goal is deleted (the goal page goes back to the list).
 * @param onEdit Edit goal…: the caller closes this menu and opens the editor sheets ([GoalEditorSheets]).
 */
@Composable
fun GoalMenuSheet(
    goalId: UUID,
    allReceived: List<ReceivedDevice>,
    received: List<ReceivedDevice>,
    onMessage: (String) -> Unit,
    onDismiss: () -> Unit,
    onDeleted: () -> Unit = {},
    onEdit: () -> Unit = {},
) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val page = rememberGoalPage(goalId, allReceived, received).value
    val lists by app.goals.lists.collectAsStateWithLifecycle()
    var inAreas by rememberSaveable { mutableStateOf(false) }
    var valueDialog by rememberSaveable { mutableStateOf(false) }
    var confirmDelete by rememberSaveable { mutableStateOf(false) }
    val stored = lists.liveGoals.firstOrNull { it.id == goalId }
    if (page == null || stored == null) {
        // Not worked out yet (or the goal is gone): an empty sheet keeps the place; a gone goal closes it.
        AbSheet(title = "Goal", onDismiss = onDismiss, height = AbSheetHeight.Tall) {}
        return
    }

    val goal = page.goal
    val today = LocalDate.now()
    fun done(error: String?, close: Boolean = true) {
        if (error != null) onMessage(error)
        if (close && error == null) onDismiss()
    }

    if (inAreas) {
        AbSheet(title = "Move to area", subtitle = goal.name, onDismiss = onDismiss, height = AbSheetHeight.Tall, onBack = { inAreas = false }) {
            lists.liveAreas.forEach { area ->
                AbSheetRow(
                    area.name, onClick = {
                        if (area.id == stored.areaId) {
                            onDismiss()
                        } else {
                            val position = (lists.liveGoals.filter { it.areaId == area.id }.maxOfOrNull { it.position } ?: 0.0) + 1.0
                            done(app.goals.updateGoal(stored.copy(areaId = area.id, position = position)))
                        }
                    },
                    icon = GoalIcons.of(area.icon), role = Role.RadioButton, trailing = { AbSheetCheck(area.id == stored.areaId) },
                )
            }
        }
        return
    }

    val items = GoalMenuPlan.of(goal, page.facts.todayState, page.facts.isPaused, page.reminderCount, page.areaName)
    val heading = items.first()
    AbSheet(title = heading.title, subtitle = listOfNotNull(page.areaName, GoalTexts.targetLine(goal, today)).joinToString(" · "), onDismiss = onDismiss, height = AbSheetHeight.Tall) {
        items.drop(1).forEach { item ->
            if (item.id == GoalMenuItemId.Separator) {
                Box(Modifier.fillMaxWidth().padding(vertical = 4.dp).height(1.dp).background(Ab.colors.line))
                return@forEach
            }

            val soon: () -> Unit = { onMessage("${item.title.trimEnd('…')} comes in the next update."); onDismiss() }
            AbSheetRow(
                item.title,
                onClick = when (item.id) {
                    GoalMenuItemId.StartSession, GoalMenuItemId.Reminders -> soon
                    GoalMenuItemId.EditGoal -> onEdit
                    GoalMenuItemId.TickToday -> ({ done(app.goals.addHandEntry(goalId, today, if (page.facts.todayState.hit) 0.0 else 1.0)) })
                    GoalMenuItemId.LogValue, GoalMenuItemId.AddTimeByHand -> ({ valueDialog = true })
                    GoalMenuItemId.MoveToArea -> ({ inAreas = true })
                    GoalMenuItemId.ShowOnToday -> ({ done(app.goals.updateGoal(stored.copy(showOnToday = !stored.showOnToday)), close = false) })
                    GoalMenuItemId.Pause -> ({ done(if (page.facts.isPaused) app.goals.resumeGoal(goalId, today) else app.goals.pauseGoal(goalId, today)) })
                    GoalMenuItemId.Delete -> ({ confirmDelete = true })
                    else -> null
                },
                icon = iconOf(item, page.facts.isPaused),
                subtitle = item.note,
                enabled = item.enabled,
                danger = item.danger,
                role = if (item.checked != null) Role.Switch else Role.Button,
                trailing = {
                    when {
                        item.checked != null -> AbSwitch(item.checked!!, onCheckedChange = { done(app.goals.updateGoal(stored.copy(showOnToday = !stored.showOnToday)), close = false) })
                        item.value != null -> AbSheetValue(item.value, opens = item.opensMore)
                        item.opensMore && item.enabled -> AbSheetValue(null)
                    }
                },
            )
        }
    }

    if (valueDialog) {
        val isTime = goal.kind == GoalKind.Time
        GoalValueDialog(
            title = if (isTime) "Add time by hand" else "Log a value",
            caption = if (isTime) "Minutes to add for today" else "Value for today",
            onSave = { value -> app.goals.addHandEntry(goalId, today, value).also { if (it != null) onMessage(it) } == null },
            onDismiss = { valueDialog = false; onDismiss() },
        )
    }
    if (confirmDelete) {
        AbDialog(
            title = "Delete this goal?",
            onDismiss = { confirmDelete = false },
            confirmText = "Delete",
            onConfirm = {
                confirmDelete = false
                val error = app.goals.deleteGoal(goalId)
                if (error != null) onMessage(error) else { onDismiss(); onDeleted() }
            },
        ) {
            androidx.compose.material3.Text("Its entries and reminders go with it. This can't be undone.", style = Ab.type.small, color = Ab.colors.muted)
        }
    }
}

private fun iconOf(item: GoalMenuItem, paused: Boolean) = when (item.id) {
    GoalMenuItemId.StartSession -> Icons.Outlined.PlayArrow
    GoalMenuItemId.TickToday -> Icons.Outlined.Check
    GoalMenuItemId.LogValue -> Icons.Outlined.Edit
    GoalMenuItemId.AddTimeByHand -> Icons.Outlined.Timer
    GoalMenuItemId.OpenTasks -> Icons.Outlined.Checklist
    GoalMenuItemId.OpenInBeeminder -> Icons.AutoMirrored.Outlined.OpenInNew
    GoalMenuItemId.EditGoal -> Icons.Outlined.Edit
    GoalMenuItemId.Reminders -> Icons.Outlined.NotificationsNone
    GoalMenuItemId.MoveToArea -> Icons.Outlined.SwapHoriz
    GoalMenuItemId.ShowOnToday -> Icons.Outlined.PushPin
    GoalMenuItemId.Pause -> if (paused) Icons.Outlined.PlayArrow else Icons.Outlined.Pause
    GoalMenuItemId.Delete -> Icons.Outlined.Delete
    GoalMenuItemId.TrackTimeOnly -> Icons.Outlined.Flag
    else -> null
}
