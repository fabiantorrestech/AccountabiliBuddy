package com.accountabilibuddy.shared.goals

/** Which way a target points (brief §5.1). */
enum class GoalDirection {
    /** At least the amount (focus minutes, steps). */
    AtLeast,

    /** At most the amount (screen time). */
    AtMost,

    /** Reach a level (a weight): up or down, whichever way the target lies from the goal's first value ([GoalCounting.reachesDown]). */
    Reach,
}
