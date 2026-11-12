package com.accountabilibuddy.shared.goals

/** What happened to one goal reminder occurrence on the phone (the PC's `GoalReminderMarkKind`, plus [Shown]). */
enum class GoalReminderMarkKind {
    /** Rings again at [GoalReminderMark.snoozedUntil]. */
    Snoozed,

    /** Swiped away, or handled through Tick today / Open. */
    Dismissed,

    /** "Skip when the goal is already done": done when it came due, so it didn't ring. */
    Skipped,

    /** Posted as a notification (the phone rings each occurrence once). */
    Shown,
}
