package com.accountabilibuddy.shared.goals

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.put
import java.math.BigDecimal
import java.math.MathContext
import java.math.RoundingMode
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.format.DateTimeFormatter
import java.time.format.DateTimeFormatterBuilder
import java.time.format.DateTimeParseException
import java.time.temporal.ChronoField
import java.util.Locale
import java.util.UUID
import kotlin.math.abs
import kotlin.math.floor

/**
 * The goal model as JSON, in the PC's shape (Core's goal records through System.Text.Json with `SyncJson.Options`): the same
 * camelCase names in the same order, enums by name, an optional field left out when it is null (`madeInProductaBuddy` when
 * false), whole numbers written without a fraction, dates "yyyy-MM-dd", times of day "HH:mm:ss" and times as .NET writes a
 * DateTimeOffset (seconds always, a fraction only when there is one, the offset kept, UTC as "+00:00").
 *
 * Reading takes what the PC's reader takes: names in any case, an enum by name (any case) or by its number (older numbered
 * weekdays), a number written as text, a missing optional field as its default. A required field that is missing, null or of
 * the wrong kind throws [IllegalArgumentException]. Also reads the synced session pieces goals count from ([readPiece]).
 */
object GoalJson {
    // .NET keeps 100 ns ticks: a finer fraction is cut, as the PC would keep it.
    private val dotNetTime: DateTimeFormatter = DateTimeFormatterBuilder()
        .appendPattern("uuuu-MM-dd'T'HH:mm:ss")
        .appendFraction(ChronoField.NANO_OF_SECOND, 0, 7, true)
        .appendOffset("+HH:MM", "+00:00")
        .toFormatter(Locale.ROOT)

    // .NET writes a TimeOnly with all 7 fraction digits when it has a fraction ("08:15:30.5000000"), none otherwise.
    private val dotNetTimeOfDay: DateTimeFormatter = DateTimeFormatter.ofPattern("HH:mm:ss", Locale.ROOT)
    private val dotNetTimeOfDayFraction: DateTimeFormatter = DateTimeFormatter.ofPattern("HH:mm:ss.SSSSSSS", Locale.ROOT)

    // A time as [time] writes it: the one string kind .NET writes without its encoder (a "+05:30" offset is not escaped). A
    // private text that happens to be exactly such a time would come out unescaped here (Q424).
    private val dotNetTimeText = Regex("""\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?[+-]\d{2}:\d{2}""")

    // Printable ASCII that .NET's default encoder (JavaScriptEncoder.Default) still escapes as \uXXXX.
    private const val ESCAPED_ASCII = "\"&'+<>`"

    // System.Text.Json reads a Guid only in its 36-character form.
    private val guidForm = Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")

    // ---------------------------------------------------------------- writing

    fun writeArea(area: GoalArea): JsonObject = buildJsonObject {
        put("id", area.id.toString())
        put("name", area.name)
        put("icon", area.icon)
        put("color", area.color)
        put("position", number(area.position))
        put("editedAt", time(area.editedAt))
        area.deletedAt?.let { put("deletedAt", time(it)) }
    }

    fun writeGoal(goal: Goal): JsonObject = buildJsonObject {
        put("id", goal.id.toString())
        put("areaId", goal.areaId.toString())
        put("name", goal.name)
        put("icon", goal.icon)
        put("color", goal.color)
        put("kind", goal.kind.name)
        put("target", writeTarget(goal.target))
        put("sources", JsonArray(goal.sources.map(::writeSourceRule)))
        put("createdAt", time(goal.createdAt))
        put("position", number(goal.position))
        put("editedAt", time(goal.editedAt))
        put("builtIn", goal.builtIn)
        goal.secondTarget?.let { put("secondTarget", writeTarget(it)) }
        goal.pauses?.let { pauses -> put("pauses", JsonArray(pauses.map(::writePause))) }
        put("showOnToday", goal.showOnToday)
        goal.extraTiles?.let { put("extraTiles", writeExtraTiles(it)) }
        goal.notes?.let { put("notes", it) }
        goal.deletedAt?.let { put("deletedAt", time(it)) }
    }

