package com.accountabilibuddy.shared.goals

/** What a goal's row on Today's goals offers at its right (boards Main, B-Phone-Tray-Open). */
enum class GoalTodayControl {
    /** A habit: a round tick, filled once today is done; a tap ticks or unticks today. */
    Tick,

    /** A value typed in (a weight, a count): a "Log" button. */
    Log,

    /** Time, steps or a milestone: a small bar of today's (or the period's) share. */
    Bar,

    /** Nothing to measure against (tracking only). */
    None,
}
