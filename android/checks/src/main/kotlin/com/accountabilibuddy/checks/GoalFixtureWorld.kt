package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalFocusAttribution
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalSourceData
import com.accountabilibuddy.shared.goals.GoalSyncedPiece
import com.accountabilibuddy.shared.goals.GoalWeekStart
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonPrimitive
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.time.ZoneOffset

/**
 * A goals fixture case's world as the Kotlin side reads it: the zone (a fixed offset, or a zone id), today, now, the week start,
 * the goal, its entries and each device's synced sessions, through the phone's own readers ([GoalJson]).
 */
class GoalFixtureWorld(private val input: JsonObject) {
    val zone: ZoneId = input["zoneId"]?.jsonPrimitive?.content?.let { ZoneId.of(it) }
        ?: ZoneOffset.ofTotalSeconds(input.getValue("zoneOffsetMinutes").jsonPrimitive.int * 60)
    val today: LocalDate = LocalDate.parse(input.getValue("today").jsonPrimitive.content)
    val now: Instant? = input["now"]?.jsonPrimitive?.content?.let(GoalJson::parseInstant)
    val weekStart: GoalWeekStart = input["weekStart"]?.jsonPrimitive?.content?.let { GoalWeekStart.valueOf(it) } ?: GoalWeekStart.Sunday
    val tagsAvailable: Boolean = input["tagsAvailable"]?.jsonPrimitive?.boolean ?: true
    val goal: Goal = GoalJson.readGoal(input.getValue("goal"))
    val entries: List<GoalEntry> = (input["entries"] as? JsonArray).orEmpty().map(GoalJson::readEntry)
    val devices: List<List<GoalSyncedPiece>> = (input["devices"] as? JsonArray).orEmpty().map { device -> device.jsonArray.map(GoalJson::readPiece) }

    /** The data as the PC's checks built it: each device's pieces joined on their own, then all sessions together. */
    fun data(): GoalSourceData {
        val focus = devices.flatMap { GoalFocusAttribution.fromSynced(it, now!!, zone) }
        return GoalSourceData(focus, entries, zone, null, tagsAvailable)
    }
}
