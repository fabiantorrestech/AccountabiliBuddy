package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalJson
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.double
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonPrimitive

/** Doubles as the PC writes them (whole ones without a fraction, .NET's shortest text, "1E-05"): the phone writes the same bytes. */
class GoalNumbersCheck : FixtureCheck {
    override val area = "goals"
    override val name = "numbers"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                val text = case.text("json")
                val values = Json.parseToJsonElement(text).jsonArray.map { it.jsonPrimitive.double }
                GoalCaseRunner.sameText(text, GoalJson.text(JsonArray(values.map(GoalJson::number))))
            }
        }
    }
}
