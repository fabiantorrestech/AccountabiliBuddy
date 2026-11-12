package com.accountabilibuddy.shared.goals

/** The trend chart's range buttons (brief §4.3 "Weight detail": 1M / 3M / 6M / All). */
enum class GoalTrendRange(val label: String, val months: Int?) {
    OneMonth("1M", 1),
    ThreeMonths("3M", 3),
    SixMonths("6M", 6),
    All("All", null),
}
