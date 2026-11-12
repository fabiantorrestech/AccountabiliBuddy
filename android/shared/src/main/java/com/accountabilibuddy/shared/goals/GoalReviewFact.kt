package com.accountabilibuddy.shared.goals

/** One fact pill on the review's goal card (brief §5.8; the PC's `GoalReviewFact`); [amber] for late or behind. */
data class GoalReviewFact(val text: String, val amber: Boolean = false)
