package com.accountabilibuddy.shared.goals

/**
 * The phone's "Today's goals" part: its rows in the Goals list's order and how many still ask for something today ("3 left").
 *
 * @property left The rows that are not done yet.
 */
data class GoalTodayCard(val rows: List<GoalTodayRow>, val left: Int) {
    /** "3 left", or "all done" when nothing is left. */
    val leftText: String get() = if (left == 0) "all done" else "$left left"
}
