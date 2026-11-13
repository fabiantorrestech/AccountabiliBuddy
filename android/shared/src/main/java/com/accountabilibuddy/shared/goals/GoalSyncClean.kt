package com.accountabilibuddy.shared.goals

import java.util.UUID

/**
 * Only usable items take part in a goals sync (ADR 79, GS1a review fix 2; the PC's `GoalSyncClean`): no id, an entry or reminder without a
 * goal, a newer app's Deep work tombstone, and anything else the merge could not use is left out (never "deleted": the merge keeps what it
 * has). A tombstone is cleaned to ids and stamps. A live area, goal or name that came without its name / target / sources is already left
 * out when the file is read ([GoalSyncJson]); this runs on every merge input and on what a file or a store hands over.
 */
object GoalSyncClean {
    private val EMPTY_ID = UUID(0L, 0L)

    fun lists(lists: GoalLists): GoalLists = GoalLists(
        areas = areas(lists.areas),
        goals = goals(lists.goals),
        entries = entries(lists.entries),
        reminders = reminders(lists.reminders),
        reviews = reviews(lists.reviews),
        newer = GoalNewerLists(
            areas = newer(lists.newer.areas) { true },
            goals = newer(lists.newer.goals) { !(it.id == Goal.DEEP_WORK_ID && it.deletedAt != null) },
            entries = newer(lists.newer.entries) { true },
            reminders = newer(lists.newer.reminders) { true },
            reviews = newer(lists.newer.reviews) { true },
        ),
    )

    fun areas(list: List<GoalArea>): List<GoalArea> = list.filter { it.id != EMPTY_ID }.map(GoalTombstones::clean)

    fun goals(list: List<Goal>): List<Goal> = list.filter { it.id != EMPTY_ID }.map(GoalTombstones::clean)

    fun entries(list: List<GoalEntry>): List<GoalEntry> = list.filter { it.id != EMPTY_ID && it.goalId != EMPTY_ID }

    fun reminders(list: List<GoalReminder>): List<GoalReminder> = list.filter { it.id != EMPTY_ID && it.goalId != EMPTY_ID }

    fun reviews(list: List<GoalReviewRecord>): List<GoalReviewRecord> = list.filter { it.id != EMPTY_ID }

    // The newer items kept unless their id reads as the empty id or the rule drops them; one whose id does not read is carried as written.
    private fun newer(list: List<GoalNewerItem>, keep: (GoalRawItem) -> Boolean): List<GoalNewerItem> =
        list.filter { item ->
            val raw = GoalRawItem.tryRead(item.json)
            raw == null || (raw.id != EMPTY_ID && keep(raw))
        }
}
