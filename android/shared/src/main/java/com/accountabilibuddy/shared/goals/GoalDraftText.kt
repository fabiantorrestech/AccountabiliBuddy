package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.util.UUID
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put

/**
 * A [GoalDraft] as one line of text and back, so a half-made goal survives a screen turn (the sheet keeps it in `rememberSaveable`).
 * Memory of the running app only: never stored, logged or sent. A text that can't be read gives null (the sheet starts over).
 */
object GoalDraftText {
    fun write(draft: GoalDraft): String = buildJsonObject {
        put("id", draft.id.toString())
        put("name", draft.name)
        put("kind", draft.kind.name)
        put("icon", draft.icon)
        put("color", draft.color)
        put("areaId", draft.areaId?.toString())
        put("period", draft.period.name)
        put("amount", draft.amount)
        put("unit", draft.unit.name)
        put("direction", draft.direction.name)
        put("threshold", draft.threshold)
        put("weeklyDays", draft.weeklyDays)
        put("longTerm", draft.longTerm)
        put("targetDate", draft.targetDate?.toString())
        put("habitUnit", draft.habitUnit.name)
        put("weeklyHours", draft.weeklyHours)
        put("isDeepWork", draft.isDeepWork)
        put("sources", JsonArray(draft.sources.map(GoalJson::writeSourceRule)))
    }.toString()

    fun read(text: String): GoalDraft? = runCatching {
        val json = Json.parseToJsonElement(text) as JsonObject
        fun string(name: String): String? = (json[name] as? JsonPrimitive)?.contentOrNull
        GoalDraft(
            id = UUID.fromString(string("id")),
            name = string("name").orEmpty(),
            kind = GoalKind.valueOf(string("kind")!!),
            icon = string("icon") ?: GoalDrafts.DEFAULT_ICON,
            color = string("color") ?: GoalDrafts.DEFAULT_COLOR,
            areaId = string("areaId")?.let(UUID::fromString),
            period = GoalPeriod.valueOf(string("period")!!),
            amount = string("amount").orEmpty(),
            unit = GoalUnit.valueOf(string("unit")!!),
            direction = GoalDirection.valueOf(string("direction")!!),
            threshold = string("threshold").orEmpty(),
            weeklyDays = string("weeklyDays").orEmpty(),
            longTerm = string("longTerm").orEmpty(),
            targetDate = string("targetDate")?.let(LocalDate::parse),
            habitUnit = GoalUnit.valueOf(string("habitUnit")!!),
            weeklyHours = string("weeklyHours").orEmpty(),
            sources = (json["sources"] as JsonArray).map(GoalJson::readSourceRule),
            isDeepWork = json["isDeepWork"]?.jsonPrimitive?.content == "true",
        )
    }.getOrNull()
}
