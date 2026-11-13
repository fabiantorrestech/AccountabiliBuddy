package com.accountabilibuddy.shared.goals

import java.time.OffsetDateTime
import java.time.format.DateTimeParseException
import java.util.UUID
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

/**
 * A goal-list item a newer app wrote (a value this version doesn't know), as the merge sees it: only its id, edit time, deletion time and
 * place are read (the PC's `GoalRawItem`); the JSON is kept as written and goes back out unchanged, apart from the stamps the future
 * clamp may change. A compact entry row is read through [GoalEntryRows.stamps]. Never counted or shown.
 */
class GoalRawItem private constructor(
    val json: JsonObject,
    override val id: UUID,
    override val editedAt: OffsetDateTime,
    override val deletedAt: OffsetDateTime?,
    override val position: Double,
) : GoalMergeItem {
    override val tieBreak: String get() = GoalJson.text(json)

    override fun withStamps(editedAt: OffsetDateTime, deletedAt: OffsetDateTime?): GoalRawItem {
        val isRow = GoalEntryRows.isRow(json)
        val fields = LinkedHashMap(json)
        if (isRow) {
            // A row's times are UTC ticks; its offset ("o") is kept.
            fields["e"] = JsonPrimitive(GoalEntryRows.ticksOf(editedAt))
            if (deletedAt != null) fields["x"] = JsonPrimitive(GoalEntryRows.ticksOf(deletedAt)) else fields.remove("x")
        } else {
            // The PC writes the times, then reads the item back as JSON (a JsonElement): written again, every string of it, these times too, goes
            // through the encoder ("+" as +). So plain text here, not a marked time.
            fields["editedAt"] = JsonPrimitive(GoalJson.time(editedAt))
            if (deletedAt != null) fields["deletedAt"] = JsonPrimitive(GoalJson.time(deletedAt)) else fields.remove("deletedAt")
        }
        return GoalRawItem(JsonObject(fields), id, editedAt, deletedAt, position)
    }

    companion object {
        private val guidForm = Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")

        /** The item when its id and stamps read, else null (it is then carried along unread). */
        fun tryRead(json: JsonElement): GoalRawItem? {
            if (json !is JsonObject) return null
            if (GoalEntryRows.isRow(json)) {
                val row = GoalEntryRows.stamps(json) ?: return null
                return GoalRawItem(json, row.id, row.editedAt, row.deletedAt, row.position)
            }
            val id = text(json["id"])?.takeIf { guidForm.matches(it) }?.let(UUID::fromString) ?: return null
            val editedAt = text(json["editedAt"])?.let(::time) ?: return null
            var deletedAt: OffsetDateTime? = null
            val deletedValue = json["deletedAt"]
            if (text(deletedValue) != null) {
                deletedAt = text(deletedValue)?.let(::time) ?: return null
            }

            val position = (json["position"] as? JsonPrimitive)?.takeIf { !it.isString && it !is JsonNull }?.content?.toDoubleOrNull() ?: 0.0
            return GoalRawItem(json, id, editedAt, deletedAt, position)
        }

        private fun text(value: JsonElement?): String? = (value as? JsonPrimitive)?.takeIf { it.isString }?.content?.let(GoalJson::unmark)

        private fun time(text: String): OffsetDateTime? = try {
            OffsetDateTime.parse(text)
        } catch (e: DateTimeParseException) {
            null
        }
    }
}
