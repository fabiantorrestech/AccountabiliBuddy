package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalLabelId
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.util.UUID

/** GoalLabelId.idFor (and the lower-cased name it hashes) against the PC's TaskLabel.IdFor / ToLowerInvariant, odd scripts included (Q393). */
class GoalLabelIdsCheck : FixtureCheck {
    override val area = "goals"
    override val name = "label-ids"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name.map { if (it.code in 32..126) it else '?' }.joinToString("")}") {
                val name = case.text("name")
                val expected = case.expected.jsonObject
                val lower = expected.getValue("lower").jsonPrimitive.content
                val id = UUID.fromString(expected.getValue("id").jsonPrimitive.content)
                when {
                    GoalLabelId.normalized(name) != lower -> "lower-cased differently: ${GoalLabelId.normalized(name).map { it.code }} vs C# ${lower.map { it.code }}"
                    GoalLabelId.idFor(name) != id -> "kotlin ${GoalLabelId.idFor(name)} vs C# $id"
                    else -> null
                }
            }
        }
    }
}
