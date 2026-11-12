package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalAppMatching
import com.accountabilibuddy.shared.goals.GoalAppPlatform
import com.accountabilibuddy.shared.goals.GoalAppRow
import com.accountabilibuddy.shared.goals.GoalJson
import kotlinx.serialization.json.double
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

/** Screen-time source v2 (ST1): GoalAppMatching.minutesFor (a row counts once) against the PC's MinutesFor. */
class ScreenTimeMinutesCheck : FixtureCheck {
    override val area = "screentime"
    override val name = "minutes"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                val rule = GoalJson.readSourceRule(case.input.getValue("rule"))
                val rows = case.input.getValue("rows").jsonArray.map { item ->
                    val obj = item.jsonObject
                    GoalAppRow(
                        obj.getValue("id").jsonPrimitive.content,
                        ScreenTimeMatchingCheck.nullableText(obj, "label"),
                        ScreenTimeMatchingCheck.nullableText(obj, "site"),
                        obj.getValue("minutes").jsonPrimitive.double,
                    )
                }
                val actual = GoalAppMatching.minutesFor(rule, rows, GoalAppPlatform.valueOf(case.text("platform")))
                val expected = case.expected.jsonPrimitive.double
                if (actual == expected) null else "kotlin $actual vs C# $expected"
            }
        }
    }
}
