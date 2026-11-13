package com.accountabilibuddy.shared.goals

/** Where a goal entry came from (brief §5.1). Pixel Watch data arrives as [HealthConnect]. */
enum class GoalEntrySource {
    /** Typed, ticked or added by hand: the user's own decision, which wins over any reading. */
    Hand,

    HealthConnect,
    ScreenTime,
    Beeminder,
    Calendar,

    /** A PC's #tag extra minutes beyond what the phone's own sessions show (ADR 79); older apps keep it as a newer item. */
    FocusLocal,
}
