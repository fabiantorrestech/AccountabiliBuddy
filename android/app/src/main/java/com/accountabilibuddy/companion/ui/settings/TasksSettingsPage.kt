package com.accountabilibuddy.companion.ui.settings

import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Bookmark
import androidx.compose.material.icons.outlined.Label
import androidx.compose.material.icons.outlined.Notifications
import androidx.compose.material.icons.outlined.Restore
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.tasks.TaskSettingsActions
import com.accountabilibuddy.companion.tasks.TasksUiPrefs
import com.accountabilibuddy.companion.ui.LocalGuardsLocked
import com.accountabilibuddy.companion.ui.SettingsCategory
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbListRow
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbToggleRow
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetCheck
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.TaskHideShelfResult
import com.accountabilibuddy.shared.tasks.TaskStoreRebuild
import com.accountabilibuddy.shared.tasks.TaskView
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

private val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

/**
 * Settings › Tasks (round 2 R7.4; the PC's Settings › Tasks, in the audit's order): Reminders · Labels · During a session · New tasks ·
 * Reset tasks (only when the task list can't be opened). Draws its own header because it has a page below it, the Labels screen
 * ("‹ Tasks", rows with their reminders' rings, a + for a new label). Everything is saved at once, through the one task list
 * ([TaskSettingsActions]) or this phone's own prefs ([TasksUiPrefs]). The rows with rules (Hide the Shelf) follow the looser-change
 * rule and rest during a focus session; the Labels rows don't (labels are not a guard).
 */
@Composable
fun TasksSettingsPage(anchor: String?, onBack: () -> Unit, onOpenTaskReminders: () -> Unit) {
    var labels by rememberSaveable { mutableStateOf(anchor == SettingsAnchors.TASKS_LABELS) }
    if (labels) {
        SubpageFrame("Tasks", "Labels", onBack = { labels = false }) { TaskLabelsPage() }
        return
    }

    SettingsPageFrame(SettingsCategory.Tasks, onBack) {
        val rows = buildList {
            add(PageRow(SettingsAnchors.TASKS_REMINDERS) { RemindersCard(onOpenTaskReminders, onOpenLabels = { labels = true }) })
            add(PageRow(SettingsAnchors.TASKS_LABELS) { LabelsCard(onOpenLabels = { labels = true }) })
            add(PageRow(SettingsAnchors.TASKS_SESSION) { DuringSessionCard() })
            add(PageRow(SettingsAnchors.TASKS_NEW) { NewTasksCard() })
            add(PageRow(SettingsAnchors.TASKS_RESET) { ResetCard() })
        }
        SettingsPageList(rows, anchor)
    }
}

@Composable
private fun RemindersCard(onOpenTaskReminders: () -> Unit, onOpenLabels: () -> Unit) {
    AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 10.dp), verticalSpacing = 0.dp) {
        AbSectionLabel("Reminders", Modifier.padding(start = 4.dp, bottom = 4.dp))
        AbListRow(
            "Ringing, snooze and full screen", icon = Icons.Outlined.Notifications, subtitle = "Which devices ring, and the snooze shortcuts",
            onClick = onOpenTaskReminders, divider = true,
        )
        AbListRow("Rings on, per label", icon = Icons.Outlined.Label, subtitle = "A label's reminders can ring on fewer devices", onClick = onOpenLabels)
    }
}

@Composable
private fun LabelsCard(onOpenLabels: () -> Unit) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val lists by app.taskModel.lists.collectAsStateWithLifecycle()
    val count = lists.labels.count { it.label.isLive }
    AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 10.dp), verticalSpacing = 0.dp) {
        AbSectionLabel("Labels", Modifier.padding(start = 4.dp, bottom = 4.dp))
        AbListRow(
            "Labels", icon = Icons.Outlined.Label, subtitle = if (count == 1) "1 label" else "$count labels", onClick = onOpenLabels,
        )
    }
}