    fun writeTarget(target: GoalTarget): JsonObject = buildJsonObject {
        put("period", target.period.name)
        target.amount?.let { put("amount", number(it)) }
        put("unit", target.unit.name)
        put("direction", target.direction.name)
        target.targetAmount?.let { put("targetAmount", number(it)) }
        target.targetDate?.let { put("targetDate", it.toString()) }
        target.weeklyDays?.let { put("weeklyDays", it) }
        target.missesAllowed?.let { put("missesAllowed", it) }
        target.pendingChange?.let { put("pendingChange", writePendingChange(it)) }
    }

    fun writePendingChange(change: GoalPendingChange): JsonObject = buildJsonObject {
        change.amount?.let { put("amount", number(it)) }
        put("appliesAt", time(change.appliesAt))
        if (change.madeInProductaBuddy) {
            put("madeInProductaBuddy", true)
        }
    }

    fun writeSourceRule(rule: GoalSourceRule): JsonObject = buildJsonObject {
        put("id", rule.id.toString())
        put("type", rule.type.name)
        rule.labelId?.let { put("labelId", it.toString()) }
        rule.tag?.let { put("tag", it) }
        rule.calendarCategoryId?.let { put("calendarCategoryId", it.toString()) }
        rule.app?.let { put("app", it) }
        rule.screenDirection?.let { put("screenDirection", it.name) }
        rule.beeminderSlug?.let { put("beeminderSlug", it) }
        rule.beeminderDirection?.let { put("beeminderDirection", it.name) }
        rule.thresholdMinutes?.let { put("thresholdMinutes", number(it)) }
        put("automatic", rule.automatic)
    }

    fun writePause(pause: GoalPauseStretch): JsonObject = buildJsonObject {
        put("from", pause.from.toString())
        pause.resumedOn?.let { put("resumedOn", it.toString()) }
    }

    fun writeExtraTiles(tiles: GoalExtraTiles): JsonObject = buildJsonObject {
        put("streak", tiles.streak)
        put("total", tiles.total)
    }

    fun writeEntry(entry: GoalEntry): JsonObject = buildJsonObject {
        put("id", entry.id.toString())
        put("goalId", entry.goalId.toString())
        put("day", entry.day.toString())
        entry.time?.let { put("time", timeOfDay(it)) }
        put("value", number(entry.value))
        put("source", entry.source.name)
        put("editedAt", time(entry.editedAt))
        put("position", number(entry.position))
        entry.deletedAt?.let { put("deletedAt", time(it)) }
    }

    fun writeReminder(reminder: GoalReminder): JsonObject = buildJsonObject {
        put("id", reminder.id.toString())
        put("goalId", reminder.goalId.toString())
        put("days", reminder.days.name)
        put("time", timeOfDay(reminder.time))
        put("position", number(reminder.position))
        put("editedAt", time(reminder.editedAt))
        reminder.weekdays?.let { days -> put("weekdays", buildJsonArray { days.forEach { add(JsonPrimitive(it.name)) } }) }
        reminder.date?.let { put("date", it.toString()) }
        put("repeat", reminder.repeat)
        reminder.reason?.let { put("reason", it) }
        put("skipWhenDone", reminder.skipWhenDone)
        put("nudgeWhenBehind", reminder.nudgeWhenBehind)
        reminder.deletedAt?.let { put("deletedAt", time(it)) }
    }

    fun writeReview(review: GoalReviewRecord): JsonObject = buildJsonObject {
        put("id", review.id.toString())
        put("week", review.week.toString())
        put("decisions", JsonArray(review.decisions.map(::writeReviewDecision)))
        put("position", number(review.position))
        put("editedAt", time(review.editedAt))
        review.snoozedUntil?.let { put("snoozedUntil", time(it)) }
        review.notes?.let { put("notes", it) }
        review.deletedAt?.let { put("deletedAt", time(it)) }
    }

    fun writeReviewDecision(decision: GoalReviewDecision): JsonObject = buildJsonObject {
        put("goalId", decision.goalId.toString())
        put("choice", decision.choice.name)
    }

    /** A time as .NET writes a DateTimeOffset ("2026-11-11T18:00:00-05:00"). */
    fun time(time: OffsetDateTime): String = dotNetTime.format(time)

    /** A time of day as .NET writes a TimeOnly ("08:00:00", "08:15:30.5000000"). */
    fun timeOfDay(time: LocalTime): String = (if (time.nano == 0) dotNetTimeOfDay else dotNetTimeOfDayFraction).format(time)

