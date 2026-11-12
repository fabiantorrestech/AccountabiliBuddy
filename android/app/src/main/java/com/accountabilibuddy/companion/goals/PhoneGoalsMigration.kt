package com.accountabilibuddy.companion.goals

import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalDefaults
import com.accountabilibuddy.shared.goals.GoalLists

/**
 * The phone's goals first run (round 2 R6, brief §5.3; the PC's `GoalsMigration`): with nothing stored, it makes the area "Work"
 * and the built-in **Deep work** goal in it, and nothing else. Deep work's daily target is not copied: it stays the PC's daily goal
 * from the sync files, read live and read only ([PhoneDeepWork], ADR 29); its weekly target is this phone's own and none.
 *
 * The marker (`goals.model`) is written with the lists in one commit and only when the result balances (one live built-in goal in a
 * live area, nothing else), so running it again does nothing, and a failed run leaves nothing behind and is tried at the next start.
 * Goals that are stored but can't be read are never set up again on top (Unreadable). There was no goal data on the phone before,
 * so there is nothing to back up first.
 */
object PhoneGoalsMigration {
    /** Runs the first run when it is due; true when the goals are ready afterwards. */
    fun run(store: PhoneGoalStore): Boolean {
        when (store.status.value) {
            PhoneGoalStatus.Ready -> return true
            PhoneGoalStatus.Unreadable -> return false
            PhoneGoalStatus.NotSetUp -> Unit
        }
        val now = PhoneGoalStore.stampNow()
        val area = GoalDefaults.newWorkArea(now)
        val lists = GoalLists(areas = listOf(area), goals = listOf(GoalDefaults.newDeepWork(area.id, now)))
        if (!isBalanced(lists)) return false
        return store.seed(lists)
    }

    /** One live area, one live goal and it is Deep work (built in) in that area; no entries, reminders or reviews. */
    internal fun isBalanced(lists: GoalLists): Boolean {
        val areas = lists.liveAreas
        val goals = lists.liveGoals
        return areas.size == 1 && goals.size == 1 && goals[0].id == Goal.DEEP_WORK_ID && goals[0].builtIn &&
            goals[0].areaId == areas[0].id && lists.entries.isEmpty() && lists.reminders.isEmpty() && lists.reviews.isEmpty()
    }
}
