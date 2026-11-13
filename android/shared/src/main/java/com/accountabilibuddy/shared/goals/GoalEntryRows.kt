package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.DotNetTime
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.time.YearMonth
import java.util.Base64
import java.util.UUID
import kotlinx.serialization.ExperimentalSerializationApi
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.JsonUnquotedLiteral
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

/**
 * Goal entries as compact rows in a sync file and the history files (the PC's `GoalEntryRows`): `{i,g,d,t,v,s,w,e,x,o,p}` in that
 * order. Ids are base64url (22 chars), `d` is yyyyMMdd, `t` a time of day in 100 ns ticks, `e` / `x` UTC .NET ticks and `o` the offset
 * in minutes, `s` a short code (hand left out), defaults left out, `i` left out when it is the derived id. An entry a row can't hold
 * exactly (unknown fields, two offsets, a source without a code) is written as the object. A row with a field or code this version
 * doesn't know throws [GoalNewerValueException] (kept as written).
 */
object GoalEntryRows {
    private const val UNIX_EPOCH_TICKS = 621_355_968_000_000_000L
    private const val MAX_TICKS = 3_155_378_975_999_999_999L
    private const val TICKS_PER_MINUTE = 600_000_000L
    private const val TICKS_PER_DAY = 864_000_000_000L
    private val NAMES = setOf("i", "g", "d", "t", "v", "s", "w", "e", "x", "o", "p")
    private val CODES = listOf(
        GoalEntrySource.HealthConnect to "hc",
        GoalEntrySource.ScreenTime to "st",
        GoalEntrySource.Beeminder to "bm",
        GoalEntrySource.Calendar to "ca",
        GoalEntrySource.FocusLocal to "fl",
    )

    /** Whether the item is a row (it has a `g`). */
    fun isRow(json: JsonElement): Boolean = json is JsonObject && json.containsKey("g")

    /** Whether a row holds the entry exactly. */
    fun fitsRow(entry: GoalEntry): Boolean {
        if (entry.extra.isNotEmpty() || (codeOf(entry.source) == null && entry.source != GoalEntrySource.Hand)) return false
        val offset = entry.editedAt.offset.totalSeconds
        return offset % 60 == 0 && (entry.deletedAt == null || entry.deletedAt.offset.totalSeconds == offset)
    }

    fun codeOf(source: GoalEntrySource): String? = CODES.firstOrNull { it.first == source }?.second

    /** An id as 22 base64url characters of its RFC 4122 bytes. */
    fun guidText(id: UUID): String {
        val bytes = java.nio.ByteBuffer.allocate(16).putLong(id.mostSignificantBits).putLong(id.leastSignificantBits).array()
        return Base64.getUrlEncoder().withoutPadding().encodeToString(bytes)
    }

    fun readGuid(text: String?): UUID? {
        if (text == null || text.length != 22 || text.any { !(it in 'a'..'z' || it in 'A'..'Z' || it in '0'..'9' || it == '-' || it == '_') }) return null
        return try {
            val bytes = Base64.getUrlDecoder().decode(text)
            if (bytes.size != 16) return null
            val buffer = java.nio.ByteBuffer.wrap(bytes)
            UUID(buffer.long, buffer.long)
        } catch (e: IllegalArgumentException) {
            null
        }
    }

    /** A moment as UTC .NET ticks. */
    fun ticksOf(time: OffsetDateTime): Long = DotNetTime.ticks(time)

    /** The entry as the list writes it: a row when it fits, else the object. */
    fun write(entry: GoalEntry): JsonObject {
        if (!fitsRow(entry)) return GoalJson.writeEntry(entry)
        return buildJsonObject {
            if (!GoalDerivedIds.isDerivedId(entry)) put("i", guidText(entry.id))
            put("g", guidText(entry.goalId))
            put("d", dayNumber(entry.day))
            entry.time?.let { put("t", it.toNanoOfDay() / 100) }
            if (!isPositiveZero(entry.value)) put("v", number(entry.value))
            codeOf(entry.source)?.let { put("s", it) }
            entry.deviceId?.let { put("w", guidText(it)) }
            put("e", ticksOf(entry.editedAt))
            entry.deletedAt?.let { put("x", ticksOf(it)) }
            val offsetMinutes = entry.editedAt.offset.totalSeconds / 60
            if (offsetMinutes != 0) put("o", offsetMinutes)
            if (!isPositiveZero(entry.position)) put("p", number(entry.position))
        }
    }

