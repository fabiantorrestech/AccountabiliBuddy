package com.accountabilibuddy.shared.goals

import java.time.OffsetDateTime

/**
 * A target change's fate ([GoalLowering.decide]).
 *
 * @property appliesAt For [GoalChangeOutcome.Wait]: the earliest it may apply; null otherwise.
 * @property madeInProductaBuddy A wait decided in ProductaBuddy mode (held only by the work-hours rule, no delay).
 */
data class GoalChangeDecision(
    val outcome: GoalChangeOutcome,
    val appliesAt: OffsetDateTime? = null,
    val madeInProductaBuddy: Boolean = false,
) {
    /** The pending change to keep for a wait; null for any other outcome. */
    fun pending(amount: Double?): GoalPendingChange? =
        if (outcome == GoalChangeOutcome.Wait && appliesAt != null) GoalPendingChange(amount, appliesAt, madeInProductaBuddy) else null
}
