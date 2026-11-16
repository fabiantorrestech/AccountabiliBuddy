package com.accountabilibuddy.companion.session

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.shared.session.PhoneSessionRecord
import com.accountabilibuddy.shared.session.SessionTasksFromModel
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.launch

/**
 * The wire between the shared task model and the running phone session's checklist (PS3a, plan check B6, PS2a Q1171): every change of the task lists while a real
 * phone session runs (a tick on Today or on the Tasks page, a removal, a park, a task moved out, an add, an edit that arrived from another device) is turned into
 * This session's list ([SessionTasksFromModel.whileRunning]) and handed to the controller, which counts an open task that left as dropped. "All tasks done"
 * (R9) is judged from that checklist, so taking tasks off This session never ends a session at once; ticking them is the only way.
 *
 * Only a loaded task list counts: before the store has loaded, the list is empty, and an empty list must never read as "every task was removed". An Undo
 * (the toast's) calls [undone], so a removed task that comes back gives its drop back (the PC's `WithOneLessDropped`).
 */
class PhoneSessionTasks(private val app: CompanionApp) {
    /** Called once at app start: follows the session's record and, while a real session runs, the task lists. */
    @OptIn(ExperimentalCoroutinesApi::class)
    fun start(scope: CoroutineScope) {
        scope.launch {
            // The task store is only touched once a real session runs.
            app.phoneSession.running
                .flatMapLatest { record -> if (isReal(record)) app.taskModel.lists else flowOf(null) }
                .collectLatest { lists -> if (lists != null) changed() }
        }
    }

    /** The task list changed: This session's list now is handed to the session (nothing when nothing differs, so nothing is saved for nothing). */
    fun changed() {
        val record = app.phoneSession.controller.running?.takeIf(::isReal) ?: return
        if (!app.taskModel.isActive) return
        val list = SessionTasksFromModel.whileRunning(app.taskModel.lists.value, record.checklist)
        if (list != record.checklist.tasks) app.phoneSession.controller.thisSessionChanged(list)
    }

    /** An Undo put tasks back: the list is read again and the drop of a task that was dropped open is taken back. */
    fun undone() {
        val record = app.phoneSession.controller.running?.takeIf(::isReal) ?: return
        if (!app.taskModel.isActive) return
        app.phoneSession.controller.thisSessionUndone(SessionTasksFromModel.whileRunning(app.taskModel.lists.value, record.checklist))
    }

    private fun isReal(record: PhoneSessionRecord?): Boolean = record != null && record.isRunning && !record.isTest
}
