package com.accountabilibuddy.shared.goals

/** The "What it takes" pop-up's verdict (brief §5.6). */
enum class GoalWhatItTakesVerdict {
    /** The target is already reached. */
    Met,

    /** Green: "Your daily goal already covers it". */
    Covered,

    /** Amber: how much more is needed per day and per week. */
    NeedsMore,

    /** "The date has passed", with Change the target. */
    DatePassed,

    /** No date: the remaining amount, the pace and the projected finish only. */
    NoDate,
}
