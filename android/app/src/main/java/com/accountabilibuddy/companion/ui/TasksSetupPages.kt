package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.tasks.TaskSettingsActions
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbToggleRow
import com.accountabilibuddy.shared.tasks.TaskRingDevices
import com.accountabilibuddy.shared.tasks.TaskView

/**
 * The migration assistant's page for the new Tasks page (since 0.1.79, round 2 R7.4): the view the Tasks page opens with, on every
 * device of the account. Picking one is saved to the account's settings (the same as Settings › Tasks › Default view), which marks this
 * set up. Labels and "Hide the Shelf" are in Settings › Tasks.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
internal fun TasksRedesignSetupPage() {
    val app = LocalContext.current.applicationContext as CompanionApp
    val actions = remember { TaskSettingsActions(app) }
    val lists by app.taskModel.lists.collectAsStateWithLifecycle()
    val views = lists.views.map { it.view }.filter { it.isLive }.sortedBy { it.position }.ifEmpty { TaskView.builtIns(java.time.OffsetDateTime.now()) }
    val chosen = lists.account?.defaultViewId ?: TaskView.DEFAULT_ID
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Tasks: pages, labels and views", style = MaterialTheme.typography.headlineSmall)
        Text("Your tasks are now one list on every device: a page per bin, label or tag, saved views, labels with colours and steps. Tasks, labels and views sync with your other devices. Your open tasks from the earlier version wait in To sort, so you sort them once.")
        Text("Which view should the Tasks page open with? It is the same on all your devices; change it any time in Settings › Tasks.", style = MaterialTheme.typography.titleSmall)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            views.forEach { view ->
                AbChip(
                    view.name.ifBlank { "Default" }, selected = view.id.equals(chosen, ignoreCase = true),
                    onClick = { actions.setDefaultView(view.id.takeUnless { it.equals(TaskView.DEFAULT_ID, ignoreCase = true) }) },
                )
            }
        }
    }
}

/**
 * The migration assistant's page for the devices task reminders ring on (since 0.1.79): one switch per phone of the group, the
 * account's "Rings on" list (the same as Settings › Alerts › Task reminders). A PC always rings for its own reminders. Choosing marks
 * this set up.
 */
@Composable
internal fun TaskRingDevicesSetupPage() {
    val app = LocalContext.current.applicationContext as CompanionApp
    val actions = remember { TaskSettingsActions(app) }
    val lists by app.taskModel.lists.collectAsStateWithLifecycle()
    val received by app.sync.received.collectAsStateWithLifecycle()
    val phones = remember(received) { actions.phones() }
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text("Which devices ring for task reminders?", style = MaterialTheme.typography.headlineSmall)
        Text("Your PCs always ring for their own reminders. Choose the phones that ring too; a label can narrow this in Settings › Tasks › Labels.")
        phones.forEach { phone ->
            AbToggleRow(phone.name, TaskRingDevices.rings(lists.account?.ringOn, phone.id), { on -> actions.setAccountRing(phone.id, on) }, subtitle = if (phone.isThisPhone) "This phone" else "Phone")
        }
    }
}
