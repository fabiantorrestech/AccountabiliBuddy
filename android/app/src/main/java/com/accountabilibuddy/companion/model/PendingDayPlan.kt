package com.accountabilibuddy.companion.model

import java.time.Instant

/** Where a day plan edit made on the phone stands (ADR 19). */
enum class PendingStatus {
    /** Waiting to reach the PC. */
    Waiting,

    /** The PC is in a focus session the user started; sent when it ends. */
    Locked,

    /** The PC refused it (see the message); the user can fix it or discard it. */
    Invalid,

    /** The PC's app is too old to take edits. */
    NeedsPcUpdate,
}

/**
 * A day plan edited on the phone that the PC hasn't taken yet: the plan it started from (the PC's, with its version)
 * and the edited plan. While it waits, the phone shows the edited plan.
 */
data class PendingDayPlan(
    val baseVersion: String,
    val basePlan: DayPlan,
    val edited: DayPlan,
    val editedAt: Instant,
    val status: PendingStatus = PendingStatus.Waiting,
    val message: String? = null,
)
