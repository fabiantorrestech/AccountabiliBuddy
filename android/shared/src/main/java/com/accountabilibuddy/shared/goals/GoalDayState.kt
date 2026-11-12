package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * A goal's day, computed (brief §5.1), never stored: the heatmap cell, the day panel, the 14-day strip.
 *
 * @property value The day's value in the goal's unit: minutes for a Time goal, the day's value for an Amount goal (0 with
 * [hasValue] false when nothing was logged), 1 or 0 for a habit, the hand total for a milestone.
 * @property minutes The time its time sources counted that day (focus, calendar, hand time), whole minutes.
 * @property hasValue Whether anything counted or was logged that day.
 * @property hit Whether the day met its own target (only for goals that count days: [GoalTarget.countsDays]).
 * @property short How far from the day's target: "N short" (or over an at-most cap) for a past day, "N to go" today; 0 otherwise.
 * @property sources The kinds of source that gave it something, in rule order (and Hand last when a hand entry counted).
 * @property byHand Whether a hand entry decided or added to it.
 * @property backFilled Whether a hand entry for it was made after the day.
 */
data class GoalDayState(
    val day: LocalDate,
    val value: Double,
    val minutes: Double,
    val hasValue: Boolean,
    val hit: Boolean,
    val status: GoalDayStatus,
    val short: Double,
    val sources: List<GoalSourceType>,
    val byHand: Boolean,
    val backFilled: Boolean,
)