    /** A number as .NET writes a double: whole numbers below 1E+17 without a fraction ("120", not "120.0"); see [doubleText] for the rest. */
    fun number(value: Double): JsonPrimitive =
        if (!value.isNaN() && !value.isInfinite() && value == floor(value) && abs(value) < 1e17) JsonPrimitive(value.toLong()) else JsonPrimitive(value)

    // ---------------------------------------------------------------- the stored lists (goals.items, areas, entries, reminders, reviews)

    fun writeGoals(goals: List<Goal>): JsonArray = JsonArray(goals.map(::writeGoal))

    fun writeAreas(areas: List<GoalArea>): JsonArray = JsonArray(areas.map(::writeArea))

    fun writeEntries(entries: List<GoalEntry>): JsonArray = JsonArray(entries.map(::writeEntry))

    fun writeReminders(reminders: List<GoalReminder>): JsonArray = JsonArray(reminders.map(::writeReminder))

    fun writeReviews(reviews: List<GoalReviewRecord>): JsonArray = JsonArray(reviews.map(::writeReview))

    fun readGoals(json: JsonElement): List<Goal> = list(json, "goals").map(::readGoal)

    fun readAreas(json: JsonElement): List<GoalArea> = list(json, "areas").map(::readArea)

    fun readEntries(json: JsonElement): List<GoalEntry> = list(json, "entries").map(::readEntry)

    fun readReminders(json: JsonElement): List<GoalReminder> = list(json, "reminders").map(::readReminder)

    fun readReviews(json: JsonElement): List<GoalReviewRecord> = list(json, "reviews").map(::readReview)

    // ---------------------------------------------------------------- text

    /**
     * Compact JSON text exactly as System.Text.Json writes it with the PC's options, byte for byte: no spaces; in strings only
     * printable ASCII is written as is, except [ESCAPED_ASCII] (as \uXXXX), the backslash (two of them) and the short escapes
     * for a new line, carriage return, tab, backspace and form feed; everything else as \uXXXX (upper-case hex, two for an
     * emoji); doubles as .NET's shortest round-trip text ([doubleText]).
     */
    fun text(json: JsonElement): String = StringBuilder().also { appendText(it, json) }.toString()

    /** A double as .NET Core writes it: the shortest digits that read back the same, plain from 1E-04 to below 1E+17, else "1.5E-07". */
    fun doubleText(value: Double): String {
        require(!value.isNaN() && !value.isInfinite()) { "JSON has no NaN or infinity" }
        if (value == 0.0) {
            return if (1.0 / value < 0) "-0" else "0"
        }

        val decimal = shortest(value)
        val digits = decimal.unscaledValue().abs().toString()
        val exponent = digits.length - 1 - decimal.scale()
        if (exponent in -4..16) {
            return decimal.toPlainString()
        }

        val mantissa = if (digits.length == 1) digits else digits[0] + "." + digits.substring(1)
        val sign = if (value < 0) "-" else ""
        return sign + mantissa + "E" + (if (exponent < 0) "-" else "+") + abs(exponent).toString().padStart(2, '0')
    }

    // The fewest significant digits that read back as the same double, the nearest such decimal (as .NET Core picks them;
    // Java's Double.toString keeps two digits for some values, e.g. 4.9E-324 where .NET writes 5E-324).
    private fun shortest(value: Double): BigDecimal {
        val exact = BigDecimal(value)
        for (precision in 1..17) {
            val rounded = exact.round(MathContext(precision, RoundingMode.HALF_EVEN))
            if (rounded.toDouble() == value) {
                return rounded.stripTrailingZeros()
            }
        }

        return exact.round(MathContext(17, RoundingMode.HALF_EVEN)).stripTrailingZeros()
    }

    private fun appendText(out: StringBuilder, json: JsonElement) {
        when (json) {
            is JsonObject -> {
                out.append('{')
                json.entries.forEachIndexed { index, (key, value) ->
                    if (index > 0) out.append(',')
                    appendString(out, key)
                    out.append(':')
                    appendText(out, value)
                }
                out.append('}')
            }

            is JsonArray -> {
                out.append('[')
                json.forEachIndexed { index, value ->
                    if (index > 0) out.append(',')
                    appendText(out, value)
                }
                out.append(']')
            }

            is JsonNull -> out.append("null")
            is JsonPrimitive -> when {
                !json.isString -> out.append(literalText(json.content))
                // .NET writes its dates and times straight, not through the string encoder (so a time's "+" stays "+").
                dotNetTimeText.matches(json.content) -> out.append('"').append(json.content).append('"')
                else -> appendString(out, json.content)
            }
        }
    }

