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
 * The pieces are shared with [WatchSnapshotJson].
 */
object FocusDayJson {
    fun read(json: JsonObject): FocusDay? {
        val date = json.date("date") ?: return null
        return FocusDay(
            date = date,
            parts = json.objects("parts").mapNotNull(::readPart),
            sessions = json.objects("sessions").mapNotNull(::readSession),
            drift = json.objects("drift").mapNotNull(::readWindow),
            focusedMinutes = json.int("focusedMinutes") ?: 0,
            plannedMinutes = json.int("plannedMinutes") ?: 0,
            week = json.objects("week").mapNotNull(::readTotal),
        )
    }

    fun write(day: FocusDay): JsonObject = buildJsonObject {
        put("date", day.date.toString())
        put("parts", buildJsonArray { day.parts.forEach { add(writePart(it)) } })
        put("sessions", buildJsonArray { day.sessions.forEach { add(writeSession(it)) } })
        put("drift", buildJsonArray { day.drift.forEach { add(writeWindow(it)) } })
        put("focusedMinutes", day.focusedMinutes)
        put("plannedMinutes", day.plannedMinutes)
        put("week", buildJsonArray { day.week.forEach { add(writeTotal(it)) } })
    }

    internal fun writePart(part: FocusPart): JsonObject = buildJsonObject {
        put("name", part.name)
        put("category", part.category)
        put("color", part.color)
        put("kind", part.kind)
        put("start", part.start.toString())
        put("end", part.end.toString())
    }

    internal fun readPart(json: JsonObject): FocusPart? {
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

    internal fun writeSession(session: FocusSession): JsonObject = buildJsonObject {
        put("start", session.start.toString())
        session.end?.let { put("end", it.toString()) }
        session.profile?.let { put("profile", it) }
        put("added", session.added)
    }

    internal fun readSession(json: JsonObject): FocusSession? {
        val start = json.instant("start") ?: return null
        return FocusSession(start, json.instant("end"), json.string("profile"), json.bool("added"))
    }

    internal fun writeWindow(window: TimeWindow): JsonObject = buildJsonObject {
        put("start", window.start.toString())
        put("end", window.end.toString())
    }

    internal fun readWindow(json: JsonObject): TimeWindow? {
        val start = json.instant("start") ?: return null
        val end = json.instant("end") ?: return null
        return TimeWindow(start, end)
    }

    internal fun writeTotal(total: DayTotal): JsonObject = buildJsonObject {
        put("date", total.date.toString())
        put("focusedMinutes", total.focusedMinutes)
        put("plannedMinutes", total.plannedMinutes)
    }

    internal fun readTotal(json: JsonObject): DayTotal? {
        val date = json.date("date") ?: return null
        return DayTotal(date, json.int("focusedMinutes") ?: 0, json.int("plannedMinutes") ?: 0)
    }

    internal fun JsonObject.string(name: String): String? =
        (this[name] as? JsonPrimitive)?.takeIf { it !is JsonNull && it.isString }?.content

    internal fun JsonObject.int(name: String): Int? = (this[name] as? JsonPrimitive)?.intOrNull

    internal fun JsonObject.bool(name: String): Boolean = (this[name] as? JsonPrimitive)?.booleanOrNull ?: false

    internal fun JsonObject.date(name: String): LocalDate? = string(name)?.let { runCatching { LocalDate.parse(it) }.getOrNull() }

    internal fun JsonObject.objects(name: String): List<JsonObject> = (this[name] as? JsonArray).orEmpty().filterIsInstance<JsonObject>()

    // The PC writes offsets ("-05:00"); the phone's own files have instants ("Z"). Both parse here.
    internal fun JsonObject.instant(name: String): Instant? =
        string(name)?.let { runCatching { OffsetDateTime.parse(it).toInstant() }.getOrNull() }
}
