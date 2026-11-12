package com.accountabilibuddy.shared.goals

import kotlinx.serialization.json.JsonObject

/** Edit goal › "Extra tiles" (change note §4a): Streak and Total show on the goal only when switched on; [extra]: fields written by a newer app, kept and written back unchanged (ADR 79); empty when none. */
data class GoalExtraTiles(val streak: Boolean = false, val total: Boolean = false, val extra: JsonObject = JsonObject(emptyMap())) {
    companion object {
        val NONE = GoalExtraTiles()
    }
}
