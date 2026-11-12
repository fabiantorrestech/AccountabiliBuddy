package com.accountabilibuddy.companion.ui.goals

import com.accountabilibuddy.shared.goals.GoalReviewAdjustment
import java.util.UUID

/** The weekly review's "Adjust the target" for one goal: Edit goal › Target opens with [adjustment]'s numbers (null = as it is). */
data class GoalReviewAdjusting(val goalId: UUID, val adjustment: GoalReviewAdjustment?)
