package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.SyncGoalParts
import java.time.OffsetDateTime
import java.util.UUID

/**
 * The phone's side of goals sync as pure decisions (ADR 79, GS4; the PC's `GoalListSync` and `GoalsService.ApplySyncedAsync`): what goes into
 * the phone's own file, and what a received file does to the phone's goals and to Deep work's weekly target. The store ([GoalStoreRules],
 * the phone's `PhoneGoalStore`) keeps the result; nothing here touches a clock, a device or a file.
 */
object GoalPhoneSync {
    /** What taking a file in decided: the lists to keep and Deep work's weekly state after the received weekly target went through its rule. */
    data class Applied(val lists: GoalLists, val weekly: GoalDeepWorkWeekly)

    /** The stored lists with Deep work's record composed with the weekly target in effect and its stamp: what the merge sees as "ours". */
    fun composed(stored: GoalLists, weekly: GoalDeepWorkWeekly): GoalLists? {
        val record = deepWorkOf(stored) ?: return null
        val targets = GoalDeepWorkWeeklySync.targets(weekly, recordStamp(record))
        return stored.copy(goals = stored.goals.map { if (it.id == Goal.DEEP_WORK_ID && it.isLive) GoalDeepWorkSync.compose(it, targets) else it })
    }

    /**
     * The five goal parts of this phone's own file: the composed lists, entries edited in the last 90 days only ([GoalSyncWindow]), never a
     * waiting change, tombstones without text ([SyncGoalParts.into]). Null while the goals aren't ready: no part, never an empty list.
     */
    fun outgoing(stored: GoalLists, weekly: GoalDeepWorkWeekly, ready: Boolean, now: OffsetDateTime): SyncGoalParts? {
        if (!ready) return null
        val held = composed(stored, weekly) ?: return null
        return SyncGoalParts.into(GoalSyncWindow.recent(held, now))
    }

    /**
     * A received file's parts merged with the phone's held ones, item by item and goal part by goal part ([GoalSyncMerge], never the backup
     * rule), and Deep work's received weekly target taken through the phone's looser-change rule. Null when nothing is to be taken in: the goals
     * aren't ready, or the file carries no goal part (an older app, a QR file, a device that doesn't share its task lists).
     */
    fun applied(
        stored: GoalLists, weekly: GoalDeepWorkWeekly, incoming: SyncGoalParts?, ready: Boolean, now: OffsetDateTime,
        env: GoalDeepWorkWeeklySync.Env, pcDailyMinutes: Double?,
    ): Applied? {
        if (!ready) return null
        val file = SyncGoalParts.from(incoming) ?: return null
        val held = composed(stored, weekly) ?: return null
        return afterMerge(stored, weekly, GoalSyncMerge.mergeReported(held, file, now).lists, env, pcDailyMinutes)
    }

    /**
     * [merged] (this phone's composed lists merged with something) split: Deep work's received weekly target and Track time only go through
     * the phone's rule, and the stored record keeps this phone's own weekly-part stamp, so a merge never rewrites the record for it (no change,
     * no echo).
     */
    fun afterMerge(stored: GoalLists, weekly: GoalDeepWorkWeekly, merged: GoalLists, env: GoalDeepWorkWeeklySync.Env, pcDailyMinutes: Double?): Applied? {
        val ownRecord = deepWorkOf(stored) ?: return null
        val index = merged.goals.indexOfFirst { it.id == Goal.DEEP_WORK_ID && it.isLive }
        if (index < 0) return Applied(merged, weekly)
        val ownStamp = recordStamp(ownRecord)
        val (record, received) = GoalDeepWorkSync.split(merged.goals[index])
        val settled = GoalDeepWorkWeeklySync.received(weekly, received, GoalDeepWorkWeeklySync.ownStamp(weekly, ownStamp), env, pcDailyMinutes)
        val goals = merged.goals.toMutableList()
        goals[index] = record.copy(stamps = GoalFieldStamps.forGoal(record).with(GoalField.SecondTarget, ownStamp))
        return Applied(merged.copy(goals = goals), settled)
    }

    /** The stored Deep work record, or null before the first run made it. */
    fun deepWorkOf(lists: GoalLists): Goal? = lists.goals.firstOrNull { it.id == Goal.DEEP_WORK_ID && it.isLive }

    /** The stamp of Deep work's weekly-target part on the stored record (a record without stamps reads as stamped at its edit time by nobody). */
    fun recordStamp(record: Goal) = GoalFieldStamps.forGoal(record).of(GoalField.SecondTarget)!!

    /**
     * Whether a goal part is derived and written by this device's own hand only: the ownership test the writer and the cleanup share. A
     * device removes only its own derived entries; Beeminder's are the reading PC's, never a phone's.
     */
    fun mayRemove(entry: GoalEntry, deviceId: UUID): Boolean = GoalDerivedWriter.mayChange(entry, deviceId, hasBeeminderToken = false)
}
