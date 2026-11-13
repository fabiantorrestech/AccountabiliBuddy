package com.accountabilibuddy.shared.tasks

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.intOrNull

/**
 * "Add to Today" during a PC session (`TodayHandoff`, R7.2 review NIT): whether a Later item was changed here after its `taskAdd` was
 * sent, by what it says (title, note, open steps, estimate) against what the action carried, not by its edit stamp: with the task list
 * the source, a stamp also moves for a merge, a number or a label rename, which would keep a task the PC took in Later too.
 */
object TaskAddSent {
    /**
     * True when the item now differs from what [sent] (the action's request text) carried; null when [sent] can't be read (the caller
     * falls back to the stamp). [note] and [estimateMinutes] as the request cleans them; [openSteps]: the open steps' titles.
     */
    fun changed(sent: String?, title: String, note: String?, openSteps: List<String>, estimateMinutes: Int?): Boolean? {
        val json = sent?.let { runCatching { Json.parseToJsonElement(it) as? JsonObject }.getOrNull() } ?: return null
        val sentTitle = (json["title"] as? JsonPrimitive)?.contentOrNull ?: return null
        val sentNote = (json["note"] as? JsonPrimitive)?.contentOrNull
        val sentSteps = (json["steps"] as? JsonArray).orEmpty().map { ((it as? JsonObject)?.get("title") as? JsonPrimitive)?.contentOrNull }
        val sentEstimate = (json["estimateMinutes"] as? JsonPrimitive)?.intOrNull
        return sentTitle != title.trim() || sentNote != note || sentSteps != openSteps.map { it.trim() } || sentEstimate != estimateMinutes
    }
}
