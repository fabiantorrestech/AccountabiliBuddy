package com.accountabilibuddy.companion.data

import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.DayTemplate
import com.accountabilibuddy.companion.model.PlanPart
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import kotlinx.serialization.json.putJsonArray
import java.time.format.DateTimeFormatter

/**
 * Writes a day plan in the PC's profile document shape (ADR 19: what the phone sends when the plan was edited here):
 * camelCase names, enums as camelCase strings, times "HH:mm:ss", dates "yyyy-MM-dd". [ProfileDocumentParser.dayPlan]
 * reads it back.
 */
object DayPlanJson {
    private val time = DateTimeFormatter.ofPattern("HH:mm:ss")
    private val date = DateTimeFormatter.ISO_LOCAL_DATE

    fun write(plan: DayPlan): JsonObject = buildJsonObject {
        putJsonArray("categories") {
            for (category in plan.categories) {
                add(buildJsonObject {
                    put("id", category.id.toString())
                    put("name", category.name)
                    put("color", category.color)
                    put("behavior", camel(category.behavior.name))
                })
            }
        }
        putJsonArray("workdays") {
            plan.workdays.sorted().forEach { add(kotlinx.serialization.json.JsonPrimitive(camel(it.name))) }
        }
        put("workday", template(plan.workday))
        put("otherDay", template(plan.otherDay))
        putJsonArray("oneOffDays") {
            for (day in plan.oneOffDays) {
                add(buildJsonObject {
                    put("id", day.id.toString())
                    put("date", day.date.format(date))
                    put("template", template(day.template))
                    day.note?.let { put("note", it) }
                })
            }
        }
    }

    private fun template(template: DayTemplate): JsonObject = buildJsonObject {
        put("parts", buildJsonArray { template.parts.forEach { add(part(it)) } })
    }

    private fun part(part: PlanPart): JsonObject = buildJsonObject {
        put("id", part.id.toString())
        put("categoryId", part.categoryId.toString())
        put("start", part.start.format(time))
        put("end", part.end.format(time))
        part.label?.let { put("label", it) }
    }

    /** "WATCH" → "watch", "MONDAY" → "monday", "Rest" → "rest": the PC's camelCase enum names. */
    private fun camel(name: String): String = name.lowercase()
}
