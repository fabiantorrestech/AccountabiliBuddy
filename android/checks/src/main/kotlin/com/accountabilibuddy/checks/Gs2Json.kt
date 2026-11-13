package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalHistoryJson
import com.accountabilibuddy.shared.goals.GoalHistoryListing
import com.accountabilibuddy.shared.goals.GoalHistoryFileKey
import com.accountabilibuddy.shared.goals.GoalHistoryFileKind
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalSyncClean
import com.accountabilibuddy.shared.goals.GoalSyncJson
import com.accountabilibuddy.shared.sync.SyncGoalParts
import com.accountabilibuddy.shared.sync.SyncGoalYearMerged
import java.time.OffsetDateTime
import java.util.UUID
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put

/** Reading the GS1a / GS1b fixtures' inputs (the PC's JSON, as written) and writing the phone's results in the same shape. */
object Gs2Json {
    fun time(json: JsonElement): OffsetDateTime = GoalJson.parseTime(json.jsonPrimitive.content)

    fun id(json: JsonElement): UUID = UUID.fromString(json.jsonPrimitive.content)

    /** The five goal parts of a sync file as lists (not cleaned): what the PC's `Gs1aParts.Read` gives. */
    fun lists(json: JsonElement): GoalLists {
        val parts = SyncGoalParts.read(json.jsonObject)
        return if (parts == null) GoalLists.EMPTY else GoalLists(
            parts.areas.orEmpty(), parts.goals.orEmpty(), parts.entries.orEmpty(), parts.reminders.orEmpty(), parts.reviews.orEmpty(), parts.newer,
        )
    }

    /** The lists as the PC's `Gs1aParts.Node` writes them: all five parts. */
    fun parts(lists: GoalLists): JsonObject = JsonObject(
        SyncGoalParts.write(SyncGoalParts(lists.goals, lists.areas, lists.entries, lists.reminders, lists.reviews, lists.newer)).toMap(),
    )

    /** Rows or objects text -> entries as a file opens (read, then cleaned). */
    fun rows(text: String): GoalLists? = GoalSyncJson.readEntriesCleaned(Json.parseToJsonElement(text))

    /** Entries as the rows text the PC's `GoalHistoryRules.RowsText` gives. */
    fun rowsText(lists: GoalLists): String = GoalJson.text(GoalSyncJson.entriesJson(lists))

    /** Entries in object form with the newer items (the PC's `ObjectForm`). */
    fun objectForm(lists: GoalLists): JsonArray = GoalJson.writeEntries(lists.entries, lists.newer.entries)

    fun entriesList(json: JsonElement): GoalLists {
        val (known, newer) = GoalJson.readItems(json.jsonArray, GoalJson::readEntry)
        return GoalLists(entries = known, newer = com.accountabilibuddy.shared.goals.GoalNewerLists(entries = newer))
    }

    fun listing(json: JsonElement): GoalHistoryListing {
        val obj = json.jsonObject
        return GoalHistoryListing(key(obj.getValue("key")), time(obj.getValue("modifiedAt")))
    }

    fun key(json: JsonElement): GoalHistoryFileKey {
        val obj = json.jsonObject
        val month = obj["month"]?.takeIf { it !is JsonNull }?.jsonPrimitive?.int
        return GoalHistoryFileKey(
            GoalHistoryFileKind.valueOf(obj.getValue("kind").jsonPrimitive.content),
            obj["deviceId"]?.takeIf { it !is JsonNull }?.let(::id), obj.getValue("year").jsonPrimitive.int, month,
        )
    }

    fun keyJson(key: GoalHistoryFileKey): JsonObject = buildJsonObject {
        put("kind", key.kind.name)
        put("deviceId", key.deviceId?.toString())
        put("year", key.year)
        put("month", key.month)
    }

    fun yearsMerged(json: JsonElement?): List<SyncGoalYearMerged>? =
        json?.takeIf { it !is JsonNull }?.jsonArray?.map {
            val obj = it.jsonObject
            SyncGoalYearMerged(obj.getValue("year").jsonPrimitive.int, id(obj.getValue("rolledBy")), time(obj.getValue("rolledAt")))
        }

    fun yearsMergedJson(items: List<SyncGoalYearMerged>?): JsonElement = items?.let { list ->
        JsonArray(list.map {
            buildJsonObject {
                put("year", it.year)
                put("rolledBy", it.rolledBy.toString())
                put("rolledAt", GoalJson.time(it.rolledAt))
            }
        })
    } ?: JsonNull

    fun month(json: JsonElement) = GoalHistoryJson.readMonth(json) ?: error("month file")

    fun year(json: JsonElement) = GoalHistoryJson.readYear(json) ?: error("year file")

    /** Null when the C# JSON and the phone's agree in structure, order and text; else where they differ. */
    fun same(expected: JsonElement, actual: JsonElement): String? =
        JsonParity.difference(expected, actual) ?: GoalCaseRunner.sameText(GoalJson.text(expected), GoalJson.text(actual))

    fun cleaned(lists: GoalLists): GoalLists = GoalSyncClean.lists(lists)

    fun bool(json: JsonElement): Boolean = json.jsonPrimitive.boolean

    fun entryText(entry: GoalEntry): String = GoalJson.text(GoalJson.writeEntry(entry))

    fun nullable(json: JsonElement?): JsonElement? = json?.takeIf { it !is JsonNull }

    fun primitive(json: JsonElement?): JsonPrimitive? = json as? JsonPrimitive
}
