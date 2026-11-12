package com.accountabilibuddy.shared.goals

/** Edit goal › "Extra tiles" (change note §4a): Streak and Total show on the goal only when switched on. */
data class GoalExtraTiles(val streak: Boolean = false, val total: Boolean = false) {
    companion object {
        val NONE = GoalExtraTiles()
    }
}
