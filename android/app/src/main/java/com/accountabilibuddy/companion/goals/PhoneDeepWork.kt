package com.accountabilibuddy.companion.goals

import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeekly
import com.accountabilibuddy.shared.goals.GoalPeriod
import com.accountabilibuddy.shared.goals.GoalTarget
import com.accountabilibuddy.shared.goals.GoalUnit
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile

/**
 * Deep work as the phone shows it (plan §3b 6, ADR 29): its daily target is the PC's daily goal, the newest PC file's `goalMinutes`
 * (the number Today's ring and Stats already read), and it is **read only** here: there is no goal-edit sync action, and nothing on
 * the phone writes it. Its weekly target is this phone's own ([PhoneDeepWorkWeekly]; none until set, with the looser-change wait). Guards, the goal unlock and focusDay
 * never read anything from here.
 */
object PhoneDeepWork {
    /** The PC's daily goal in minutes from [files] (the newest PC file that carries one), or null when none has arrived. */
    fun pcDailyMinutes(files: List<SyncFile>): Int? = files
        .filter { it.device.kind == SyncDeviceCard.KIND_PC && (it.goalMinutes ?: 0) > 0 }
        .maxByOrNull { it.device.writtenAt }?.goalMinutes

    /**
     * [goal] with its targets as views (the PC's daily goal, this phone's [weekly] target, a waiting change on the weekly one); any
     * other goal unchanged.
     */
    fun view(goal: Goal, pcDailyMinutes: Int?, weekly: GoalDeepWorkWeekly = GoalDeepWorkWeekly()): Goal =
        if (!goal.isDeepWork) {
            goal
        } else {
            goal.copy(
                target = GoalTarget(GoalPeriod.Day, pcDailyMinutes?.toDouble(), GoalUnit.Minutes),
                secondTarget = weekly.asTarget()?.copy(pendingChange = weekly.pending),
            )
        }
}
