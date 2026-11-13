package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.SyncGoalYearMerged
import java.time.OffsetDateTime
import java.util.UUID

/** A device of the group: whether it is a PC or removed, when its regular sync file was last merged here and its year marker. */
data class GoalGroupDevice(val id: UUID, val isPc: Boolean, val removed: Boolean, val lastRegularMerge: OffsetDateTime?, val yearsMerged: List<SyncGoalYearMerged>?)
