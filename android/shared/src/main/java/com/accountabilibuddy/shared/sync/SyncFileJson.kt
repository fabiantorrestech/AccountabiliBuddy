package com.accountabilibuddy.shared.sync

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.put
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.time.format.DateTimeFormatterBuilder
import java.time.format.DateTimeParseException
import java.time.temporal.ChronoField
import java.util.Locale
import java.util.UUID

/**
 * A [SyncFile] as JSON, in the PC's shape (System.Text.Json with LinkFraming.JsonOptions): the same camelCase names in
 * the same order, nulls written, "seen" as an object keyed by device id, and times as .NET writes a DateTimeOffset
 * ("2026-10-21T20:30:00.25+00:00": seconds always, a fraction of up to 7 digits only when there is one, UTC as
 * "+00:00"). Reading takes any ISO time with an offset, "Z" too.
 *
 * Reading refuses what the PC refuses: a missing or null list, card field, drift app, plan field or document field, a
 * value of the wrong type, the empty device id, a device id not in the 36-character form, a format version outside 1 to
 * [SyncFile.CURRENT_FORMAT_VERSION]. A missing time is refused too (the PC would read it as year 1). Unknown properties
 * are ignored, and unknown kinds, methods and document keys are kept (a newer app within format 1).
 */
object SyncFileJson {
    // .NET keeps 100 ns ticks: a finer fraction is cut, as the PC would keep it.
    private val dotNetTime: DateTimeFormatter = DateTimeFormatterBuilder()
        .appendPattern("uuuu-MM-dd'T'HH:mm:ss")
        .appendFraction(ChronoField.NANO_OF_SECOND, 0, 7, true)
        .appendOffset("+HH:MM", "+00:00")
        .toFormatter(Locale.ROOT)

    private val deviceIdForm = Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")
    private val emptyDeviceId = UUID(0, 0)

    fun write(file: SyncFile): JsonObject = buildJsonObject {
        put("formatVersion", file.formatVersion)
        put("device", writeDevice(file.device))
        put("sessions", JsonArray(file.sessions.map(::writeSession)))
        put("drift", JsonArray(file.drift.map(::writeDrift)))
        put("dayPlan", file.dayPlan?.let(::writeDayPlan) ?: JsonNull)
        put("documents", JsonArray(file.documents.map(::writeDocument)))
    }

    /** Null when the JSON isn't a complete sync file this app can read (see the class comment). */
    fun read(json: JsonObject): SyncFile? = try {
        readFile(json)
    } catch (e: NotASyncFileException) {
        null
    }

    private fun writeDevice(device: SyncDeviceCard): JsonObject = buildJsonObject {
        put("deviceId", device.deviceId.toString())
        put("name", device.name)
        put("kind", device.kind)
        put("appVersion", device.appVersion)
        put("writtenAt", writeTime(device.writtenAt))
        put("methods", JsonArray(device.methods.map { JsonPrimitive(it) }))
        put("historyOff", device.historyOff)
        put("seen", JsonObject(device.seen.entries.associate { (id, seen) -> id.toString() to JsonPrimitive(writeTime(seen)) }))
    }

    private fun writeSession(session: SyncSession): JsonObject = buildJsonObject {
        put("start", writeTime(session.start))
        put("end", session.end?.let(::writeTime))
        put("profile", session.profile)
        put("added", session.added)
    }

    private fun writeDrift(drift: SyncDrift): JsonObject = buildJsonObject {
        put("start", writeTime(drift.start))
        put("end", writeTime(drift.end))
        put("app", drift.app)
        put("site", drift.site)
        put("section", drift.section)
    }

    private fun writeDayPlan(plan: SyncDayPlan): JsonObject = buildJsonObject {
        put("planJson", plan.planJson)
        put("version", plan.version)
        put("editedAt", writeTime(plan.editedAt))
        put("basedOn", plan.basedOn)
    }

    private fun writeDocument(document: SyncDocument): JsonObject = buildJsonObject {
        put("key", document.key)
        put("json", document.json)
        put("editedAt", writeTime(document.editedAt))
    }

    private fun writeTime(time: Instant): String = dotNetTime.format(time.atOffset(ZoneOffset.UTC))

