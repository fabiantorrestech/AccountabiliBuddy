package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalAge
import com.accountabilibuddy.shared.goals.GoalJson
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonPrimitive
import java.time.LocalDate
import java.time.ZoneOffset

/** GoalAge.forGoal against the PC's GoalAge.For ("6 months", "Since Mon 5/4/2026 · 191 days (27 weeks)"). */
class GoalAgeCheck : FixtureCheck {
    override val area = "goals"
    override val name = "age"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            val result = runCatching {
                val zone = ZoneOffset.ofTotalSeconds(case.input.getValue("zoneOffsetMinutes").jsonPrimitive.int * 60)
                val age = GoalAge.forGoal(GoalJson.readGoal(case.input.getValue("goal")), LocalDate.parse(case.text("today")), zone)
                JsonParity.difference(case.expected, GoalSnapshotJson.age(age))
            }
            val detail = result.getOrElse { "threw ${it.message}" }
            report.check("$area/$name: ${case.name}", result.isSuccess && detail == null, detail ?: "")
        }
    }
}
