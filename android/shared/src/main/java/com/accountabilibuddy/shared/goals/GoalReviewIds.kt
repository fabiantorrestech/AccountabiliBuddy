package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.util.UUID

/** The id of a weekly review record: one per week on every device, so the same week from two devices is one item (the PC's `GoalReviewIds`). */
object GoalReviewIds {
    private val NAMESPACE: UUID = UUID.fromString("5e7a0c2b-91d4-4f3e-8b6a-2c4d9e1f7a30")

    fun forWeek(week: LocalDate): UUID = GoalNameId.of(NAMESPACE, week.toString())
}