    // true / false / a whole number as they are; a double (it has a point or an exponent) as .NET writes it.
    private fun literalText(content: String): String {
        if (content == "true" || content == "false" || content.none { it == '.' || it == 'e' || it == 'E' }) {
            return content
        }

        return doubleText(content.toDouble())
    }

    private fun appendString(out: StringBuilder, text: String) {
        out.append('"')
        for (char in text) {
            when {
                char == '\\' -> out.append("\\\\")
                char == '\n' -> out.append("\\n")
                char == '\r' -> out.append("\\r")
                char == '\t' -> out.append("\\t")
                char == '\b' -> out.append("\\b")
                char == '\u000C' -> out.append("\\f")
                char in ' '..'~' && char !in ESCAPED_ASCII -> out.append(char)
                else -> out.append("\\u").append(String.format(Locale.ROOT, "%04X", char.code))
            }
        }
        out.append('"')
    }

    // ---------------------------------------------------------------- reading

    fun readArea(json: JsonElement): GoalArea {
        val obj = obj(json, "area")
        return GoalArea(
            obj.requiredGuid("id"),
            obj.optionalText("name") ?: "",
            obj.optionalText("icon") ?: "",
            obj.optionalText("color") ?: "",
            obj.optionalNumber("position") ?: 0.0,
            obj.requiredTime("editedAt"),
            obj.optionalTime("deletedAt"),
        )
    }

    fun readGoal(json: JsonElement): Goal {
        val obj = obj(json, "goal")
        return Goal(
            obj.requiredGuid("id"),
            obj.requiredGuid("areaId"),
            obj.optionalText("name") ?: "",
            obj.optionalText("icon") ?: "",
            obj.optionalText("color") ?: "",
            obj.requiredEnum<GoalKind>("kind"),
            readTarget(obj.required("target")),
            obj.optionalArray("sources")?.map(::readSourceRule) ?: emptyList(),
            obj.requiredTime("createdAt"),
            obj.optionalNumber("position") ?: 0.0,
            obj.requiredTime("editedAt"),
            obj.optionalBool("builtIn") ?: false,
            obj.value("secondTarget")?.let(::readTarget),
            obj.optionalArray("pauses")?.map(::readPause),
            obj.optionalBool("showOnToday") ?: false,
            obj.value("extraTiles")?.let(::readExtraTiles),
            obj.optionalText("notes"),
            obj.optionalTime("deletedAt"),
        )
    }

    fun readTarget(json: JsonElement): GoalTarget {
        val obj = obj(json, "target")
        return GoalTarget(
            obj.requiredEnum<GoalPeriod>("period"),
            obj.optionalNumber("amount"),
            obj.requiredEnum<GoalUnit>("unit"),
            obj.optionalEnum<GoalDirection>("direction") ?: GoalDirection.AtLeast,
            obj.optionalNumber("targetAmount"),
            obj.optionalDate("targetDate"),
            obj.optionalInt("weeklyDays"),
            obj.optionalInt("missesAllowed"),
            obj.value("pendingChange")?.let(::readPendingChange),
        )
    }

    fun readPendingChange(json: JsonElement): GoalPendingChange {
        val obj = obj(json, "pendingChange")
        return GoalPendingChange(obj.optionalNumber("amount"), obj.requiredTime("appliesAt"), obj.optionalBool("madeInProductaBuddy") ?: false)
    }

    fun readSourceRule(json: JsonElement): GoalSourceRule {
        val obj = obj(json, "source")
        return GoalSourceRule(
            obj.requiredGuid("id"),
            obj.requiredEnum<GoalSourceType>("type"),
            obj.optionalGuid("labelId"),
            obj.optionalText("tag"),
            obj.optionalGuid("calendarCategoryId"),
            obj.optionalText("app"),
            obj.optionalEnum<GoalDirection>("screenDirection"),
            obj.optionalText("beeminderSlug"),
            obj.optionalEnum<GoalBeeminderDirection>("beeminderDirection"),
            obj.optionalNumber("thresholdMinutes"),
            obj.optionalBool("automatic") ?: true,
        )
    }

