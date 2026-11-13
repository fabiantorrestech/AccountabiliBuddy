package com.accountabilibuddy.shared.goals

/** A goals merge and the entries it dropped because neither input held their goal (the PC's `GoalSyncMergeResult`). */
data class GoalSyncMergeResult(val lists: GoalLists, val droppedEntries: List<GoalEntry>)
