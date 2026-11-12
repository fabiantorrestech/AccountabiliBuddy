package com.accountabilibuddy.shared.goals

/** A weekly review's decision for a goal, and what the review suggests (brief §5.8). */
enum class GoalReviewChoice {
    KeepGoing,

    /** Adjust the target: opens Edit goal › Target with the suggested numbers; a lowering still waits. */
    Adjust,

    Pause,

    /** "Decide next week": skips the goal in this review (round 2 R5b2; nothing changes, next week's review asks again). */
    DecideNextWeek,
}