    fun readPause(json: JsonElement): GoalPauseStretch {
        val obj = obj(json, "pause")
        return GoalPauseStretch(obj.requiredDate("from"), obj.optionalDate("resumedOn"))
    }

    fun readExtraTiles(json: JsonElement): GoalExtraTiles {
        val obj = obj(json, "extraTiles")
        return GoalExtraTiles(obj.optionalBool("streak") ?: false, obj.optionalBool("total") ?: false)
    }

    fun readEntry(json: JsonElement): GoalEntry {
        val obj = obj(json, "entry")
        return GoalEntry(
            obj.requiredGuid("id"),
            obj.requiredGuid("goalId"),
            obj.requiredDate("day"),
            obj.optionalTimeOfDay("time"),
            obj.optionalNumber("value") ?: 0.0,
            obj.requiredEnum<GoalEntrySource>("source"),
            obj.requiredTime("editedAt"),
            obj.optionalNumber("position") ?: 0.0,
            obj.optionalTime("deletedAt"),
        )
    }

    fun readReminder(json: JsonElement): GoalReminder {
        val obj = obj(json, "reminder")
        return GoalReminder(
            obj.requiredGuid("id"),
            obj.requiredGuid("goalId"),
            obj.requiredEnum<GoalReminderDays>("days"),
            obj.optionalTimeOfDay("time") ?: throw missing("time"),
            obj.optionalNumber("position") ?: 0.0,
            obj.requiredTime("editedAt"),
            obj.optionalArray("weekdays")?.map { enumOf<GoalWeekday>(it, "weekdays") },
            obj.optionalDate("date"),
            obj.optionalBool("repeat") ?: true,
            obj.optionalText("reason"),
            obj.optionalBool("skipWhenDone") ?: true,
            obj.optionalBool("nudgeWhenBehind") ?: false,
            obj.optionalTime("deletedAt"),
        )
    }

    fun readReview(json: JsonElement): GoalReviewRecord {
        val obj = obj(json, "review")
        return GoalReviewRecord(
            obj.requiredGuid("id"),
            obj.requiredDate("week"),
            obj.optionalArray("decisions")?.map(::readReviewDecision) ?: emptyList(),
            obj.optionalNumber("position") ?: 0.0,
            obj.requiredTime("editedAt"),
            obj.optionalTime("snoozedUntil"),
            obj.optionalText("notes"),
            obj.optionalTime("deletedAt"),
        )
    }

    fun readReviewDecision(json: JsonElement): GoalReviewDecision {
        val obj = obj(json, "decision")
        return GoalReviewDecision(obj.requiredGuid("goalId"), obj.requiredEnum<GoalReviewChoice>("choice"))
    }

    /**
     * One `sessions` item of a sync file as goals read it (the PC's `SyncSession` with its tasks' label fields): a missing or null
     * `tasks` stays null (no checklist), an empty one stays empty, as the piece-joining rule tells them apart.
     */
    fun readPiece(json: JsonElement): GoalSyncedPiece {
        val obj = obj(json, "session")
        return GoalSyncedPiece(
            obj.requiredTime("start").toInstant(),
            obj.optionalTime("end")?.toInstant(),
            obj.optionalText("profile"),
            obj.optionalBool("added") ?: false,
            obj.optionalArray("tasks")?.map(::readSessionTask),
            obj.optionalInt("offMinutesBefore"),
        )
    }

    /** A checklist task's label fields (its category id and name, its #tags); everything else in it is left unread. */
    fun readSessionTask(json: JsonElement): GoalSessionTask {
        val obj = obj(json, "task")
        return GoalSessionTask(
            obj.optionalGuid("categoryId"),
            obj.optionalText("category"),
            obj.optionalArray("labels")?.map { text(it, "labels") },
        )
    }

    /** Reads a time the way [time] writes it (any ISO offset, "Z" too). */
    fun parseTime(text: String): OffsetDateTime = try {
        OffsetDateTime.parse(text)
    } catch (e: DateTimeParseException) {
        throw IllegalArgumentException("Not a time: $text", e)
    }

    fun parseInstant(text: String): Instant = parseTime(text).toInstant()

    // ---------------------------------------------------------------- helpers

