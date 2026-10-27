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
 * part of a session in a sync file. [doneAt] is when it was ticked; null while open. [categoryId], [category] (its name)
 * and [categoryColor] ("#RRGGBB") are the task's category; all null = no category. The PC sends the list already grouped.
 */
data class SessionTaskItem(
    val id: String?,
    val title: String,
    val doneAt: Instant?,
    val subtasks: List<SessionTaskItem>,
    val categoryId: String? = null,
    val category: String? = null,
    val categoryColor: String? = null,
) {
    val isDone: Boolean get() = doneAt != null

    companion object {
        /** The PC's JSON ({"id","title","doneAt","subtasks"}, optional "categoryId","category","categoryColor"); items that aren't objects with a title are left out. */
        fun read(array: JsonArray?): List<SessionTaskItem> = array.orEmpty().mapNotNull { element ->
            val json = element as? JsonObject ?: return@mapNotNull null
            val title = (json["title"] as? JsonPrimitive)?.contentOrNull ?: return@mapNotNull null
            SessionTaskItem(
                (json["id"] as? JsonPrimitive)?.contentOrNull,
                title,
                (json["doneAt"] as? JsonPrimitive)?.contentOrNull?.let { runCatching { OffsetDateTime.parse(it).toInstant() }.getOrNull() },
                read(json["subtasks"] as? JsonArray),
                (json["categoryId"] as? JsonPrimitive)?.contentOrNull,
                (json["category"] as? JsonPrimitive)?.contentOrNull,
                (json["categoryColor"] as? JsonPrimitive)?.contentOrNull,
            )
        }

        /**
         * What the PC's bar shows (its SessionChecklist.Shown): the picked open subtask with its task; or the picked open
         * task, else the first open task, with its first open subtask (null when it has none). Null when all are done.
         */
        fun shown(tasks: List<SessionTaskItem>, currentId: String?): Pair<SessionTaskItem, SessionTaskItem?>? {
            val open = tasks.filter { !it.isDone }
            if (currentId != null) {
                open.forEach { task -> task.subtasks.firstOrNull { it.id == currentId && !it.isDone }?.let { return task to it } }
            }

            val task = open.firstOrNull { currentId != null && it.id == currentId } ?: open.firstOrNull() ?: return null
            return task to task.subtasks.firstOrNull { !it.isDone }
        }

        /**
         * The open tasks in the order the PC lists them: grouped by category (id, else name) in the order each first
         * appears, tasks with no category last. Their numbers (1, 2, 3…) are their places in this list.
         */
        fun openInShownOrder(tasks: List<SessionTaskItem>): List<SessionTaskItem> {
            val keyOf: (SessionTaskItem) -> String? = { it.categoryId ?: it.category }
            val open = tasks.filter { !it.isDone }
            return open.filter { keyOf(it) != null }.groupBy(keyOf).values.flatten() + open.filter { keyOf(it) == null }
        }

        fun write(items: List<SessionTaskItem>, time: (Instant) -> String): JsonArray = JsonArray(items.map { item ->
            buildJsonObject {
                put("id", item.id)
                put("title", item.title)
                put("doneAt", item.doneAt?.let(time))
                put("subtasks", write(item.subtasks, time))
                // Left out when absent, like the PC does.
                item.categoryId?.let { put("categoryId", it) }
                item.category?.let { put("category", it) }
                item.categoryColor?.let { put("categoryColor", it) }
            }
        })
    }
}
