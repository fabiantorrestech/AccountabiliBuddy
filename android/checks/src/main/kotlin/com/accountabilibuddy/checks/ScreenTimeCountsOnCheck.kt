package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalAppMatching
import com.accountabilibuddy.shared.goals.GoalJson
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.jsonPrimitive
import java.util.UUID

/** Screen-time source v2 (ST1): which device counts a rule (all devices, the list, else where it was created) against the PC's CountsOn. */
class ScreenTimeCountsOnCheck : FixtureCheck {
    override val area = "screentime"
    override val name = "counts-on"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                val rule = GoalJson.readSourceRule(case.input.getValue("rule"))
                val actual = GoalAppMatching.countsOn(rule, UUID.fromString(case.text("deviceId")))
                val expected = case.expected.jsonPrimitive.boolean
                if (actual == expected) null else "kotlin $actual vs C# $expected"
            }
        }
    }
}
