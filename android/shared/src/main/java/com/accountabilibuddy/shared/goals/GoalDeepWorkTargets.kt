package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.FieldStamp

/** Deep work's effective weekly target and "Track time only" with the stamp they travel under (the PC's `GoalDeepWorkTargets`). */
data class GoalDeepWorkTargets(val weeklyMinutes: Double?, val trackTimeOnly: Boolean, val stamp: FieldStamp)
