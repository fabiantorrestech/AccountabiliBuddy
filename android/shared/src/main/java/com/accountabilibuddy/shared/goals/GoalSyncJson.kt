package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.DotNetTime
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

/**
 * The goal parts of a sync file read and written as the PC does (`SyncGoalListConverter`, `SyncGoalEntryListConverter`): an item the app
 * cannot read is left out, one holding a value it does not know is kept as written at its place ([GoalNewerItem]), a part that is not a list
 * reads as missing. A field that is absent reads as the PC's default (a missing area id is the empty id, a missing kind is a habit, a missing
 * time is 0001-01-01): the C# record binding does the same, and [GoalSyncClean] decides what such an item is worth. A live area without a
 * name, a live goal without a name, target or sources are damaged and left out (the PC reads them as null and its clean-up drops them).
 */
object GoalSyncJson {
    private const val ZERO_ID = "00000000-0000-0000-0000-000000000000"
    private val MIN_TIME = GoalJson.timeValue(DotNetTime.MIN)
    private val MIN_DAY = JsonPrimitive("0001-01-01")
    private val PLACEHOLDER_TARGET = JsonObject(
        mapOf("period" to JsonPrimitive("Day"), "unit" to JsonPrimitive("Count"), "direction" to JsonPrimitive("AtLeast")),
    )

    /** A known list with the newer items kept at their places. */
    data class Part<T>(val known: List<T>, val newer: List<GoalNewerItem>)

    fun readAreas(json: JsonElement?): Part<GoalArea>? = readList(json) { item ->
        val obj = item as? JsonObject ?: throw IllegalArgumentException("area")
        if (isLive(obj) && missing(obj, "name")) throw IllegalArgumentException("area without a name")
        GoalJson.readArea(withDefaults(obj, "editedAt" to MIN_TIME))
    }

    fun readGoals(json: JsonElement?): Part<Goal>? = readList(json) { item ->
        val obj = item as? JsonObject ?: throw IllegalArgumentException("goal")
        val live = isLive(obj)
        if (live && (missing(obj, "name") || missing(obj, "target") || missing(obj, "sources"))) throw IllegalArgumentException("incomplete goal")
        val defaults = mutableListOf<Pair<String, JsonElement>>(
            "areaId" to JsonPrimitive(ZERO_ID), "kind" to JsonPrimitive("Habit"), "createdAt" to MIN_TIME, "editedAt" to MIN_TIME,
        )
        if (!live) defaults += "target" to PLACEHOLDER_TARGET
        // A tombstone written with "target": null keeps its place with the placeholder target, as the PC does (a default only fills an absent field).
        val source = if (!live && field(obj, "target") is JsonNull) JsonObject(obj.filterKeys { !it.equals("target", ignoreCase = true) }) else obj
        GoalJson.readGoal(withDefaults(source, *defaults.toTypedArray()))
    }

    fun readEntries(json: JsonElement?): Part<GoalEntry>? = readList(json) { item ->
        val obj = item as? JsonObject ?: throw IllegalArgumentException("entry")
        if (GoalEntryRows.isRow(obj)) {
            GoalEntryRows.read(obj)
        } else {
            GoalJson.readEntry(withDefaults(obj, "goalId" to JsonPrimitive(ZERO_ID), "day" to MIN_DAY, "source" to JsonPrimitive("Hand"), "editedAt" to MIN_TIME))
        }
    }

    fun readReminders(json: JsonElement?): Part<GoalReminder>? = readList(json) { item ->
        val obj = item as? JsonObject ?: throw IllegalArgumentException("reminder")
        GoalJson.readReminder(
            withDefaults(
                obj, "goalId" to JsonPrimitive(ZERO_ID), "days" to JsonPrimitive(GoalReminderDays.values().first().name),
                "time" to JsonPrimitive("00:00:00"), "editedAt" to MIN_TIME,
            ),
        )
    }

    fun readReviews(json: JsonElement?): Part<GoalReviewRecord>? = readList(json) { item ->
        val obj = item as? JsonObject ?: throw IllegalArgumentException("review")
        var fixed = withDefaults(obj, "week" to MIN_DAY, "editedAt" to MIN_TIME)
        val decisions = fixed["decisions"] as? JsonArray
        if (decisions != null) {
            fixed = JsonObject(fixed + ("decisions" to JsonArray(decisions.map { decision ->
                if (decision is JsonObject) {
                    withDefaults(decision, "goalId" to JsonPrimitive(ZERO_ID), "choice" to JsonPrimitive(GoalReviewChoice.values().first().name))
                } else {
                    decision
                }
            })))
        }

        GoalJson.readReview(fixed)
    }

    fun writeAreas(part: Part<GoalArea>): JsonArray = GoalJson.writeItems(part.known, part.newer, GoalJson::writeArea)

    fun writeGoals(part: Part<Goal>): JsonArray = GoalJson.writeItems(part.known, part.newer, GoalJson::writeGoal)

    fun writeEntries(part: Part<GoalEntry>): JsonArray = GoalJson.writeItems(part.known, part.newer, GoalEntryRows::write)

    fun writeReminders(part: Part<GoalReminder>): JsonArray = GoalJson.writeItems(part.known, part.newer, GoalJson::writeReminder)

    fun writeReviews(part: Part<GoalReviewRecord>): JsonArray = GoalJson.writeItems(part.known, part.newer, GoalJson::writeReview)

    /** The entries as the list writes them (rows where they fit): what "the content changed" compares and a history file holds. */
    fun entriesJson(lists: GoalLists): JsonArray = writeEntries(Part(lists.entries, lists.newer.entries))

    /** Reads a list of entries (rows or objects), cleaned as a file opens; null when it is not a list. */
    fun readEntriesCleaned(json: JsonElement?): GoalLists? = readEntries(json)?.let {
        GoalSyncClean.lists(GoalLists(entries = it.known, newer = GoalNewerLists(entries = it.newer)))
    }

    // Reads an array: non-objects skipped, readable items known, newer-value items kept at their running place, damaged ones left out.
    private fun <T> readList(json: JsonElement?, read: (JsonElement) -> T): Part<T>? {
        val array = json as? JsonArray ?: return null
        val known = ArrayList<T>()
        val newer = ArrayList<GoalNewerItem>()
        var index = 0
        for (item in array) {
            if (item !is JsonObject) continue
            try {
                known += read(item)
                index++
            } catch (e: GoalNewerValueException) {
                newer += GoalNewerItem(index, item)
                index++
            } catch (e: IllegalArgumentException) {
                // An item this app cannot read is left out.
            } catch (e: java.time.DateTimeException) {
                // Likewise.
            }
        }

        return Part(known, newer)
    }

    private fun isLive(obj: JsonObject): Boolean = missing(obj, "deletedAt")

    private fun field(obj: JsonObject, name: String): JsonElement? = obj[name] ?: obj.entries.firstOrNull { it.key.equals(name, ignoreCase = true) }?.value

    private fun missing(obj: JsonObject, name: String): Boolean = field(obj, name).let { it == null || it is JsonNull }

    // The object with each default added when the field is absent (any case); a null value stays null (the PC rejects it as damaged).
    private fun withDefaults(obj: JsonObject, vararg defaults: Pair<String, JsonElement>): JsonObject {
        var result = obj
        for ((name, value) in defaults) {
            if (field(result, name) == null) result = JsonObject(result + (name to value))
        }

        return result
    }
}
