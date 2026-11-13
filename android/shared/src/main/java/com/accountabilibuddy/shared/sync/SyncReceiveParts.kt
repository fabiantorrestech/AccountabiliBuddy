package com.accountabilibuddy.shared.sync

import com.accountabilibuddy.shared.tasks.TaskParts

/**
 * What a received file's parts hand to the phone's stores, and what the stores say about being ready (SYNC1c). Each hook is the phone's own merge
 * (the same one a normal accept calls); none of them writes this phone's own file, and nothing here does either: receiving a file never triggers a
 * write, receipts leave with the next scheduled write.
 *
 * @property sharesTaskLists ADR 71: this phone shares its task lists (and so takes the others').
 * @property clockAhead The file was written more than a minute ahead of this phone's clock (the sender's clock runs fast): its lists are not taken now.
 * @property dayPlanHeld A day plan question or wait is open here, so the plan part is "held".
 */
class SyncReceiveHooks(
    val sharesTaskLists: Boolean,
    val clockAhead: Boolean,
    val dayPlanHeld: () -> Boolean = { false },
    val tasksReady: () -> Boolean = { true },
    val oldListsReady: () -> Boolean = { true },
    val goalsReady: () -> Boolean = { true },
    val onSharedLists: (SyncFile) -> Unit = {},
    val onTaskExtras: (SyncFile) -> Unit = {},
    val onTaskParts: (TaskParts) -> Unit = {},
    val onGoalParts: (SyncFile) -> Unit = {},
)

/**
 * The outcome of the last accept of each part of a sender's file (ADR 86 receipts; the PC's `outcome` dictionary of `AcceptOnUiThreadAsync` and
 * `ReceiveSharedListsAsync`): taken, held back, off (the switch), not in the file, or refused (a clock too far ahead, a store that is closed, a
 * part that threw: never lost to the caller). Pure but for the hooks it calls.
 */
object SyncReceiveParts {
    fun outcome(file: SyncFile, hooks: SyncReceiveHooks): Map<String, String> {
        val outcome = LinkedHashMap<String, String>()
        outcome["sessions"] = if (file.device.historyOff) SyncReceiptParts.ABSENT else SyncReceiptParts.TAKEN
        outcome["steps"] = if (file.steps == null) SyncReceiptParts.ABSENT else SyncReceiptParts.TAKEN
        outcome["lunch"] = if (file.lunch == null) SyncReceiptParts.ABSENT else SyncReceiptParts.TAKEN
        outcome["actions"] = if (file.actions == null) SyncReceiptParts.ABSENT else SyncReceiptParts.TAKEN
        // The PC's plan and settings are shown here and applied by this phone's own rules (the app reads them from the received files).
        outcome["dayPlan"] = if (file.dayPlan == null) SyncReceiptParts.ABSENT else if (guarded { hooks.dayPlanHeld() } == true) SyncReceiptParts.HELD else SyncReceiptParts.TAKEN
        outcome["settings"] = if (file.documents.isEmpty()) SyncReceiptParts.ABSENT else SyncReceiptParts.TAKEN
        outcome.putAll(sharedLists(file, hooks))
        return outcome
    }

    // Runs a hook; null when it threw (the part is "refused").
    private fun <T> guarded(block: () -> T): Any? = try {
        block() ?: Unit
    } catch (e: Exception) {
        null
    }

    private fun sharedLists(file: SyncFile, hooks: SyncReceiveHooks): Map<String, String> {
        val hasTasks = file.taskParts != null
        val hasOld = file.nextTasks != null || file.parked != null || file.later != null
        val hasGoals = file.goalParts != null
        val states = LinkedHashMap<String, String>()
        fun fill(tasks: String, old: String, goals: String) {
            states["tasks"] = if (hasTasks) tasks else SyncReceiptParts.ABSENT
            states["oldLists"] = if (hasOld) old else SyncReceiptParts.ABSENT
            states["goals"] = if (hasGoals) goals else SyncReceiptParts.ABSENT
            states["goalHistory"] = states.getValue("goals")
        }

        // ADR 71: a phone that doesn't share its task lists doesn't take the others' either.
        if (!hooks.sharesTaskLists) {
            fill(SyncReceiptParts.OFF, SyncReceiptParts.OFF, SyncReceiptParts.OFF)
            return states
        }

        // Not from a device whose clock runs more than a minute ahead; a later file from it, once the clocks agree, is taken.
        if (hooks.clockAhead) {
            fill(SyncReceiptParts.REFUSED, SyncReceiptParts.REFUSED, SyncReceiptParts.REFUSED)
            return states
        }

        var oldState = SyncReceiptParts.TAKEN
        var tasksState = SyncReceiptParts.TAKEN
        var goalsState = SyncReceiptParts.TAKEN
        // A file with a "later" list (even empty) comes from a writer that knows ADR 50's fields; one without, from an older app.
        if (hasOld && guarded { hooks.onSharedLists(file) } == null) oldState = SyncReceiptParts.REFUSED
        // ADR 72: the rest of the task lists, under the same rules.
        if (guarded { hooks.onTaskExtras(file) } == null) tasksState = SyncReceiptParts.REFUSED
        // R7.2 (ADR 76): the task list's parts; a file without them (an older app, a QR file) leaves the task list alone (Q583).
        file.taskParts?.let { parts -> if (guarded { hooks.onTaskParts(parts) } == null) tasksState = SyncReceiptParts.REFUSED }
        // ADR 79 (GS4): the goal parts; a file without them (an older app, a QR file) leaves the goals alone.
        if (hasGoals && guarded { hooks.onGoalParts(file) } == null) goalsState = SyncReceiptParts.REFUSED

        val tasksOpen = guarded { hooks.tasksReady() } == true
        val oldOpen = guarded { hooks.oldListsReady() } == true
        val goalsOpen = guarded { hooks.goalsReady() } == true
        fill(
            if (tasksOpen) tasksState else SyncReceiptParts.REFUSED,
            if (oldOpen) oldState else SyncReceiptParts.REFUSED,
            if (goalsOpen) goalsState else SyncReceiptParts.REFUSED,
        )
        return states
    }
}
