package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.util.UUID

/** What this device measured for a goal on a day, from one derived source (the PC's `GoalDerivedReading`). */
data class GoalDerivedReading(val goalId: UUID, val source: GoalEntrySource, val day: LocalDate, val value: Double)
