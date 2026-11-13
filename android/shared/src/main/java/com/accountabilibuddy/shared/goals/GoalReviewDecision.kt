package com.accountabilibuddy.shared.goals

import java.time.OffsetDateTime
import java.util.UUID
import kotlinx.serialization.json.JsonObject

/**
 * What the weekly review decided for one goal; [extra]: fields written by a newer app, kept and written back unchanged (ADR 79); empty when none.
 *
 * @property decidedAt When this answer was given (ADR 79, GS1a review fix 6): the merge keeps each goal's newest answer by it (else by its
 * record's edit time). Null on an answer an older app wrote; left out of the JSON then.
 */
data class GoalReviewDecision(
    val goalId: UUID,
    val choice: GoalReviewChoice,
    val extra: JsonObject = JsonObject(emptyMap()),
    val decidedAt: OffsetDateTime? = null,
)
