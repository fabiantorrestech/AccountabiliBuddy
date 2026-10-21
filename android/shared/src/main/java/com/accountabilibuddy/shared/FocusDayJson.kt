package com.accountabilibuddy.shared

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.put
import java.time.Instant
import java.time.LocalDate
import java.time.OffsetDateTime

/**
 * The PC's "focusDay" JSON (camelCase; dates "yyyy-MM-dd", times ISO with their offset), read and written the same way
 * so the phone can keep it as a file. Entries that can't be read are skipped; a day without a date isn't read at all.
 */
object FocusDayJson {
    fun read(json: JsonObject): FocusDay? {
        val date = json.date("date") ?: return null
        return FocusDay(
            date = date,
            parts = json.objects("parts").mapNotNull(::part),
            sessions = json.objects("sessions").mapNotNull(::session),
            drift = json.objects("drift").mapNotNull(::window),
            focusedMinutes = json.int("focusedMinutes") ?: 0,
            plannedMinutes = json.int("plannedMinutes") ?: 0,
            week = json.objects("week").mapNotNull(::total),
        )
    }

    fun write(day: FocusDay): JsonObject = buildJsonObject {
        put("date", day.date.toString())
        put("parts", buildJsonArray {
            day.parts.forEach { part ->
                add(buildJsonObject {
                    put("name", part.name)
                    put("category", part.category)
                    put("color", part.color)
                    put("kind", part.kind)
                    put("start", part.start.toString())
                    put("end", part.end.toString())
                })
            }
        })
        put("sessions", buildJsonArray {
            day.sessions.forEach { session ->
                add(buildJsonObject {
                    put("start", session.start.toString())
                    session.end?.let { put("end", it.toString()) }
                    session.profile?.let { put("profile", it) }
                    put("added", session.added)
                })
            }
        })
        put("drift", buildJsonArray {
            day.drift.forEach { window ->
                add(buildJsonObject {
                    put("start", window.start.toString())
                    put("end", window.end.toString())
                })
            }
        })
        put("focusedMinutes", day.focusedMinutes)
        put("plannedMinutes", day.plannedMinutes)
        put("week", buildJsonArray {
            day.week.forEach { total ->
                add(buildJsonObject {
                    put("date", total.date.toString())
                    put("focusedMinutes", total.focusedMinutes)
                    put("plannedMinutes", total.plannedMinutes)
                })
            }
        })
    }

    private fun part(json: JsonObject): FocusPart? {
        val start = json.instant("start") ?: return null
        val end = json.instant("end") ?: return null
        return FocusPart(
            json.string("name").orEmpty(),
            json.string("category").orEmpty(),
            json.string("color") ?: "#64748B",
            json.string("kind") ?: "rest",
            start,
            end,
        )
    }

    private fun session(json: JsonObject): FocusSession? {
        val start = json.instant("start") ?: return null
        return FocusSession(start, json.instant("end"), json.string("profile"), (json["added"] as? JsonPrimitive)?.booleanOrNull ?: false)
    }

    private fun window(json: JsonObject): TimeWindow? {
        val start = json.instant("start") ?: return null
        val end = json.instant("end") ?: return null
        return TimeWindow(start, end)
    }

    private fun total(json: JsonObject): DayTotal? {
        val date = json.date("date") ?: return null
        return DayTotal(date, json.int("focusedMinutes") ?: 0, json.int("plannedMinutes") ?: 0)
    }

    private fun JsonObject.string(name: String): String? =
        (this[name] as? JsonPrimitive)?.takeIf { it !is JsonNull && it.isString }?.content

    private fun JsonObject.int(name: String): Int? = (this[name] as? JsonPrimitive)?.intOrNull

    private fun JsonObject.date(name: String): LocalDate? = string(name)?.let { runCatching { LocalDate.parse(it) }.getOrNull() }

    private fun JsonObject.objects(name: String): List<JsonObject> = (this[name] as? JsonArray).orEmpty().filterIsInstance<JsonObject>()

    // The PC writes offsets ("-05:00"); the phone's own file has instants ("Z"). Both parse here.
    private fun JsonObject.instant(name: String): Instant? =
        string(name)?.let { runCatching { OffsetDateTime.parse(it).toInstant() }.getOrNull() }
}
