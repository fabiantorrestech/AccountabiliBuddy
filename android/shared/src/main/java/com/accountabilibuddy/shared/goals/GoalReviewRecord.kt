package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.time.OffsetDateTime
import java.util.UUID
import kotlinx.serialization.json.JsonObject

/**
 * One weekly review (brief §5.1, §5.8).
 *
 * @property week The first day of the week reviewed (on the user's week start).
 * @property decisions A decision per goal reviewed.
 * @property snoozedUntil Snoozed for 1 or 2 days; null when not snoozed.
 * @property notes The user's private text: sealed at rest, never logged.
 * @property newerDecisions Decisions of a newer app (a choice this version doesn't know), kept as written and written back after
 * [decisions] at their places, never applied (ADR 79).
 * @property extra Fields written by a newer app, kept and written back unchanged after the known ones (ADR 79 forward compatibility);
 * empty when there are none. May hold private text: sealed at rest, never logged.
 */
data class GoalReviewRecord(
    override val id: UUID,
    val week: LocalDate,
    val decisions: List<GoalReviewDecision>,
    override val position: Double,
    override val editedAt: OffsetDateTime,
    val snoozedUntil: OffsetDateTime? = null,
    val notes: String? = null,
    override val deletedAt: OffsetDateTime? = null,
    val newerDecisions: List<GoalNewerItem> = emptyList(),
    val extra: JsonObject = JsonObject(emptyMap()),
) : GoalMergeItem {
    override val tieBreak: String get() = GoalJson.text(GoalJson.writeReview(this))

    override fun withStamps(editedAt: OffsetDateTime, deletedAt: OffsetDateTime?): GoalReviewRecord = copy(editedAt = editedAt, deletedAt = deletedAt)

    val isLive: Boolean get() = deletedAt == null
}
