package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * One day of the 14-day strip ([GoalStrip14]).
 *
 * @property progress Today only: how much of the day's target is done, 0 to 1; 0 for other days.
 * @property isToday Today's cell has the accent outline, hit or not.
 */
data class GoalStripCell(val day: LocalDate, val kind: GoalStripCellKind, val progress: Double, val isToday: Boolean)
