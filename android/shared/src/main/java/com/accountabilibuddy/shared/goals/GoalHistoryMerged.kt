package com.accountabilibuddy.shared.goals

/**
 * [lists] after a merge; [read] the files to record as read (a file whose rows were dropped is left out so it is read again);
 * [dropped] the rows of goals this device does not hold; [goalsPartMerged] whether a regular file's goals part was merged.
 */
data class GoalHistoryMerged(val lists: GoalLists, val read: List<GoalHistoryListing>, val dropped: List<GoalEntry>, val goalsPartMerged: Boolean)
