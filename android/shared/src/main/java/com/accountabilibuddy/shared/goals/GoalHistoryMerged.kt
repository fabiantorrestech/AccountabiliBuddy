package com.accountabilibuddy.shared.goals

import java.util.UUID

/**
 * [lists] after a merge; [read] the files to record as read (a file whose rows were dropped is left out so it is read again);
 * [dropped] the rows of goals this device does not hold; [goalsPartMerged] whether a regular file's goals part was merged;
 * [newlyPruned] the goals the late joiner's rule freed ([GoalHistoryRules.latePrunable]): the device adds them to its pruned goal ids.
 */
data class GoalHistoryMerged(
    val lists: GoalLists,
    val read: List<GoalHistoryListing>,
    val dropped: List<GoalEntry>,
    val goalsPartMerged: Boolean,
    val newlyPruned: List<UUID> = emptyList(),
)
