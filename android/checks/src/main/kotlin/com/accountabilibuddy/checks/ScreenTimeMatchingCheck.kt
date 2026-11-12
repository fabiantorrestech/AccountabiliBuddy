package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalAppMatch
import com.accountabilibuddy.shared.goals.GoalAppMatchKind
import com.accountabilibuddy.shared.goals.GoalAppMatching
import com.accountabilibuddy.shared.goals.GoalAppPlatform
import com.accountabilibuddy.shared.goals.GoalAppRow
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.jsonPrimitive

/** Screen-time source v2 (ST1): GoalAppMatching.matches per platform against the PC's GoalAppMatching.Matches. */
class ScreenTimeMatchingCheck : FixtureCheck {
    override val area = "screentime"
    override val name = "matching"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            val match = GoalAppMatch(GoalAppMatchKind.valueOf(case.text("kind")), case.text("text"))
            val row = GoalAppRow(case.text("id"), nullableText(case.input, "label"), nullableText(case.input, "site"), 1.0)
            val actual = GoalAppMatching.matches(match, row, GoalAppPlatform.valueOf(case.text("platform")))
            val expected = case.expected.jsonPrimitive.boolean
            report.check("$area/$name: ${case.name}", actual == expected, "kotlin $actual vs C# $expected")
        }
    }

    companion object {
        fun nullableText(obj: JsonObject, key: String): String? = obj[key]?.takeIf { it !is JsonNull }?.jsonPrimitive?.content
    }
}
