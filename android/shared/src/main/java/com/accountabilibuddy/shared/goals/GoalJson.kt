package com.accountabilibuddy.shared.goals

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonObjectBuilder
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
 *
 * ADR 79 forward compatibility, as the PC (`GoalNewerJson`, `GoalUnknownFields`): a field this version doesn't know is kept in the
 * object's `extra` and written back unchanged after its known fields; an enum value it doesn't know throws [GoalNewerValueException],
 * and a list (the stored lists, a goal's sources, a review's decisions) keeps that item as written ([GoalNewerItem]) at its place
 * instead of refusing the whole list. A name the PC ignores (its computed `isLive`, `isDeepWork`, `countsDays`, `countsTime`) is
 * dropped there, so it is dropped here too.
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
        putExtra(area.extra)
    }

    fun writeGoal(goal: Goal): JsonObject = buildJsonObject {
        put("id", goal.id.toString())
        put("areaId", goal.areaId.toString())
        put("name", goal.name)
        put("icon", goal.icon)
        put("color", goal.color)
        put("kind", goal.kind.name)
        put("target", writeTarget(goal.target))
        put("sources", writeItems(goal.sources, goal.newerSources, ::writeSourceRule))
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
        goal.stamps?.let { put("stamps", GoalStampJson.write(it)) }
        if (goal.trackTimeOnly) put("trackTimeOnly", true)
        putExtra(goal.extra)
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
        putExtra(target.extra)
    }

    fun writePendingChange(change: GoalPendingChange): JsonObject = buildJsonObject {
        change.amount?.let { put("amount", number(it)) }
        put("appliesAt", time(change.appliesAt))
        if (change.madeInProductaBuddy) {
            put("madeInProductaBuddy", true)
        }
        putExtra(change.extra)
    }

    fun writeSourceRule(rule: GoalSourceRule): JsonObject = buildJsonObject {
        put("id", rule.id.toString())
        put("type", rule.type.name)
        rule.labelId?.let { put("labelId", it.toString()) }
        rule.tag?.let { put("tag", it) }
        rule.calendarCategoryId?.let { put("calendarCategoryId", it.toString()) }
        rule.screenDirection?.let { put("screenDirection", it.name) }
        rule.beeminderSlug?.let { put("beeminderSlug", it) }
        rule.beeminderDirection?.let { put("beeminderDirection", it.name) }
        rule.thresholdMinutes?.let { put("thresholdMinutes", number(it)) }
        put("automatic", rule.automatic)
        rule.apps?.let { apps ->
            put("apps", buildJsonArray {
                apps.forEach { match ->
                    add(buildJsonObject {
                        put("kind", match.kind.name)
                        put("text", match.text)
                        putExtra(match.extra)
                    })
                }
            })
        }
        rule.app?.let { put("app", it) }
        rule.devices?.let { devices -> put("devices", buildJsonArray { devices.forEach { add(JsonPrimitive(it.toString())) } }) }
        if (rule.allDevices) {
            put("allDevices", true)
        }
        rule.createdOn?.let { put("createdOn", it.toString()) }
        putExtra(rule.extra)
    }

    fun writePause(pause: GoalPauseStretch): JsonObject = buildJsonObject {
        put("from", pause.from.toString())
        pause.resumedOn?.let { put("resumedOn", it.toString()) }
        putExtra(pause.extra)
    }

    fun writeExtraTiles(tiles: GoalExtraTiles): JsonObject = buildJsonObject {
        put("streak", tiles.streak)
        put("total", tiles.total)
        putExtra(tiles.extra)
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
        entry.deviceId?.let { put("deviceId", it.toString()) }
        putExtra(entry.extra)
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
        putExtra(reminder.extra)
    }

    fun writeReview(review: GoalReviewRecord): JsonObject = buildJsonObject {
        put("id", review.id.toString())
        put("week", review.week.toString())
        put("decisions", writeItems(review.decisions, review.newerDecisions, ::writeReviewDecision))
        put("position", number(review.position))
        put("editedAt", time(review.editedAt))
        review.snoozedUntil?.let { put("snoozedUntil", time(it)) }
        review.notes?.let { put("notes", it) }
        review.deletedAt?.let { put("deletedAt", time(it)) }
        putExtra(review.extra)
    }

    fun writeReviewDecision(decision: GoalReviewDecision): JsonObject = buildJsonObject {
        put("goalId", decision.goalId.toString())
        put("choice", decision.choice.name)
        decision.decidedAt?.let { put("decidedAt", time(it)) }
        putExtra(decision.extra)
    }

    /** A time as .NET writes a DateTimeOffset ("2026-11-11T18:00:00-05:00"). */
    fun time(time: OffsetDateTime): String = dotNetTime.format(time)

    /** A time of day as .NET writes a TimeOnly ("08:00:00", "08:15:30.5000000"). */
    fun timeOfDay(time: LocalTime): String = (if (time.nano == 0) dotNetTimeOfDay else dotNetTimeOfDayFraction).format(time)

    /** A number as .NET writes a double: whole numbers below 1E+17 without a fraction ("120", not "120.0"); see [doubleText] for the rest. */
    @OptIn(kotlinx.serialization.ExperimentalSerializationApi::class)
    fun number(value: Double): JsonPrimitive = when {
        // A negative zero is a value of its own: .NET writes it "-0".
        value == 0.0 && 1.0 / value < 0 -> kotlinx.serialization.json.JsonUnquotedLiteral("-0")
        !value.isNaN() && !value.isInfinite() && value == floor(value) && abs(value) < 1e17 -> JsonPrimitive(value.toLong())
        else -> JsonPrimitive(value)
    }

    // ---------------------------------------------------------------- the stored lists (goals.items, areas, entries, reminders, reviews)

    fun writeGoals(goals: List<Goal>, newer: List<GoalNewerItem> = emptyList()): JsonArray = writeItems(goals, newer, ::writeGoal)

    fun writeAreas(areas: List<GoalArea>, newer: List<GoalNewerItem> = emptyList()): JsonArray = writeItems(areas, newer, ::writeArea)

    fun writeEntries(entries: List<GoalEntry>, newer: List<GoalNewerItem> = emptyList()): JsonArray = writeItems(entries, newer, ::writeEntry)

    fun writeReminders(reminders: List<GoalReminder>, newer: List<GoalNewerItem> = emptyList()): JsonArray =
        writeItems(reminders, newer, ::writeReminder)

    fun writeReviews(reviews: List<GoalReviewRecord>, newer: List<GoalNewerItem> = emptyList()): JsonArray = writeItems(reviews, newer, ::writeReview)

    /** The items this version knows; one holding a value it doesn't know is left out (see [readStoredLists] to keep them). */
    fun readGoals(json: JsonElement): List<Goal> = readItems(list(json, "goals"), ::readGoal).first

    fun readAreas(json: JsonElement): List<GoalArea> = readItems(list(json, "areas"), ::readArea).first

    fun readEntries(json: JsonElement): List<GoalEntry> = readItems(list(json, "entries"), ::readEntry).first

    fun readReminders(json: JsonElement): List<GoalReminder> = readItems(list(json, "reminders"), ::readReminder).first

    fun readReviews(json: JsonElement): List<GoalReviewRecord> = readItems(list(json, "reviews"), ::readReview).first

    /**
     * A device's five stored lists (the PC's `GoalStore` read): the items this version knows, and per list the newer ones kept as
     * written ([GoalLists.newer]). Throws [IllegalArgumentException] only for JSON that is damaged, never for a newer item.
     */
    fun readStoredLists(areas: JsonElement, goals: JsonElement, entries: JsonElement, reminders: JsonElement, reviews: JsonElement): GoalLists {
        val (knownAreas, newerAreas) = readItems(list(areas, "areas"), ::readArea)
        val (knownGoals, newerGoals) = readItems(list(goals, "goals"), ::readGoal)
        val (knownEntries, newerEntries) = readItems(list(entries, "entries"), ::readEntry)
        val (knownReminders, newerReminders) = readItems(list(reminders, "reminders"), ::readReminder)
        val (knownReviews, newerReviews) = readItems(list(reviews, "reviews"), ::readReview)
        return GoalLists(knownAreas, knownGoals, knownEntries, knownReminders, knownReviews, GoalNewerLists(newerAreas, newerGoals, newerEntries, newerReminders, newerReviews))
    }

    /**
     * Reads a JSON array of goal items (the PC's `GoalNewerJson.ReadList`): the ones this version knows, and the ones holding a value it
     * doesn't know ([GoalNewerValueException]) kept as written with their places. A null item is skipped; any other damage throws.
     */
    fun <T> readItems(array: JsonArray, read: (JsonElement) -> T): Pair<List<T>, List<GoalNewerItem>> {
        val known = ArrayList<T>(array.size)
        val newer = ArrayList<GoalNewerItem>()
        array.forEachIndexed { index, item ->
            if (item is JsonNull) return@forEachIndexed
            try {
                known += read(item)
            } catch (e: GoalNewerValueException) {
                if (item !is JsonObject) throw e
                newer += GoalNewerItem(index, item)
            }
        }
        return known to newer
    }

    /**
     * Writes [known] with [newer] back at their places (the PC's `GoalNewerJson.WriteList`: a place past the end goes last), so a list
     * read and written unchanged comes out in its order.
     */
    fun <T> writeItems(known: List<T>, newer: List<GoalNewerItem>, write: (T) -> JsonObject): JsonArray {
        val waiting = newer.sortedBy { it.index }
        val out = ArrayList<JsonElement>(known.size + newer.size)
        var next = 0
        for (item in known) {
            while (next < waiting.size && waiting[next].index <= out.size) {
                out += waiting[next++].json
            }
            out += write(item)
        }
        while (next < waiting.size) {
            out += waiting[next++].json
        }
        return JsonArray(out)
    }

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

    // true / false / a whole number as they are; a double this app made (Kotlin's own text of it) as .NET writes it. Anything else, an
    // unknown field a newer app wrote ("1.0", "1.50", a long decimal, one too big for a double), goes back exactly as it came
    // (R7.1 review).
    private fun literalText(content: String): String {
        if (content == "true" || content == "false" || content.none { it == '.' || it == 'e' || it == 'E' }) {
            return content
        }

        val value = content.toDoubleOrNull()?.takeIf { !it.isInfinite() && !it.isNaN() } ?: return content
        val madeHere = content == value.toString() && (value != floor(value) || abs(value) >= 1e17)
        return if (madeHere) doubleText(value) else content
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
            obj.unknown(AREA_FIELDS, LIVE_ONLY),
        )
    }

    fun readGoal(json: JsonElement): Goal {
        val obj = obj(json, "goal")
        val (sources, newerSources) = obj.optionalArray("sources")?.let { readItems(it, ::readSourceRule) } ?: (emptyList<GoalSourceRule>() to emptyList())
        return Goal(
            obj.requiredGuid("id"),
            obj.requiredGuid("areaId"),
            obj.optionalText("name") ?: "",
            obj.optionalText("icon") ?: "",
            obj.optionalText("color") ?: "",
            obj.requiredEnum<GoalKind>("kind"),
            readTarget(obj.required("target")),
            sources,
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
            newerSources,
            obj.unknown(GOAL_FIELDS, GOAL_IGNORED),
            obj.value("stamps")?.let(GoalStampJson::read),
            obj.optionalBool("trackTimeOnly") ?: false,
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
            obj.unknown(TARGET_FIELDS, TARGET_IGNORED),
        )
    }

    fun readPendingChange(json: JsonElement): GoalPendingChange {
        val obj = obj(json, "pendingChange")
        return GoalPendingChange(
            obj.optionalNumber("amount"),
            obj.requiredTime("appliesAt"),
            obj.optionalBool("madeInProductaBuddy") ?: false,
            obj.unknown(PENDING_FIELDS),
        )
    }

    fun readSourceRule(json: JsonElement): GoalSourceRule {
        val obj = obj(json, "source")
        val apps = obj.optionalArray("apps")?.map(::readAppMatch)
        return GoalSourceRule(
            obj.requiredGuid("id"),
            obj.requiredEnum<GoalSourceType>("type"),
            obj.optionalGuid("labelId"),
            obj.optionalText("tag"),
            obj.optionalGuid("calendarCategoryId"),
            obj.optionalEnum<GoalDirection>("screenDirection"),
            obj.optionalText("beeminderSlug"),
            obj.optionalEnum<GoalBeeminderDirection>("beeminderDirection"),
            obj.optionalNumber("thresholdMinutes"),
            obj.optionalBool("automatic") ?: true,
            apps ?: GoalAppMatchGuess.guess(obj.optionalText("app"))?.let { listOf(it) },
            obj.optionalArray("devices")?.map { guid(text(it, "devices"), "devices") },
            obj.optionalBool("allDevices") ?: false,
            obj.optionalGuid("createdOn"),
            obj.unknown(SOURCE_FIELDS, SOURCE_IGNORED),
        )
    }

    private fun readAppMatch(json: JsonElement): GoalAppMatch {
        val obj = obj(json, "apps")
        return GoalAppMatch(obj.requiredEnum<GoalAppMatchKind>("kind"), obj.required("text").let { text(it, "text") }, obj.unknown(APP_FIELDS))
    }

    fun readPause(json: JsonElement): GoalPauseStretch {
        val obj = obj(json, "pause")
        return GoalPauseStretch(obj.requiredDate("from"), obj.optionalDate("resumedOn"), obj.unknown(PAUSE_FIELDS))
    }

    fun readExtraTiles(json: JsonElement): GoalExtraTiles {
        val obj = obj(json, "extraTiles")
        return GoalExtraTiles(obj.optionalBool("streak") ?: false, obj.optionalBool("total") ?: false, obj.unknown(TILES_FIELDS))
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
            obj.unknown(ENTRY_FIELDS, LIVE_ONLY),
            obj.optionalGuid("deviceId"),
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
            obj.unknown(REMINDER_FIELDS, LIVE_ONLY),
        )
    }

    fun readReview(json: JsonElement): GoalReviewRecord {
        val obj = obj(json, "review")
        val (decisions, newerDecisions) = obj.optionalArray("decisions")?.let { readItems(it, ::readReviewDecision) }
            ?: (emptyList<GoalReviewDecision>() to emptyList())
        return GoalReviewRecord(
            obj.requiredGuid("id"),
            obj.requiredDate("week"),
            decisions,
            obj.optionalNumber("position") ?: 0.0,
            obj.requiredTime("editedAt"),
            obj.optionalTime("snoozedUntil"),
            obj.optionalText("notes"),
            obj.optionalTime("deletedAt"),
            newerDecisions,
            obj.unknown(REVIEW_FIELDS, LIVE_ONLY),
        )
    }

    fun readReviewDecision(json: JsonElement): GoalReviewDecision {
        val obj = obj(json, "decision")
        return GoalReviewDecision(obj.requiredGuid("goalId"), obj.requiredEnum<GoalReviewChoice>("choice"), obj.unknown(DECISION_FIELDS), obj.optionalTime("decidedAt"))
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

    // The JSON names each goal object knows (as the PC writes them), and the PC's computed names it reads and drops ([JsonIgnore]).
    private val AREA_FIELDS = setOf("id", "name", "icon", "color", "position", "editedAt", "deletedAt")
    private val GOAL_FIELDS = setOf(
        "id", "areaId", "name", "icon", "color", "kind", "target", "sources", "createdAt", "position", "editedAt", "builtIn", "secondTarget",
        "pauses", "showOnToday", "extraTiles", "notes", "deletedAt", "stamps", "trackTimeOnly",
    )
    private val TARGET_FIELDS = setOf("period", "amount", "unit", "direction", "targetAmount", "targetDate", "weeklyDays", "missesAllowed", "pendingChange")
    private val PENDING_FIELDS = setOf("amount", "appliesAt", "madeInProductaBuddy")
    private val SOURCE_FIELDS = setOf(
        "id", "type", "labelId", "tag", "calendarCategoryId", "screenDirection", "beeminderSlug", "beeminderDirection", "thresholdMinutes",
        "automatic", "apps", "app", "devices", "allDevices", "createdOn",
    )
    private val APP_FIELDS = setOf("kind", "text")
    private val PAUSE_FIELDS = setOf("from", "resumedOn")
    private val TILES_FIELDS = setOf("streak", "total")
    private val ENTRY_FIELDS = setOf("id", "goalId", "day", "time", "value", "source", "editedAt", "position", "deletedAt", "deviceId")
    private val REMINDER_FIELDS = setOf(
        "id", "goalId", "days", "time", "position", "editedAt", "weekdays", "date", "repeat", "reason", "skipWhenDone", "nudgeWhenBehind", "deletedAt",
    )
    private val REVIEW_FIELDS = setOf("id", "week", "decisions", "position", "editedAt", "snoozedUntil", "notes", "deletedAt")
    private val DECISION_FIELDS = setOf("goalId", "choice", "decidedAt")
    private val LIVE_ONLY = setOf("isLive")
    private val GOAL_IGNORED = setOf("isLive", "isDeepWork")
    private val TARGET_IGNORED = setOf("countsDays")
    private val SOURCE_IGNORED = setOf("countsTime")

    // The fields this version doesn't know, in their order (names compared in any case, as the PC binds them).
    private fun JsonObject.unknown(known: Set<String>, ignored: Set<String> = emptySet()): JsonObject =
        JsonObject(filterKeys { key -> known.none { it.equals(key, ignoreCase = true) } && ignored.none { it.equals(key, ignoreCase = true) } })

    // Unknown fields go back after the known ones, as System.Text.Json writes extension data.
    private fun JsonObjectBuilder.putExtra(extra: JsonObject) {
        extra.forEach { (key, value) -> put(key, value) }
    }

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

    // By name (exact, else any case) or by its number (a number or a number in text), as the PC's GoalEnumConverter reads it. A
    // name or number this version doesn't know is a newer app's value ([GoalNewerValueException]); null, true / false or an object
    // or array is damaged.
    private inline fun <reified T : Enum<T>> enumOf(json: JsonElement, name: String): T {
        val values = enumValues<T>()
        val primitive = json as? JsonPrimitive ?: throw missing(name)
        if (primitive is JsonNull || (!primitive.isString && primitive.content.toDoubleOrNull() == null)) throw missing(name)
        val content = primitive.content.trim()
        if (primitive.isString) {
            values.firstOrNull { it.name == content }?.let { return it }
            values.firstOrNull { it.name.equals(content, ignoreCase = true) }?.let { return it }
        }

        return content.toIntOrNull()?.let { values.getOrNull(it) }
            ?: throw GoalNewerValueException("Goal JSON: \"$name\" holds a value this version doesn't know (written by a newer version)")
    }
}
