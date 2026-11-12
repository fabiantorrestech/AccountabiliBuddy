package com.accountabilibuddy.shared.goals

/** "This period" for a goal, and the Target tile's pill (brief §5.5). */
enum class GoalPeriodStatus {
    OnTrack,
    Behind,

    /** The period hasn't asked for anything yet (nothing done, and the slack to wait). */
    NotDueYet,

    /** The goal is paused today: left out of the This week card. */
    Paused,
}
