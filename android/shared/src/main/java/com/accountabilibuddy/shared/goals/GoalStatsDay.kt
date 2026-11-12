package com.accountabilibuddy.shared.goals

import java.time.LocalDate

/**
 * One day of Stats' per-goal chart (round 2 R6b4).
 *
 * @property amount What the bar stands for, in the goal's unit (a habit with a "ticks itself at" time: its minutes; a habit ticked by
 * hand: 1 or 0). 0 for a paused day, whatever was measured on it.
 * @property fraction The bar's height, 0 to 1 of the chart's.
 * @property text The day in words, as the goal heatmap's tip ("Fri 10/30 · 0 min · missed").
 */
data class GoalStatsDay(val day: LocalDate, val amount: Double, val fraction: Double, val mark: GoalStatsDayMark, val text: String)
