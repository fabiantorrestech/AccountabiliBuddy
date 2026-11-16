package com.accountabilibuddy.companion.ui.goals

import androidx.compose.material.icons.outlined.Tune
import androidx.compose.material.icons.outlined.PlayCircle
import androidx.compose.material.icons.outlined.PauseCircle
import androidx.compose.material.icons.outlined.Add
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Spacer
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
 * (at once, from today; Deep work refuses), Delete goal… (asks first; Deep work refuses), Start a session… (the presets page:
 * the PC's default, its profiles, Custom…, through the start Today uses; greyed with the reason when Start isn't possible), Reminders
 * ("4 set ›" with "Next: today 8:00 PM"; Edit goal's Reminders page, R6b3). Edit goal… opens the goal's sheets (R6b2). Greyed with a reason: Open in
 * Beeminder (read on the PC), Deep work's Track time only (its target is the PC's, ADR 29). A milestone's **Open its tasks** (GS5b) shows the
 * goal's page with its Tasks card in view ([onOpenTasks]).
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
    /** What "Start a session…" opens (the Start sheet, PS3a); null greys the row. */
    start: GoalSessionStart? = null,
    /** Reminders: the caller closes this menu and opens Edit goal's Reminders page (round 2 R6b3). */
    onReminders: () -> Unit = {},
    /** Open its tasks (a milestone's): the caller closes this menu and shows the goal's page scrolled to its Tasks card (GS5b). */
    onOpenTasks: () -> Unit = {},
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

    val items = GoalMenuPlan.of(
        goal, page.facts.todayState, page.facts.isPaused, page.reminderCount, page.areaName,
        startValue = null, startBlocked = start?.blocked ?: "A session can't be started from here",
        reminderNext = page.reminderNext, linkedTasks = page.linkedTasks, hasTasksCard = page.tasksCard != null, noPc = !com.accountabilibuddy.companion.goals.PhoneDeepWork.hasPc(app.sync.received.value.map { it.file }),
    )
    val heading = items.first()
    AbSheet(title = heading.title, subtitle = listOfNotNull(page.areaName, GoalTexts.targetLine(goal, today), "hold a goal or tap ⋮").joinToString(" · "), onDismiss = onDismiss, height = AbSheetHeight.Tall) {
        items.drop(1).forEach { item ->
            if (item.id == GoalMenuItemId.Separator) {
                // The board draws no lines between the groups (FX-A): a little air only.
                Spacer(Modifier.height(6.dp))
                return@forEach
            }

            AbSheetRow(
                item.title,
                onClick = when (item.id) {
                    GoalMenuItemId.StartSession -> ({ start?.onOpenSheet?.invoke(); onDismiss() })
                    GoalMenuItemId.Reminders -> onReminders
                    GoalMenuItemId.EditGoal -> onEdit
                    GoalMenuItemId.OpenTasks -> onOpenTasks
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
                        item.checked != null -> AbSwitch(item.checked!!, accent = true, onCheckedChange = { done(app.goals.updateGoal(stored.copy(showOnToday = !stored.showOnToday)), close = false) })
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
            caption = (if (isTime) "Minutes to add for today" else "Value for today")
                .let { caption -> GoalTexts.replacesBeeminder(goal, page.facts.todayState)?.let { "$caption · $it" } ?: caption },
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
    // One meaning per icon (ADR 32): the pencil is Edit goal, so logging a value is a plus.
    GoalMenuItemId.LogValue -> Icons.Outlined.Add
    GoalMenuItemId.AddTimeByHand -> Icons.Outlined.Timer
    GoalMenuItemId.OpenTasks -> Icons.Outlined.Checklist
    GoalMenuItemId.OpenInBeeminder -> Icons.AutoMirrored.Outlined.OpenInNew
    GoalMenuItemId.EditGoal -> Icons.Outlined.Edit
    GoalMenuItemId.Reminders -> Icons.Outlined.NotificationsNone
    GoalMenuItemId.MoveToArea -> Icons.Outlined.SwapHoriz
    GoalMenuItemId.ShowOnToday -> Icons.Outlined.PushPin
    // Brief §3.10: pause_circle for a goal (the bare pause is the session's Pause timer; the bare play is Start a session).
    GoalMenuItemId.Pause -> if (paused) Icons.Outlined.PlayCircle else Icons.Outlined.PauseCircle
    GoalMenuItemId.Delete -> Icons.Outlined.Delete
    GoalMenuItemId.TrackTimeOnly -> Icons.Outlined.Flag
    else -> null
}
