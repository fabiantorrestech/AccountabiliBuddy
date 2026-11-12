package com.accountabilibuddy.shared.goals

import kotlinx.serialization.json.JsonObject

/** One app or site a screen-time source counts. [text] is private text: sealed at rest, never logged; [extra]: fields written by a newer app, kept and written back unchanged (ADR 79); empty when none. */
data class GoalAppMatch(val kind: GoalAppMatchKind, val text: String, val extra: JsonObject = JsonObject(emptyMap()))
