package com.accountabilibuddy.shared.goals

/** The entries one history file held (with the newer items kept), and the file they came from. */
data class GoalHistoryRows(val file: GoalHistoryListing, val entries: GoalLists)
