package com.accountabilibuddy.shared.goals

/** How a goal heatmap cell is drawn beyond its ramp level (brief §3.8); the PC's `HeatCellMark`, by the same names. */
enum class GoalHeatCellMark {
    /** Filled with its ramp level. */
    None,

    /** A missed day: a hollow outline, no fill. */
    Hollow,

    /** Some time, under the target: a faint fill inside the outline. */
    Faint,

    /** Today: filled from the bottom with its progress, the accent outline around it. */
    Today,

    /** A paused day: hatched. */
    Paused,

    /** A day still to come this week: a faint dashed outline. */
    Future,
}