    /** Reads a row. Throws [GoalNewerValueException] for a field or code this version doesn't know, [IllegalArgumentException] for damage. */
    fun read(row: JsonObject): GoalEntry {
        for (name in row.keys) {
            if (name !in NAMES) throw GoalNewerValueException("A goal entry row field this app doesn't know: $name.")
        }
        var source = GoalEntrySource.Hand
        row["s"]?.let { value ->
            val code = (value as? JsonPrimitive)?.takeIf { it.isString }?.content ?: throw IllegalArgumentException("Row source.")
            source = CODES.firstOrNull { it.second == code }?.first
                ?: throw GoalNewerValueException("A goal entry source this app doesn't know: $code.")
        }
        val goalId = id(row, "g") ?: throw IllegalArgumentException("Row goal id.")
        val day = day(row)
        val clock = row["t"]?.let { value ->
            val ticks = long(value)
            if (ticks != null && ticks >= 0 && ticks < TICKS_PER_DAY) LocalTime.ofNanoOfDay(ticks * 100) else throw IllegalArgumentException("Row time.")
        }
        val value = row["v"]?.let(::number) ?: 0.0
        val device = if (row.containsKey("w")) id(row, "w") ?: throw IllegalArgumentException("Row device.") else null
        val offsetMinutes = row["o"]?.let { value2 ->
            val minutes = long(value2)
            if (minutes != null && minutes >= -14 * 60 && minutes <= 14 * 60) minutes.toInt() else throw IllegalArgumentException("Row offset.")
        } ?: 0
        val editedAt = time(row, "e", offsetMinutes) ?: throw IllegalArgumentException("Row edit time.")
        val deletedAt = if (row.containsKey("x")) time(row, "x", offsetMinutes) ?: throw IllegalArgumentException("Row deletion time.") else null
        val position = row["p"]?.let(::number) ?: 0.0
        val id = if (row.containsKey("i")) {
            id(row, "i") ?: throw IllegalArgumentException("Row id.")
        } else {
            derivedId(goalId, source, device, day) ?: throw IllegalArgumentException("A row without an id must be a device's derived value.")
        }
        return GoalEntry(id, goalId, day, clock, value, source, editedAt, position, deletedAt, deviceId = device)
    }

    /** A row's id, edit and deletion times and place, read no further (a newer app's row); null when they don't read. */
    fun stamps(row: JsonObject): RowStamps? = try {
        val id = if (row.containsKey("i")) id(row, "i") else rebuiltId(row)
        val minutes = row["o"]?.let { long(it) }?.takeIf { it >= -14 * 60 && it <= 14 * 60 }?.toInt() ?: 0
        val editedAt = time(row, "e", minutes)
        if (id == null || editedAt == null) {
            null
        } else {
            val deletedAt = if (row.containsKey("x")) time(row, "x", minutes) else null
            val place = (row["p"] as? JsonPrimitive)?.takeIf { !it.isString && it !is JsonNull }?.content?.toDoubleOrNull() ?: 0.0
            RowStamps(id, editedAt, deletedAt, place)
        }
    } catch (e: IllegalArgumentException) {
        null
    }

    /** What the merge needs of a row. */
    data class RowStamps(val id: UUID, val editedAt: OffsetDateTime, val deletedAt: OffsetDateTime?, val position: Double)

    /** The day as yyyyMMdd. */
    fun dayNumber(day: LocalDate): Int = day.year * 10000 + day.monthValue * 100 + day.dayOfMonth

    private fun derivedId(goalId: UUID, source: GoalEntrySource, device: UUID?, day: LocalDate): UUID? =
        if (GoalDerivedWriter.isDerived(source) && device != null) GoalDerivedIds.forEntry(goalId, source, device, day) else null

    // The id of a row without "i": rebuilt from g, s, w and d when s is a known code of a device-measured source.
    private fun rebuiltId(row: JsonObject): UUID? {
        val code = (row["s"] as? JsonPrimitive)?.takeIf { it.isString }?.content ?: return null
        val source = CODES.firstOrNull { it.second == code }?.first ?: return null
        val goalId = id(row, "g")
        val device = id(row, "w")
        return if (goalId != null) derivedId(goalId, source, device, day(row)) else null
    }

    // 0 is a default (left out); a negative zero is a value of its own (written -0).
    private fun isPositiveZero(value: Double): Boolean = value == 0.0 && 1.0 / value > 0

    @OptIn(ExperimentalSerializationApi::class)
    private fun number(value: Double): JsonPrimitive = if (value == 0.0) JsonUnquotedLiteral("-0") else GoalJson.number(value)

    private fun day(row: JsonObject): LocalDate {
        val number = row["d"]?.let(::long)
        if (number == null || number < 10101 || number > 99991231) throw IllegalArgumentException("Row day.")
        val year = (number / 10000).toInt()
        val month = (number / 100 % 100).toInt()
        val dayOfMonth = (number % 100).toInt()
        if (month < 1 || month > 12 || dayOfMonth < 1 || dayOfMonth > YearMonth.of(year, month).lengthOfMonth()) throw IllegalArgumentException("Row day.")
        return LocalDate.of(year, month, dayOfMonth)
    }

    private fun id(row: JsonObject, name: String): UUID? = (row[name] as? JsonPrimitive)?.takeIf { it.isString }?.let { readGuid(it.content) }

    private fun time(row: JsonObject, name: String, offsetMinutes: Int): OffsetDateTime? {
        val ticks = row[name]?.let(::long) ?: return null
        val local = ticks + offsetMinutes * TICKS_PER_MINUTE
        if (ticks < 0 || ticks > MAX_TICKS || local < 0 || local > MAX_TICKS) return null
        val sinceEpoch = ticks - UNIX_EPOCH_TICKS
        val instant = Instant.ofEpochSecond(Math.floorDiv(sinceEpoch, 10_000_000L), Math.floorMod(sinceEpoch, 10_000_000L) * 100)
        return OffsetDateTime.ofInstant(instant, ZoneOffset.ofTotalSeconds(offsetMinutes * 60))
    }

    private fun long(value: JsonElement): Long? = (value as? JsonPrimitive)?.takeIf { !it.isString && it !is JsonNull }?.content?.toLongOrNull()

    private fun number(value: JsonElement): Double {
        val primitive = value as? JsonPrimitive
        val number = primitive?.takeIf { !it.isString && it !is JsonNull }?.content?.toDoubleOrNull()
        if (number == null || number.isNaN() || number.isInfinite()) throw IllegalArgumentException("Row number.")
        return number
    }
}
