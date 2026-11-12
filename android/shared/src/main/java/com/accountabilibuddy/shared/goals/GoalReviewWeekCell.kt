package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/** One box of the review's week row (board Goals-Phone-Review): the weekday's letter, ticked when hit; [counted] false = paused or before the goal. */
data class GoalReviewWeekCell(val day: LocalDate, val letter: String, val hit: Boolean, val counted: Boolean)
