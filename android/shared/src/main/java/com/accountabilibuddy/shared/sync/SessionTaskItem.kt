package com.accountabilibuddy.shared.sync

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.put
import java.time.Instant
import java.time.OffsetDateTime

/**
 * A task (or subtask) of a guided session's checklist as the PC sends it (ADR 24): in the link's session state and as
 * part of a session in a sync file. [doneAt] is when it was ticked; null while open.
 */
data class SessionTaskItem(val id: String?, val title: String, val doneAt: Instant?, val subtasks: List<SessionTaskItem>) {
    val isDone: Boolean get() = doneAt != null

    companion object {
        /** The PC's JSON ({"id","title","doneAt","subtasks"}); items that aren't objects with a title are left out. */
        fun read(array: JsonArray?): List<SessionTaskItem> = array.orEmpty().mapNotNull { element ->
            val json = element as? JsonObject ?: return@mapNotNull null
            val title = (json["title"] as? JsonPrimitive)?.contentOrNull ?: return@mapNotNull null
            SessionTaskItem(
                (json["id"] as? JsonPrimitive)?.contentOrNull,
                title,
                (json["doneAt"] as? JsonPrimitive)?.contentOrNull?.let { runCatching { OffsetDateTime.parse(it).toInstant() }.getOrNull() },
                read(json["subtasks"] as? JsonArray),
            )
        }

        fun write(items: List<SessionTaskItem>, time: (Instant) -> String): JsonArray = JsonArray(items.map { item ->
            buildJsonObject {
                put("id", item.id)
                put("title", item.title)
                put("doneAt", item.doneAt?.let(time))
                put("subtasks", write(item.subtasks, time))
            }
        })
    }
}
