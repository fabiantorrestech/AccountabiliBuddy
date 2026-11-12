package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalJson
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

/**
 * The five stored goal lists (goals, areas, entries, reminders, reviews) with every optional field, tombstones, a source rule
 * with every field, waiting changes made in either mode, odd times and numbers and private text with every escaped character:
 * the PC's text read by the phone and written back must be the same bytes (so the PC reads back what it wrote; GoalsChecks
 * checks its own round trip of the same text).
 */
class GoalShapeCheck : FixtureCheck {
    override val area = "goals"
    override val name = "shape"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name} round-trip C# → Kotlin → C# byte-identical") {
                val text = case.text("json")
                val json = Json.parseToJsonElement(text)
                val written: JsonElement = when (case.name) {
                    "goals" -> GoalJson.writeGoals(GoalJson.readGoals(json))
                    "areas" -> GoalJson.writeAreas(GoalJson.readAreas(json))
                    "entries" -> GoalJson.writeEntries(GoalJson.readEntries(json))
                    "reminders" -> GoalJson.writeReminders(GoalJson.readReminders(json))
                    "reviews" -> GoalJson.writeReviews(GoalJson.readReviews(json))
                    else -> error("no list called ${case.name}")
                }
                JsonParity.difference(json, written)
                    ?: GoalCaseRunner.sameText(case.expected.jsonObject.getValue("json").jsonPrimitive.content, GoalJson.text(written))
            }
        }
    }
}
