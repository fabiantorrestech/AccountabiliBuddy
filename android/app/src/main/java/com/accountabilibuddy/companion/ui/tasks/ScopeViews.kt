package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.shared.tasks.TaskScopeView

/**
 * ADR 89 (LB1c): the task list as THIS phone shows it. Every screen that reads labels or tags reads [TaskScopeView.shown] of this, never the
 * raw list, so another device's device-only labels and tags never show (pickers, chips, pages, filters, search, Today, Settings). A change
 * goes through the store's writers on the real list, which keep what is hidden here.
 */
@Composable
fun rememberScopeView(app: CompanionApp): TaskScopeView {
    val lists by app.taskModel.lists.collectAsState()
    val removed by app.sync.removedDevices.collectAsState()
    val self = remember(app) { app.sync.deviceId.toString().lowercase() }
    return remember(lists, removed) { TaskScopeView(lists, self, removed.map { it.deviceId.toString().lowercase() }.toSet()) }
}

/** The name of the device [id] for "on <device>": a device of the group, a removed one, or "another device". */
fun scopeDeviceName(app: CompanionApp, id: String): String {
    if (id.equals(app.sync.deviceId.toString(), ignoreCase = true)) return "this phone"
    app.sync.allReceived.value.firstOrNull { it.file.device.deviceId.toString().equals(id, ignoreCase = true) }?.let { return it.file.device.name }
    app.sync.removedDevices.value.firstOrNull { it.deviceId.toString().equals(id, ignoreCase = true) }?.let { return it.name }
    return "another device"
}
