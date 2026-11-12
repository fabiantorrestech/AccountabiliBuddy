package com.accountabilibuddy.shared.goals

/** A day's status for a goal (brief §5.1, change note §4a). Computed, never stored. */
enum class GoalDayStatus {
    /** Hit with the user's own hand in it (a tick, a typed value, time added by hand). */
    Counted,

    /** "Counted · ticked itself": hit by its sources alone (focus, a reading). */
    CountedTickedItself,

    /** "Not counted · N short": some progress, under the day's target (or over an at-most cap); see [GoalDayState.short]. */
    NotCounted,

    /** A past day that asked for something and got nothing. */
    Missed,

    /** Today, not hit yet ("In progress · N min to go"). */
    InProgress,

    /** Inside a pause stretch: neither hit nor missed. */
    Paused,

    /** Nothing was asked of the day: before the goal existed, in the future, or a day a weekly goal didn't need. */
    NotDue,
}
