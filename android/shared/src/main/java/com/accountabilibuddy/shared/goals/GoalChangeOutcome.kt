package com.accountabilibuddy.shared.goals

/** What happens to a target change ([GoalLowering.decide]). */
enum class GoalChangeOutcome {
    /** The same amount: nothing to do. */
    Unchanged,

    /** Applies at once (stricter, or a goal that feeds no guard). */
    ApplyNow,

    /** Kept as a [GoalPendingChange] until [GoalLowering.canApply] says so. */
    Wait,
}
