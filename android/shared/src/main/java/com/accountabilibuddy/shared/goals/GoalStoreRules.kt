package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.FieldStamp
import java.time.OffsetDateTime
import java.util.UUID

/**
 * What the goals store does to the lists after every change, once goals sync (ADR 79, GS4; the PC's `GoalStore.Normalize`, rule for rule):
 * Deep work kept whole and first, no other goal built in, a live goal in a live area (the tidy move stamped with [GoalAreaTidy.STAMP_ID],
 * as the merge does), tombstones dropped after [GoalListRules.TOMBSTONES_KEPT] (a goal's own takes its entries and reminders with it;
 * ENTRY tombstones are kept for good, so a stale file can never bring a removed entry back), what this version doesn't know put back, and
 * by [Kind]:
 * - [Kind.Local]: a change made here: every goal part it altered is stamped by this device ([GoalEdits.restamp]), a new goal's at its edit time.
 * - [Kind.Stored]: read from storage, or a backup's restore that stamped its goals itself: nothing is stamped.
 * - [Kind.Synced]: a received merge, taken as merged: nothing is stamped and nothing the merge decided is brought back.
 */
object GoalStoreRules {
    enum class Kind { Local, Stored, Synced }

    fun normalize(before: GoalLists, after: GoalLists, now: OffsetDateTime, kind: Kind, by: String): GoalLists {
        val cutoff = now.toInstant().minus(GoalListRules.TOMBSTONES_KEPT)
        fun kept(deletedAt: OffsetDateTime?) = deletedAt == null || !deletedAt.toInstant().isBefore(cutoff)
        val others = after.goals.filter { it.id != Goal.DEEP_WORK_ID }.map { if (it.builtIn) it.copy(builtIn = false) else it }
        val deepWork = after.goals.firstOrNull { it.id == Goal.DEEP_WORK_ID } ?: before.goals.firstOrNull { it.id == Goal.DEEP_WORK_ID }
        val goals = if (deepWork == null) others else listOf(GoalDefaults.normalized(deepWork)) + others

        // A goal tombstone old enough to go takes its entries and reminders with it (a received merge already did the same).
        val pruned: Set<UUID> = if (kind == Kind.Synced) emptySet() else after.goals.filter { it.deletedAt != null && !kept(it.deletedAt) }.map { it.id }.toSet()
        val stamped = if (kind == Kind.Local) restamped(before.goals, GoalUnknowns.keep(GoalLists(goals = before.goals), GoalLists(goals = goals)).goals, now, by) else goals
        val (areas, placed) = GoalAreaTidy.rehome(after.areas, stamped, GoalDefaults.newWorkArea(now))
        val tidied = GoalLists(
            areas = areas.filter { kept(it.deletedAt) },
            goals = movesStamped(stamped, placed).filter { kept(it.deletedAt) },
            entries = after.entries.filter { it.goalId !in pruned },
            reminders = after.reminders.filter { it.goalId !in pruned && kept(it.deletedAt) },
            reviews = after.reviews.filter { kept(it.deletedAt) },
            newer = after.newer,
        )
        return if (kind == Kind.Synced) tidied else GoalUnknowns.keep(before, tidied)
    }

    // Each live goal's changed parts stamped by this device (a new goal: every part at its edit time, unless it came stamped);
    // tombstones as they are.
    private fun restamped(before: List<Goal>, after: List<Goal>, now: OffsetDateTime, by: String): List<Goal> {
        val held = HashMap<UUID, Goal>()
        for (goal in before) if (goal.isLive) held.putIfAbsent(goal.id, goal)

        // A goal no change touched keeps having no stamps (an older store's goals read as stamped at their edit time anyway), so a change
        // that alters nothing still saves nothing.
        fun unchanged(old: Goal, result: Goal): Goal =
            if (old.stamps == null && DotNetCompare.same(result.editedAt, old.editedAt)) result.copy(stamps = null) else result

        return after.map { goal ->
            val old = held[goal.id]
            when {
                !goal.isLive -> goal
                old != null -> if (old === goal) goal else unchanged(old, GoalEdits.restamp(old, goal, now, by))
                goal.stamps == null -> GoalEdits.new(goal, by)
                else -> goal
            }
        }
    }

    // A goal the tidy moved to "Work" gets its Area part stamped (its new edit time, GoalAreaTidy.STAMP_ID), exactly as the merge does.
    private fun movesStamped(before: List<Goal>, placed: List<Goal>): List<Goal> {
        if (before === placed) return placed
        return placed.mapIndexed { index, goal ->
            if (goal.areaId == before[index].areaId) goal
            else goal.copy(stamps = GoalFieldStamps.forGoal(goal).with(GoalField.Area, FieldStamp(goal.editedAt, GoalAreaTidy.STAMP_ID)))
        }
    }

    private object DotNetCompare {
        fun same(a: OffsetDateTime, b: OffsetDateTime): Boolean = com.accountabilibuddy.shared.sync.DotNetTime.compare(a, b) == 0
    }
}
