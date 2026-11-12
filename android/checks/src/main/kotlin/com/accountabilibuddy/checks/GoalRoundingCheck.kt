package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalWhatItTakes
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.double
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put

/** GoalWhatItTakes.friendlyMinutes + minutesText against the PC's FriendlyMinutes + MinutesText (always rounded up). */
class GoalRoundingCheck : FixtureCheck {
    override val area = "goals"
    override val name = "rounding"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            val result = runCatching {
                val friendly = GoalWhatItTakes.friendlyMinutes(case.input.getValue("minutes").jsonPrimitive.double)
                JsonParity.difference(case.expected, buildJsonObject {
                    put("friendly", friendly)
                    put("text", GoalWhatItTakes.minutesText(friendly))
                })
            }
            val detail = result.getOrElse { "threw ${it.message}" }
            report.check("$area/$name: ${case.name}", result.isSuccess && detail == null, detail ?: "")
        }
    }
}
