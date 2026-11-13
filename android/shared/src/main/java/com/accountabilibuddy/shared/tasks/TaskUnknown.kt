package com.accountabilibuddy.shared.tasks

import kotlinx.serialization.json.JsonObject

/**
 * Fields of a task object that this version doesn't know (a newer app wrote them): each model class keeps them in its `extra` and
 * [TaskJson] writes them back after the known fields, so the phone never strips what a newer phone stored. The PC drops them
 * when it reads, so they never take part in a merge's comparisons or tie-breaks.
 */
object TaskUnknown {
    /** No unknown fields. */
    val NONE: JsonObject = JsonObject(emptyMap())
}
