package com.accountabilibuddy.shared.goals

/** How a day of Stats' per-goal chart is drawn (round 2 R6b4); the goal heatmap's states ([GoalStripCellKind]) as bars. */
enum class GoalStatsDayMark {
    /** The day met its own target: a solid bar. */
    Hit,

    /** Something counted, under the day's target (or the goal doesn't judge single days): a lighter bar. */
    Some,

    /** A day that asked for something and got nothing: a small stub. */
    Missed,

    /** Today, not hit yet: the accent. */
    Today,

    /** Inside a pause stretch: hatched, neither hit nor missed, and left out of every number. */
    Paused,

    /** Nothing was asked and nothing is drawn: before the goal began, still to come, or a day the goal didn't need. */
    None,
}
