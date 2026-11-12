package com.accountabilibuddy.shared.goals

/** A goal row's visual by kind (change note §4a "Row visual by kind"); the PC's `GoalRowVisual`. */
enum class GoalRowVisual {
    /** Nothing to measure against (tracking only). */
    None,

    /** Time and milestone goals, a per-period amount: a progress bar. */
    Bar,

    /** A daily habit: the 14-day strip. */
    Strip,

    /** A weekly-count habit: the week's dots, the week start first, today as a ring. */
    WeekDots,

    /** A daily amount (steps): 7 day bars, solid where met, today outlined. */
    DayBars,

    /** A level to reach (weight): a sparkline of the recent values. */
    Sparkline,
}
