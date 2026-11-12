package com.accountabilibuddy.companion.ui.today

import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.sync.RunningTasks
import com.accountabilibuddy.companion.sync.deviceIdOf
import com.accountabilibuddy.shared.sync.SyncAction

/**
 * Ticks a PC session's tasks and steps from the phone: the same `tickTask` sync action the steps card always sent (ADR 24, 26), to
 * the PC whose checklist it is. [canTick] is false without a device group (the list is then read only); [sending] is true while a
 * tick for that id is still on its way, so the row already shows it done.
 */
class TaskTicker(private val sendingIds: Set<String>, val canTick: Boolean, private val send: (String) -> Unit) {
    fun sending(id: String?): Boolean = id != null && id in sendingIds

    fun tick(id: String?) {
        if (canTick && id != null && !sending(id)) send(id)
    }
}

@Composable
fun rememberTaskTicker(running: RunningTasks): TaskTicker {
    val app = LocalContext.current.applicationContext as CompanionApp
    val received by app.sync.received.collectAsState()
    val pending by app.actions.pending.collectAsState()
    val target = deviceIdOf(running.source, received)
    val sending = if (target == null) emptySet() else pending.filter { it.target == target && it.kind == SyncAction.TICK_TASK }.mapNotNull { it.item }.toSet()
    return TaskTicker(sending, canTick = target != null) { id -> target?.let { app.actions.add(SyncAction.TICK_TASK, it, item = id) } }
}
