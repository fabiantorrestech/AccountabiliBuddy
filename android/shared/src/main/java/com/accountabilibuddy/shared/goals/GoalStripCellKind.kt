package com.accountabilibuddy.shared.goals

/** How a 14-day strip cell is drawn (change note §4). */
enum class GoalStripCellKind {
    /** Solid. */
    Hit,

    /** Hollow with a faint fill: some time, under the target. */
    Partial,

    /** Hollow: a thin outline, no fill. */
    Missed,

    /** Hatched: neither hit nor missed. */
    Paused,

    /** Nothing asked (before the goal existed, a day a weekly goal didn't need): blank. */
    NotDue,

    /** Today, not hit yet: accent outline, filled from the bottom by [GoalStripCell.progress]. */
    Today,
}
