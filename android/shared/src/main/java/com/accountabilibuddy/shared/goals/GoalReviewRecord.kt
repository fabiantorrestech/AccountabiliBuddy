package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.time.OffsetDateTime
import java.util.UUID

/**
 * One weekly review (brief §5.1, §5.8).
 *
 * @property week The first day of the week reviewed (on the user's week start).
 * @property decisions A decision per goal reviewed.
 * @property snoozedUntil Snoozed for 1 or 2 days; null when not snoozed.
 * @property notes The user's private text: sealed at rest, never logged.
 */
data class GoalReviewRecord(
    val id: UUID,
    val week: LocalDate,
    val decisions: List<GoalReviewDecision>,
    val position: Double,
    val editedAt: OffsetDateTime,
    val snoozedUntil: OffsetDateTime? = null,
    val notes: String? = null,
    val deletedAt: OffsetDateTime? = null,
) {
    val isLive: Boolean get() = deletedAt == null
}
