package com.accountabilibuddy.shared.goals

/** The span a goal's target is for (brief §5.1). Weeks start on the user's week start ([GoalWeekStart]). */
enum class GoalPeriod {
    Day,
    Week,

    /** Two weeks, counted in pairs from the week the goal was created in ([GoalPeriods]). */
    TwoWeeks,

    /** A calendar month (the week start doesn't matter). */
    Month,
}
