package com.accountabilibuddy.shared.goals

/** What happened to a weekly-target request ([GoalDeepWorkWeeklyRules.set]). */
enum class GoalDeepWorkWeeklyResult {
    /** The same target: nothing to do (a waiting change for another amount is dropped, as on the PC). */
    Unchanged,

    /** Applied at once (a raise, or a first target). */
    Applied,

    /** Lowering or removing: kept waiting. */
    Waiting,

    /** Lowering or removing during a session outside ProductaBuddy mode: refused, nothing kept. */
    RefusedDuringSession,
}
