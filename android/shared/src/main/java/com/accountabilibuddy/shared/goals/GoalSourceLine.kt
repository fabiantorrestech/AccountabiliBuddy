package com.accountabilibuddy.shared.goals

/** One line of the goal page's Progress tracking list ("Ticks itself at 15 min in a day"); greyed when "Coming soon". */
data class GoalSourceLine(val text: String, val comingSoon: Boolean = false)
