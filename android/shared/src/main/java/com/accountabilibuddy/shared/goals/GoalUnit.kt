package com.accountabilibuddy.shared.goals

/** What a goal's amounts are counted in (brief §5.1). */
enum class GoalUnit {
    Minutes,
    Count,
    Lb,
    Kg,
    Steps,

    /** A habit's tick: 1 = done, 0 = not. */
    YesNo,
}
