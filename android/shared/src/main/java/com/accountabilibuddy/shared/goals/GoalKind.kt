package com.accountabilibuddy.shared.goals

/** What sort of goal it is (round 2 brief §5.1). Written by name, the same names as the PC's `GoalKind`. */
enum class GoalKind {
    /** Yes or no each day: ticked by hand, or ticking itself once a linked time threshold is met. */
    Habit,

    /** A value over time (weight, steps, a count): a day's value is a hand edit, else the day's first reading. */
    Amount,

    /** Minutes: focus time on labels or #tags, calendar time, time added by hand. Deep work is one. */
    Time,

    /** A milestone with its own tasks (the task list is "Coming soon" until ADR 79); done by hand for now. */
    Milestone,
}
