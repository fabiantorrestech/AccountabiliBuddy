package com.accountabilibuddy.shared.goals

/**
 * Everything a device's goals store keeps that a backup carries and a future goals sync (ADR 79, Proposed) would merge: areas, goals,
 * entries, reminders and weekly reviews, tombstones included; the PC's `GoalLists`. Every item has its id, position, edit stamp and,
 * once deleted, a 30-day tombstone with no text. Names, notes and reasons are private text: sealed at rest, never logged.
 */
data class GoalLists(
    val areas: List<GoalArea> = emptyList(),
    val goals: List<Goal> = emptyList(),
    val entries: List<GoalEntry> = emptyList(),
    val reminders: List<GoalReminder> = emptyList(),
    val reviews: List<GoalReviewRecord> = emptyList(),
) {
    /** Live areas, in their order. */
    val liveAreas: List<GoalArea> get() = areas.filter { it.isLive }.sortedBy { it.position }

    /** Live goals, in their order. */
    val liveGoals: List<Goal> get() = goals.filter { it.isLive }.sortedBy { it.position }

    /** Live entries (readings, ticks, logs). */
    val liveEntries: List<GoalEntry> get() = entries.filter { it.isLive }

    companion object {
        val EMPTY = GoalLists()
    }
}