@Composable
private fun DuringSessionCard() {
    val context = LocalContext.current
    val prefs = remember { TasksUiPrefs.of(context) }
    val state by prefs.state.collectAsStateWithLifecycle()
    val locked = LocalGuardsLocked.current
    var message by remember { mutableStateOf<String?>(null) }
    AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 10.dp), verticalSpacing = 0.dp) {
        AbSectionLabel("During a session", Modifier.padding(start = 4.dp, bottom = 4.dp))
        AbToggleRow(
            "Hide the Shelf", state.hideShelfInSession,
            { hide ->
                message = when (prefs.setHideShelf(hide, TasksUiPrefs.environment(context))) {
                    TaskHideShelfResult.Waiting -> prefs.state.value.showShelfAt?.let { "The Shelf shows during a session from ${it.atZone(ZoneId.systemDefault()).format(CLOCK)}. It never changes during a session." }
                    TaskHideShelfResult.RefusedDuringSession -> "Showing the Shelf during a session can't be asked for during one. Hiding it can."
                    else -> null
                }
            },
            subtitle = "The Shelf waits behind \"Show the Shelf\" while a session runs",
            enabled = !locked,
        )
        state.showShelfAt?.let { at ->
            AbListRow(
                "Shows the Shelf at ${at.atZone(ZoneId.systemDefault()).format(CLOCK)}", icon = Icons.Outlined.Schedule, subtitle = "Showing it is the looser way: it waits (10 min) and never happens during a session",
                trailing = { AbTextButton("Cancel", onClick = { prefs.cancelShowShelf(); message = null }) }, showChevron = false,
            )
        }
        message?.let { Text(it, style = Ab.type.small, color = Ab.colors.amberInk, modifier = Modifier.padding(horizontal = 4.dp, vertical = 6.dp)) }
        Text(
            "Hiding the Shelf applies at once. Showing it waits like any looser change; in ProductaBuddy mode it applies at once.",
            style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = 4.dp, vertical = 6.dp),
        )
    }
}

@Composable
private fun NewTasksCard() {
    val app = LocalContext.current.applicationContext as CompanionApp
    val actions = remember { TaskSettingsActions(app) }
    val lists by app.taskModel.lists.collectAsStateWithLifecycle()
    var picking by rememberSaveable { mutableStateOf(false) }
    val views = lists.views.map { it.view }.filter { it.isLive }.sortedBy { it.position }.ifEmpty { TaskView.builtIns(java.time.OffsetDateTime.now()) }
    val chosen = lists.account?.defaultViewId
    val name = views.firstOrNull { it.id.equals(chosen ?: TaskView.DEFAULT_ID, ignoreCase = true) }?.name?.ifBlank { null } ?: "Default"
    AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 10.dp), verticalSpacing = 0.dp) {
        AbSectionLabel("New tasks", Modifier.padding(start = 4.dp, bottom = 4.dp))
        AbListRow(
            "Default view", icon = Icons.Outlined.Bookmark, subtitle = "What the Tasks page opens with, on every device of your account",
            onClick = { picking = true }, trailing = { Text(name, style = Ab.type.small, color = Ab.colors.muted) },
        )
    }
    if (picking) {
        AbSheet(title = "Default view", subtitle = "Your account's, shared with your other devices", onDismiss = { picking = false }, height = AbSheetHeight.Tall) {
            views.forEach { view ->
                AbSheetRow(
                    view.name.ifBlank { "Default" }, onClick = { actions.setDefaultView(view.id.takeUnless { it.equals(TaskView.DEFAULT_ID, ignoreCase = true) }); picking = false },
                    role = Role.RadioButton, subtitle = if (view.builtIn) "Built in" else null,
                    trailing = { AbSheetCheck(view.id.equals(chosen ?: TaskView.DEFAULT_ID, ignoreCase = true)) },
                )
            }
        }
    }
}

// Reset tasks: only a task list that can't be opened shows it. Rebuild asks first and is refused during a session (Q640).
@Composable
private fun ResetCard() {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    var done by remember { mutableStateOf(false) }
    if (!(app.taskModel.isClosed && !app.taskModel.isActive) && !done) return
    var asking by rememberSaveable { mutableStateOf(false) }
    var message by remember { mutableStateOf<String?>(null) }
    AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 10.dp), verticalSpacing = 0.dp) {
        AbSectionLabel("Reset tasks", Modifier.padding(start = 4.dp, bottom = 4.dp))
        AbListRow(
            "Rebuild the task list", icon = Icons.Outlined.Restore, showChevron = false, onClick = if (done) null else ({ asking = true }),
            subtitle = if (done) "Started again, empty. Your tasks come back with the next sync." else "The task list can't be opened on this phone",
        )
        message?.let { Text(it, style = Ab.type.small, color = Ab.colors.drift, modifier = Modifier.padding(horizontal = 4.dp, vertical = 6.dp)) }
    }
    if (asking) {
        AbDialog(
            title = "Rebuild the task list?", onDismiss = { asking = false }, confirmText = "Rebuild",
            onConfirm = {
                asking = false
                val session = com.accountabilibuddy.companion.placement.GuardsLock.at(app, com.accountabilibuddy.companion.guidance.TrustedTime.now()) != null
                message = TaskStoreRebuild.refusal(app.taskModel.isClosed, session)
                    ?: if (app.taskModel.rebuild()) { done = true; null } else "The rebuild couldn't be saved. Try again."
            },
        ) {
            Text(
                "This starts an empty task list. The copy that can't be opened is kept aside on this phone and never deleted. " +
                    "Your tasks come back from your other devices' next sync or from a backup.",
                style = Ab.type.body, color = Ab.colors.ink,
            )
        }
    }
}