    private fun readFile(json: JsonObject): SyncFile {
        val formatVersion = json.requiredInt("formatVersion")
        if (formatVersion < 1 || formatVersion > SyncFile.CURRENT_FORMAT_VERSION) {
            throw NotASyncFileException()
        }

        return SyncFile(
            formatVersion,
            readDevice(json.requiredObject("device")),
            json.requiredObjects("sessions").map(::readSession),
            json.requiredObjects("drift").map(::readDrift),
            json.optionalObject("dayPlan")?.let(::readDayPlan),
            json.requiredObjects("documents").map(::readDocument),
        )
    }

    private fun readDevice(json: JsonObject): SyncDeviceCard {
        val deviceId = readDeviceId(json.requiredString("deviceId"))
        if (deviceId == emptyDeviceId) {
            throw NotASyncFileException()
        }

        return SyncDeviceCard(
            deviceId,
            json.requiredString("name"),
            json.requiredString("kind"),
            json.requiredString("appVersion"),
            json.requiredTime("writtenAt"),
            json.requiredStrings("methods"),
            json.flag("historyOff"),
            json.requiredObject("seen").entries.associate { (id, seen) -> readDeviceId(id) to readTime(stringOf(seen)) },
        )
    }

    private fun readSession(json: JsonObject): SyncSession = SyncSession(
        json.requiredTime("start"),
        json.optionalTime("end"),
        json.optionalString("profile"),
        json.flag("added"),
    )

    private fun readDrift(json: JsonObject): SyncDrift = SyncDrift(
        json.requiredTime("start"),
        json.requiredTime("end"),
        json.requiredString("app"),
        json.optionalString("site"),
        json.optionalString("section"),
    )

    private fun readDayPlan(json: JsonObject): SyncDayPlan = SyncDayPlan(
        json.requiredString("planJson"),
        json.requiredString("version"),
        json.requiredTime("editedAt"),
        json.optionalString("basedOn"),
    )

    private fun readDocument(json: JsonObject): SyncDocument = SyncDocument(
        json.requiredString("key"),
        json.requiredString("json"),
        json.requiredTime("editedAt"),
    )

    // The PC reads a Guid only in its 36-character form (upper or lower case); java.util.UUID would take shorter ones.
    private fun readDeviceId(text: String): UUID =
        if (deviceIdForm.matches(text)) UUID.fromString(text) else throw NotASyncFileException()

    private fun readTime(text: String): Instant = try {
        OffsetDateTime.parse(text).toInstant()
    } catch (e: DateTimeParseException) {
        throw NotASyncFileException()
    }

    /** A JSON string's text; anything else (null, a number, an object) isn't. */
    private fun stringOf(value: JsonElement?): String =
        (value as? JsonPrimitive)?.takeIf { it !is JsonNull && it.isString }?.content ?: throw NotASyncFileException()

    private fun JsonObject.requiredString(name: String): String = stringOf(this[name])

    private fun JsonObject.optionalString(name: String): String? = when (val value = this[name]) {
        null, JsonNull -> null
        else -> stringOf(value)
    }

    private fun JsonObject.requiredTime(name: String): Instant = readTime(requiredString(name))

    private fun JsonObject.optionalTime(name: String): Instant? = optionalString(name)?.let(::readTime)

    // As the PC's reader: a number, or a number in a string.
    private fun JsonObject.requiredInt(name: String): Int =
        (this[name] as? JsonPrimitive)?.takeIf { it !is JsonNull }?.intOrNull ?: throw NotASyncFileException()

    // As the PC's reader: missing is false; null or anything but true and false isn't read.
    private fun JsonObject.flag(name: String): Boolean = when (val value = this[name]) {
        null -> false
        is JsonPrimitive -> value.takeIf { it !is JsonNull && !it.isString }?.booleanOrNull ?: throw NotASyncFileException()
        else -> throw NotASyncFileException()
    }

    private fun JsonObject.requiredObject(name: String): JsonObject = this[name] as? JsonObject ?: throw NotASyncFileException()

    private fun JsonObject.optionalObject(name: String): JsonObject? = when (val value = this[name]) {
        null, JsonNull -> null
        is JsonObject -> value
        else -> throw NotASyncFileException()
    }

    private fun JsonObject.requiredArray(name: String): JsonArray = this[name] as? JsonArray ?: throw NotASyncFileException()

    private fun JsonObject.requiredObjects(name: String): List<JsonObject> =
        requiredArray(name).map { it as? JsonObject ?: throw NotASyncFileException() }

    private fun JsonObject.requiredStrings(name: String): List<String> = requiredArray(name).map(::stringOf)

    /** Thrown inside [read] at the first thing that can't be read; [read] turns it into null. */
    private class NotASyncFileException : Exception()
}
