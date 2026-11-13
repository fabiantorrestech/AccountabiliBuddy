package com.accountabilibuddy.shared.tasks

import kotlinx.serialization.json.JsonObject
import java.time.OffsetDateTime

/**
 * A step of a [TaskItem] (ADR 76; the PC's TaskStep). Its letter is its place in the list, never stored.
 *
 * @property doneAt When it was ticked; null while it is open.
 * @property focus It is one of the session's actions.
 * @property estimateMinutes Its time limit; null for none.
 */
data class TaskStep(
    val id: String,
    val title: String,
    val doneAt: OffsetDateTime? = null,
    val focus: Boolean = false,
    val estimateMinutes: Int? = null,
    val extra: JsonObject = TaskUnknown.NONE,
) {
    val isDone: Boolean get() = doneAt != null
}
