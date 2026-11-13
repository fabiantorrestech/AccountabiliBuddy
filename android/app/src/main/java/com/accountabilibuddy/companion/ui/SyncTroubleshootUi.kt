package com.accountabilibuddy.companion.ui

import com.accountabilibuddy.companion.sync.SyncTroubleshoot
import com.accountabilibuddy.shared.tasks.TaskConflict
import com.accountabilibuddy.shared.tasks.TaskConflictPick
import com.accountabilibuddy.shared.tasks.TaskLists
import kotlinx.coroutines.flow.StateFlow

/**
 * What Settings › Devices & sync › Troubleshoot sync (SYNC1c) is drawn from and can ask for: the actions and the board ([troubleshoot]), a counter
 * that moves whenever something it shows changed ([version]), the task list for the conflicts list, and the few things the screen cannot know.
 * Display and maintenance only: none of it needs a lock in a focus session, except answering a day plan clash (a Day plan change).
 *
 * @property taskLists The phone's task list; its tasks "Changed on both devices" make the conflicts list.
 * @property answerConflict Applies the user's pick to one conflict (a stamped edit, no Undo); returns the toast text, or null when the task or the conflict is gone.
 * @property deviceNameOf The name of the device that made a version (the id as the task field holds it); "This phone" for this one.
 * @property dayPlanClash Whether a plan edit of this phone and the PC's plan are waiting for an answer (the existing question, a dialog that stays up until it is answered; here a row only, view-only in a session).
 * @property shareReport Hands the diagnostics report's text to the share sheet (a file in the cache, never sent anywhere by itself); false when it could not be shared.
 * @property receiptsSwitch The developer switch "Tell me when sync reaches each device" (moved here, preference key unchanged).
 */
class SyncTroubleshootUi(
    val troubleshoot: SyncTroubleshoot,
    val version: StateFlow<Int>,
    val taskLists: StateFlow<TaskLists>,
    val answerConflict: (id: String, conflict: TaskConflict, pick: TaskConflictPick) -> String?,
    val deviceNameOf: (String?) -> String,
    val dayPlanClash: StateFlow<Boolean>,
    val shareReport: suspend (String) -> Boolean,
    val receiptsSwitch: Pair<() -> Boolean, (Boolean) -> Unit>,
)
