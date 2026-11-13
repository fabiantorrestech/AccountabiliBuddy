package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.util.UUID

/** The days of a goal and source a read covered: one of this device's own entries inside it with no reading is gone (the PC's `GoalDerivedCover`). */
data class GoalDerivedCover(val goalId: UUID, val source: GoalEntrySource, val from: LocalDate, val to: LocalDate)
