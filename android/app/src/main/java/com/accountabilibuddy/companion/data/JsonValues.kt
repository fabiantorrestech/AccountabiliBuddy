package com.accountabilibuddy.companion.data

import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonPrimitive
import java.time.DayOfWeek
import java.time.Duration
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import java.util.UUID

/**
 * Reading the Windows app's JSON values: .NET writes durations as "[d.]hh:mm:ss[.fffffff]", times of day as
 * "HH:mm:ss", dates as "yyyy-MM-dd", local date-times as "yyyy-MM-ddTHH:mm:ss", GUIDs as strings and (in the
 * profile document) enums as camelCase names.
 */
internal object JsonValues {
    private val timeSpan = Regex("""^(-)?(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?$""")

    fun JsonObject.string(name: String): String? = (this[name] as? JsonPrimitive)?.takeIf { it !is JsonNull && it.isString }?.content

    fun JsonObject.requireString(name: String): String = string(name) ?: throw ImportException("Missing \"$name\" in the file.")

    fun JsonObject.bool(name: String, default: Boolean): Boolean = (this[name] as? JsonPrimitive)?.booleanOrNull ?: default

    fun JsonObject.int(name: String): Int? = (this[name] as? JsonPrimitive)?.intOrNull

    fun JsonObject.obj(name: String): JsonObject? = this[name] as? JsonObject

    fun JsonElement?.isNullOrJsonNull(): Boolean = this == null || this is JsonNull

    fun uuid(text: String): UUID = try {
        UUID.fromString(text)
    } catch (e: IllegalArgumentException) {
        throw ImportException("\"$text\" isn't an id.", e)
    }

    fun duration(text: String): Duration {
        val match = timeSpan.matchEntire(text.trim()) ?: throw ImportException("\"$text\" isn't a duration.")
        val (sign, days, hours, minutes, seconds, fraction) = match.destructured
        var duration = Duration.ofDays(days.ifEmpty { "0" }.toLong())
            .plusHours(hours.toLong())
            .plusMinutes(minutes.toLong())
            .plusSeconds(seconds.toLong())
            .plusNanos(fraction.padEnd(9, '0').ifEmpty { "0" }.toLong())
        if (sign == "-") {
            duration = duration.negated()
        }

        return duration
    }

    fun time(text: String): LocalTime = try {
        LocalTime.parse(text.trim())
    } catch (e: Exception) {
        throw ImportException("\"$text\" isn't a time of day.", e)
    }

    fun date(text: String): LocalDate = try {
        LocalDate.parse(text.trim())
    } catch (e: Exception) {
        throw ImportException("\"$text\" isn't a date.", e)
    }

    fun dateTime(text: String): LocalDateTime = try {
        LocalDateTime.parse(text.trim().removeSuffix("Z"))
    } catch (e: Exception) {
        throw ImportException("\"$text\" isn't a date and time.", e)
    }

    fun dayOfWeek(text: String): DayOfWeek = DayOfWeek.entries.firstOrNull { it.name.equals(text, ignoreCase = true) }
        ?: throw ImportException("\"$text\" isn't a day of the week.")

    /** A camelCase enum name ("processName") or its number. */
    inline fun <reified T : Enum<T>> enumValue(element: JsonElement?, field: String): T {
        val primitive = element?.jsonPrimitive ?: throw ImportException("Missing \"$field\" in the file.")
        val values = enumValues<T>()
        primitive.intOrNull?.let { number -> values.getOrNull(number)?.let { return it } }
        return values.firstOrNull { it.name.equals(primitive.content, ignoreCase = true) }
            ?: throw ImportException("\"${primitive.content}\" isn't a known $field.")
    }
}
