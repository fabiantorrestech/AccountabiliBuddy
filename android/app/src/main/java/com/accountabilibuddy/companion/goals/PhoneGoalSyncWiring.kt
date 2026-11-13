package com.accountabilibuddy.companion.goals

import com.accountabilibuddy.shared.goals.GoalApplyResult

/**
 * The wiring decisions of the phone's goals sync (ADR 79, GS4; GS4 review), as plain data so the checks can hold them: what each change
 * sets going, and which of it may run inside the goal store's lock. Pure: no Android API.
 */
object PhoneGoalSyncWiring {
    enum class Action {
        /** The auto-backup is due soon. */
        AutoBackup,

        /** This phone's sync file goes out again soon (it is written only when its content changed). */
        SendSyncFiles,

        /** The pruned-goal ids are looked at again ([com.accountabilibuddy.shared.goals.GoalHistorySync.trackPrunedGoals]). */
        TrackPrunedGoals,

        /** The goal reminders' alarm is set again. */
        RefreshReminders,
    }

    /**
     * Whether [action] may run inside the goal store's lock when its hook fires. [Action.TrackPrunedGoals] may not: it takes the history
     * lock, and a history pass holds that lock while it waits for the store's (deadlock); the app posts it instead.
     */
    fun mayRunInsideStoreLock(action: Action): Boolean = action != Action.TrackPrunedGoals

    /** A goal list change made or taken in here (the store's hook). */
    fun afterListChange(): List<Action> = listOf(Action.AutoBackup, Action.TrackPrunedGoals, Action.SendSyncFiles, Action.RefreshReminders)

    /** Deep work's weekly target or its waiting change was written: edits and Cancel's re-stamp must go out promptly too. */
    fun afterWeeklyChange(): List<Action> = listOf(Action.AutoBackup, Action.SendSyncFiles)

    /** The screens, widgets and notices are drawn again after another device's goal parts were taken in: for any change, the weekly target's too. */
    fun redrawAfterReceive(result: GoalApplyResult): Boolean = result == GoalApplyResult.Changed
}
