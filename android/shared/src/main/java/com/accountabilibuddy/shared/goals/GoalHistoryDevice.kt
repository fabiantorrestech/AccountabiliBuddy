package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.SyncGoalYearMerged
import java.util.UUID

/** An active device of the group as the history rules see it. */
data class GoalHistoryDevice(val id: UUID, val isPc: Boolean, val yearsMerged: List<SyncGoalYearMerged>?)
