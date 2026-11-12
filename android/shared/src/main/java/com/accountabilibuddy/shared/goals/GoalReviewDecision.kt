package com.accountabilibuddy.shared.goals

import java.util.UUID

/** What the weekly review decided for one goal. */
data class GoalReviewDecision(val goalId: UUID, val choice: GoalReviewChoice)