    private fun missing(name: String) = IllegalArgumentException("Goal JSON: \"$name\" is missing or of the wrong kind")

    private fun obj(json: JsonElement, name: String): JsonObject = json as? JsonObject ?: throw missing(name)

    private fun list(json: JsonElement, name: String): JsonArray = json as? JsonArray ?: throw missing(name)

    // The PC reads property names in any case (JsonSerializerDefaults.Web); an exact match wins.
    private fun JsonObject.field(name: String): JsonElement? = this[name] ?: entries.firstOrNull { it.key.equals(name, ignoreCase = true) }?.value

    private fun JsonObject.value(name: String): JsonElement? = field(name)?.takeIf { it !is JsonNull }

    private fun JsonObject.required(name: String): JsonElement = value(name) ?: throw missing(name)

    private fun text(json: JsonElement, name: String): String = (json as? JsonPrimitive)?.takeIf { it.isString }?.content ?: throw missing(name)

    private fun JsonObject.optionalText(name: String): String? = value(name)?.let { text(it, name) }

    private fun JsonObject.optionalArray(name: String): JsonArray? = value(name)?.let { it as? JsonArray ?: throw missing(name) }

    private fun JsonObject.optionalBool(name: String): Boolean? = value(name)?.let { element ->
        val primitive = element as? JsonPrimitive
        if (primitive == null || primitive.isString) throw missing(name)
        primitive.content.toBooleanStrictOrNull() ?: throw missing(name)
    }

    // A number, or a number in a string (JsonNumberHandling.AllowReadingFromString).
    private fun JsonObject.optionalNumber(name: String): Double? = value(name)?.let { element ->
        val primitive = element as? JsonPrimitive ?: throw missing(name)
        (if (primitive.isString) primitive.content.trim().toDoubleOrNull() else primitive.doubleOrNull) ?: throw missing(name)
    }

    private fun JsonObject.optionalInt(name: String): Int? = value(name)?.let { element ->
        val primitive = element as? JsonPrimitive ?: throw missing(name)
        (if (primitive.isString) primitive.content.trim() else primitive.content).toIntOrNull() ?: throw missing(name)
    }

    private fun guid(text: String, name: String): UUID = if (guidForm.matches(text)) UUID.fromString(text) else throw missing(name)

    private fun JsonObject.requiredGuid(name: String): UUID = guid(text(required(name), name), name)

    private fun JsonObject.optionalGuid(name: String): UUID? = optionalText(name)?.let { guid(it, name) }

    private fun JsonObject.requiredTime(name: String): OffsetDateTime = parseTime(text(required(name), name))

    private fun JsonObject.optionalTime(name: String): OffsetDateTime? = optionalText(name)?.let(::parseTime)

    private fun JsonObject.requiredDate(name: String): LocalDate = optionalDate(name) ?: throw missing(name)

    private fun JsonObject.optionalDate(name: String): LocalDate? = optionalText(name)?.let {
        try {
            LocalDate.parse(it)
        } catch (e: DateTimeParseException) {
            throw IllegalArgumentException("Not a date: $it", e)
        }
    }

    private fun JsonObject.optionalTimeOfDay(name: String): LocalTime? = optionalText(name)?.let {
        try {
            LocalTime.parse(it)
        } catch (e: DateTimeParseException) {
            throw IllegalArgumentException("Not a time of day: $it", e)
        }
    }

    private inline fun <reified T : Enum<T>> JsonObject.requiredEnum(name: String): T = enumOf(required(name), name)

    private inline fun <reified T : Enum<T>> JsonObject.optionalEnum(name: String): T? = value(name)?.let { enumOf<T>(it, name) }

    // By name (exact, else any case) or by its number, as the PC's JsonStringEnumConverter reads it. A number with no named
    // value is refused here (the PC would keep it as an unnamed value).
    private inline fun <reified T : Enum<T>> enumOf(json: JsonElement, name: String): T {
        val values = enumValues<T>()
        val primitive = json as? JsonPrimitive ?: throw missing(name)
        val content = primitive.content.trim()
        if (primitive.isString) {
            values.firstOrNull { it.name == content }?.let { return it }
            values.firstOrNull { it.name.equals(content, ignoreCase = true) }?.let { return it }
        }

        return content.toIntOrNull()?.let { values.getOrNull(it) } ?: throw missing(name)
    }
}
