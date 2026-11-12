package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalLowering
import kotlinx.serialization.json.double
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.time.Duration

/** GoalLowering.intoAccountabiliBuddy against the PC's IntoAccountabiliBuddy (a ProductaBuddy-made wait seen in AccountabiliBuddy mode). */
class GoalIntoAbCheck : FixtureCheck {
    override val area = "goals"
    override val name = "into-ab"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                val pending = GoalJson.readPendingChange(case.input.getValue("pending"))
                val delay = Duration.ofSeconds((case.input.getValue("delayMinutes").jsonPrimitive.double * 60).toLong())
                val result = GoalLowering.intoAccountabiliBuddy(pending, GoalJson.parseTime(case.text("now")), delay)
                GoalCaseRunner.sameText(case.expected.jsonObject.getValue("json").jsonPrimitive.content, GoalJson.text(GoalJson.writePendingChange(result)))
            }
        }
    }
}
