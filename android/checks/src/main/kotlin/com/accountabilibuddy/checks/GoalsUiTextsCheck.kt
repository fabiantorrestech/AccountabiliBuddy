package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalTexts
import com.accountabilibuddy.shared.goals.GoalUnit
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.double
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.time.LocalDate

/**
 * Round 2 R6a: the goals' numbers and dates as the PC writes them (goals-ui/texts.json, `GoalTexts` in the PC's App): .NET's
 * rounding to even and "#,0" / "#,0.#" grouping, each unit's value and short value, whole minutes, "Wed 11/11" with the year only
 * when it differs.
 */
class GoalsUiTextsCheck : FixtureCheck {
    override val area = "goals-ui"
    override val name = "texts"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            val result = runCatching {
                val actual = if (case.input.containsKey("day")) {
                    buildJsonObject { put("date", GoalTexts.date(LocalDate.parse(case.text("day")), LocalDate.parse(case.text("today")))) }
                } else {
                    val value = case.input.getValue("value").jsonPrimitive.double
                    buildJsonObject {
                        put("number0", GoalTexts.number(value, 0))
                        put("number1", GoalTexts.number(value, 1))
                        put("minutes", GoalTexts.minutes(value))
                        put("units", buildJsonObject {
                            GoalUnit.entries.forEach { unit ->
                                put(unit.name, buildJsonObject { put("value", GoalTexts.value(value, unit)); put("short", GoalTexts.shortValue(value, unit)) })
                            }
                        })
                    }
                }
                JsonParity.difference(case.expected, actual)
            }
            val detail = result.getOrElse { "threw ${it::class.simpleName}: ${it.message}" }
            report.check("$area/$name: ${case.name}", result.isSuccess && detail == null, detail ?: "")
        }
    }
}
