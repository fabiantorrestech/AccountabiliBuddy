package com.accountabilibuddy.shared.goals

import java.util.UUID
import kotlinx.serialization.json.JsonObject

/** What the weekly review decided for one goal; [extra]: fields written by a newer app, kept and written back unchanged (ADR 79); empty when none. */
data class GoalReviewDecision(val goalId: UUID, val choice: GoalReviewChoice, val extra: JsonObject = JsonObject(emptyMap()))
