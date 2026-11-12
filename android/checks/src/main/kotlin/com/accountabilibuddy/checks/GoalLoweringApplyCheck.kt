package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalLowering
import com.accountabilibuddy.shared.goals.GoalPendingChange
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.jsonPrimitive

/** GoalLowering.canApply against the PC's GoalLowering.CanApply (the delay, sessions, the work-hours rule, PB-made waits). */
class GoalLoweringApplyCheck : FixtureCheck {
    override val area = "goals"
    override val name = "lowering-apply"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            val result = runCatching {
                fun flag(key: String) = case.input.getValue(key).jsonPrimitive.boolean
                val pending = GoalPendingChange(90.0, GoalJson.parseTime(case.text("appliesAt")), flag("madeInProductaBuddy"))
                val got = GoalLowering.canApply(pending, GoalJson.parseTime(case.text("now")), flag("sessionRunning"), flag("productaBuddy"), flag("lowerWaitsNow"))
                JsonParity.difference(case.expected, JsonPrimitive(got))
            }
            val detail = result.getOrElse { "threw ${it.message}" }
            report.check("$area/$name: ${case.name}", result.isSuccess && detail == null, detail ?: "")
        }
    }
}
