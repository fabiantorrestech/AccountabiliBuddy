package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalLabelId
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.time.LocalDate
import java.util.UUID

/**
 * A goals snapshot fixture (R4.7): for each case, the goal's days (value, minutes, status, short, sources, heat…), This period,
 * the status, streak, 14-day strip, age, Target tile, What it takes and the review suggestion, all from the synced shape, against
 * what the C# Core computed. Also checks the goal and entry JSON comes back out of the phone's reader and writer as the PC wrote it.
 */
class GoalSnapshotCheck(override val name: String) : FixtureCheck {
    override val area = "goals"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        var shapeDifference: String? = null
        for (case in fixture.cases) {
            val result = runCatching {
                val world = GoalFixtureWorld(case.input)
                val expected = case.expected.jsonObject
                val from = LocalDate.parse(expected.getValue("days").jsonArray.first().jsonObject.getValue("day").jsonPrimitive.content)
                val actual = GoalSnapshotJson.snapshot(world.goal, world.data(), world.today, world.weekStart, from)
                if (shapeDifference == null) {
                    shapeDifference = roundTrip(case.input)?.let { "${case.name}: $it" }
                }
                JsonParity.difference(expected, actual)
            }
            val detail = result.getOrElse { "threw ${it::class.simpleName}: ${it.message}" }
            report.check("$area/$name: ${case.name}", result.isSuccess && detail == null, detail ?: "")
        }

        report.check("$area/$name: goal and entry JSON round-trips in the PC's shape", shapeDifference == null, shapeDifference ?: "")
        if (name == "attribution") {
            report.check("$area/$name: label ids from names (TaskLabel.IdFor)", labelIdsAgree(fixture) == null, labelIdsAgree(fixture) ?: "")
        }
    }

    private fun roundTrip(input: JsonObject): String? {
        val goal = input.getValue("goal")
        JsonParity.difference(goal, GoalJson.writeGoal(GoalJson.readGoal(goal)), "$.goal")?.let { return it }
        for ((index, entry) in (input["entries"] as? JsonArray).orEmpty().withIndex()) {
            JsonParity.difference(entry, GoalJson.writeEntry(GoalJson.readEntry(entry)), "$.entries[$index]")?.let { return it }
        }

        return null
    }

    // Every fixture task that names its label: the phone's id for the name is the id the PC sent.
    private fun labelIdsAgree(fixture: FixtureFile): String? {
        var seen = 0
        for (case in fixture.cases) {
            for (device in (case.input["devices"] as? JsonArray).orEmpty()) {
                for (session in device.jsonArray) {
                    for (task in (session.jsonObject["tasks"] as? JsonArray).orEmpty()) {
                        val category = task.jsonObject["category"]?.jsonPrimitive?.content ?: continue
                        val id = task.jsonObject["categoryId"]?.jsonPrimitive?.content ?: continue
                        seen++
                        if (GoalLabelId.idFor(category) != UUID.fromString(id)) return "\"$category\": kotlin ${GoalLabelId.idFor(category)} vs C# $id"
                    }
                }
            }
        }

        return if (seen == 0) "no named label in the fixture" else null
    }
}
