package com.accountabilibuddy.shared.goals

import com.accountabilibuddy.shared.sync.SyncGoalYearMerged
import java.time.OffsetDateTime
import java.util.UUID
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.put

/** [GoalHistoryState] as the phone keeps it (one sealed document): ids, numbers and times only, plus the month files as last written. */
object GoalHistoryStateJson {
    fun write(state: GoalHistoryState): JsonObject = buildJsonObject {
        put("version", 1)
        put("group", state.group)
        put("goalsPartMerged", state.goalsPartMerged)
        put("lastRegularMerge", times(state.lastRegularMerge))
        put("lastMergeFrom", times(state.lastMergeFrom))
        put("deviceMarkers", buildJsonObject { state.deviceMarkers.forEach { (id, marker) -> put(id.toString(), marker(marker)) } })
        put("ownMarker", marker(state.ownMarker))
        put("tombstonedGoals", ids(state.tombstonedGoals))
        put("prunedGoals", ids(state.prunedGoals))
        state.firstDropAt?.let { put("firstDropAt", GoalJson.time(it)) }
        put("historyNames", strings(state.historyNames))
        put(
            "transports",
            buildJsonObject {
                state.transports.forEach { (name, t) ->
                    put(
                        name,
                        buildJsonObject {
                            put("read", textTimes(t.read))
                            put("readTag", buildJsonObject { t.readTag.forEach { (k, v) -> put(k, v) } })
                            put("written", buildJsonObject { t.written.forEach { (k, v) -> put(k, GoalHistoryJson.writeMonth(v)) } })
                            put("uploaded", textTimes(t.uploaded))
                            put("removed", strings(t.removed))
                            put("resendMonths", strings(t.resendMonths))
                            put("years", buildJsonObject { t.years.forEach { (k, v) -> put(k, GoalHistoryJson.writeYear(v.copy(entries = GoalLists.EMPTY))) } })
                            put("ownYearNames", strings(t.ownYearNames))
                            put("ownNames", strings(t.ownNames))
                            put("rollChecked", buildJsonObject { t.rollChecked.forEach { (k, v) -> put(k.toString(), GoalJson.time(v)) } })
                            put("newToGroup", t.newToGroup)
                        },
                    )
                }
            },
        )
    }

    /** The state, or null when [json] isn't one (the caller then starts empty: only a read position is lost, never a goal). */
    fun read(json: JsonElement): GoalHistoryState? = try {
        val obj = json as JsonObject
        GoalHistoryState().also { state ->
            state.group = obj.str("group")
            state.goalsPartMerged = (obj["goalsPartMerged"] as? JsonPrimitive)?.booleanOrNull ?: false
            readTimes(obj["lastRegularMerge"], state.lastRegularMerge)
            readTimes(obj["lastMergeFrom"], state.lastMergeFrom)
            (obj["deviceMarkers"] as? JsonObject)?.forEach { (id, value) -> state.deviceMarkers[UUID.fromString(id)] = readMarker(value) }
            state.ownMarker = readMarker(obj["ownMarker"])
            state.tombstonedGoals = readIds(obj["tombstonedGoals"]).toSet()
            state.prunedGoals.addAll(readIds(obj["prunedGoals"]))
            (obj["firstDropAt"] as? JsonPrimitive)?.contentOrNull?.let { state.firstDropAt = GoalJson.parseTime(it) }
            state.historyNames.addAll(readStrings(obj["historyNames"]))
            (obj["transports"] as? JsonObject)?.forEach { (name, value) ->
                val t = state.transport(name)
                val o = value as JsonObject
                (o["read"] as? JsonObject)?.forEach { (k, v) -> t.read[k] = GoalJson.parseTime(v.text()) }
                (o["readTag"] as? JsonObject)?.forEach { (k, v) -> t.readTag[k] = v.text() }
                (o["written"] as? JsonObject)?.forEach { (k, v) -> t.written[k] = GoalHistoryJson.readMonth(v) ?: throw IllegalArgumentException("month") }
                (o["uploaded"] as? JsonObject)?.forEach { (k, v) -> t.uploaded[k] = GoalJson.parseTime(v.text()) }
                t.removed.addAll(readStrings(o["removed"]))
                t.resendMonths.addAll(readStrings(o["resendMonths"]))
                (o["years"] as? JsonObject)?.forEach { (k, v) -> t.years[k] = GoalHistoryJson.readYear(v) ?: throw IllegalArgumentException("year") }
                t.ownYearNames.addAll(readStrings(o["ownYearNames"]))
                t.ownNames.addAll(readStrings(o["ownNames"]))
                (o["rollChecked"] as? JsonObject)?.forEach { (k, v) -> t.rollChecked[k.toInt()] = GoalJson.parseTime(v.text()) }
                // A state written before this field existed was new to the group only if it never merged a goals part (the reads all happened).
                t.newToGroup = (o["newToGroup"] as? JsonPrimitive)?.booleanOrNull ?: !state.goalsPartMerged
            }
        }
    } catch (e: Exception) {
        null
    }

    private fun times(map: Map<UUID, OffsetDateTime>) = buildJsonObject { map.forEach { (k, v) -> put(k.toString(), GoalJson.time(v)) } }

    private fun textTimes(map: Map<String, OffsetDateTime>) = buildJsonObject { map.forEach { (k, v) -> put(k, GoalJson.timeValue(v)) } }

    private fun ids(ids: Iterable<UUID>) = buildJsonArray { ids.forEach { add(JsonPrimitive(it.toString())) } }

    private fun strings(values: Iterable<String>) = buildJsonArray { values.forEach { add(JsonPrimitive(it)) } }

    private fun marker(marker: List<SyncGoalYearMerged>) = buildJsonArray {
        marker.forEach {
            add(buildJsonObject { put("year", it.year); put("rolledBy", it.rolledBy.toString()); put("rolledAt", GoalJson.timeValue(it.rolledAt)) })
        }
    }

    private fun readMarker(json: JsonElement?): List<SyncGoalYearMerged> = (json as? JsonArray).orEmpty().map {
        val o = it as JsonObject
        SyncGoalYearMerged(o["year"].text().toInt(), UUID.fromString(o.str("rolledBy")), GoalJson.parseTime(o.str("rolledAt")))
    }

    private fun readTimes(json: JsonElement?, into: MutableMap<UUID, OffsetDateTime>) {
        (json as? JsonObject)?.forEach { (k, v) -> into[UUID.fromString(k)] = GoalJson.parseTime(v.text()) }
    }

    private fun readIds(json: JsonElement?): List<UUID> = (json as? JsonArray).orEmpty().map { UUID.fromString(it.text()) }

    private fun readStrings(json: JsonElement?): List<String> = (json as? JsonArray).orEmpty().map { it.text() }

    private fun JsonObject.str(name: String): String = this[name].text()

    private fun JsonElement?.text(): String = (this as JsonPrimitive).contentOrNull ?: throw IllegalArgumentException("text")
}
